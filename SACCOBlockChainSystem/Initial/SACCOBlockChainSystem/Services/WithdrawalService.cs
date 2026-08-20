using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface IWithdrawalService
    {
        // Withdrawal Management
        Task<MemberWithdrawal> CreateWithdrawalAsync(string memberNo, MemberWithdrawalDTO dto, string createdBy);
        Task<MemberWithdrawal> UpdateWithdrawalAsync(int id, MemberWithdrawalDTO dto, string modifiedBy);
        Task<bool> CancelWithdrawalAsync(int id, string reason, string cancelledBy);
        Task<bool> DeleteWithdrawalAsync(int id);

        // Approval Workflow
        Task<bool> ApproveWithdrawalAsync(int id, string approvedBy, string comments);
        Task<bool> RejectWithdrawalAsync(int id, string rejectedBy, string reason);
        Task<bool> ProcessPaymentAsync(int id, string processedBy, DateTime paymentDate, string paymentReference);

        // Retrieval
        Task<MemberWithdrawal> GetWithdrawalByIdAsync(int id);
        Task<MemberWithdrawal> GetWithdrawalByNoAsync(string withdrawalNo, string companyCode);
        Task<List<WithdrawalResponseDTO>> GetWithdrawalsByMemberAsync(string memberNo, string companyCode);
        Task<List<WithdrawalResponseDTO>> GetWithdrawalsByStatusAsync(string status, string companyCode);

        // Calculations
        Task<WithdrawalCalculationDTO> CalculateWithdrawalAmountAsync(string memberNo, string companyCode);
        Task<bool> CheckWithdrawalEligibilityAsync(string memberNo, string companyCode);

        // Blockchain
        Task<bool> RecordWithdrawalOnBlockchainAsync(int id);
    }

    public class WithdrawalService : IWithdrawalService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly IShareService _shareService;
        private readonly ILoanService _loanService;
        private readonly IMemberService _memberService;
        private readonly ILogger<WithdrawalService> _logger;
        private readonly ICompanyContextService _companyContextService;
        private readonly ISaccoService _saccoService;

        public WithdrawalService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            IShareService shareService,
            ILoanService loanService,
            IMemberService memberService,
            ILogger<WithdrawalService> logger,
            ICompanyContextService companyContextService,
            ISaccoService saccoService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _shareService = shareService;
            _loanService = loanService;
            _memberService = memberService;
            _logger = logger;
            _companyContextService = companyContextService;
            _saccoService = saccoService;
        }

        public async Task<MemberWithdrawal> CreateWithdrawalAsync(string memberNo, MemberWithdrawalDTO dto, string createdBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Verify member exists and is active
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

                if (member == null)
                {
                    throw new InvalidOperationException($"Member {memberNo} not found");
                }

                if (member.Withdrawn == true)
                {
                    throw new InvalidOperationException("Member has already withdrawn");
                }

                if (member.Archived == true)
                {
                    throw new InvalidOperationException("Archived members cannot withdraw");
                }

                // Get SACCO parameters
                var saccoParams = await _saccoService.GetSaccoParametersAsync(companyCode);

                // Check membership maturity
                if (member.ApplicDate.HasValue)
                {
                    var membershipMonths = (DateTime.Now - member.ApplicDate.Value).TotalDays / 30;
                    if (membershipMonths < saccoParams.MembershipMaturityMonths)
                    {
                        throw new InvalidOperationException($"Member must be a member for at least {saccoParams.MembershipMaturityMonths} months before withdrawal.");
                    }
                }

                // Check withdrawal notice period
                if (member.ApplicDate.HasValue)
                {
                    var noticeDate = member.ApplicDate.Value.AddDays(saccoParams.WithdrawalNoticeDays);
                    if (DateTime.Now < noticeDate)
                    {
                        var daysRemaining = (noticeDate - DateTime.Now).Days;
                        throw new InvalidOperationException($"Withdrawal requires {saccoParams.WithdrawalNoticeDays} days notice. " +
                                                           $"Notice period ends on {noticeDate:dd/MM/yyyy}. Please wait {daysRemaining} more day(s).");
                    }
                }

                // Check withdrawal eligibility
                var eligibility = await CheckWithdrawalEligibilityAsync(memberNo, companyCode);
                if (!eligibility)
                {
                    throw new InvalidOperationException("Member is not eligible for withdrawal. Please check outstanding loans.");
                }

                // Calculate withdrawal amounts
                var calculation = await CalculateWithdrawalAmountAsync(memberNo, companyCode);

                if (calculation.NetPayableAmount <= 0)
                {
                    throw new InvalidOperationException("Net payable amount is zero or negative. Cannot process withdrawal.");
                }

                // Generate withdrawal number
                var withdrawalNo = await GenerateWithdrawalNumberAsync(companyCode);

                // Get GL Accounts dynamically based on account types
                // Get Members' Equity Account (Source Account - where members' funds are held)
                var membersEquityAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                              g.Type == "EQUITY" &&
                                              g.SubType == "MEMBERS_EQUITY" &&
                                              g.Status == true);

                // If not found by SubType, try by AccCategory
                if (membersEquityAccount == null)
                {
                    membersEquityAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                                  g.AccCategory == "MEMBERS_EQUITY" &&
                                                  g.Status == true);
                }

                // If still not found, try by account type "EQUITY"
                if (membersEquityAccount == null)
                {
                    membersEquityAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                                  g.Type == "EQUITY" &&
                                                  g.Status == true);
                }

                // Get Cash/Bank Account (Destination Account - where payment will be made from)
                var cashBankAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                              g.Type == "ASSET" &&
                                              g.SubType == "CASH_BANK" &&
                                              g.Status == true);

                // If not found by SubType, try by AccCategory
                if (cashBankAccount == null)
                {
                    cashBankAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                                  g.AccCategory == "CASH" &&
                                                  g.Status == true);
                }

                // If still not found, try by account type "ASSET" with cash-related name
                if (cashBankAccount == null)
                {
                    cashBankAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.CompanyCode == companyCode &&
                                                  g.Type == "ASSET" &&
                                                  (g.Glaccname != null && (g.Glaccname.Contains("CASH") || g.Glaccname.Contains("BANK"))) &&
                                                  g.Status == true);
                }

                // If no cash/bank account found, throw meaningful error
                if (cashBankAccount == null)
                {
                    _logger.LogWarning($"No active cash/bank account found for company {companyCode}. Please configure GL accounts.");
                    // Don't throw - we can still create withdrawal without GL account
                }

                // Create withdrawal record
                var withdrawal = new MemberWithdrawal
                {
                    WithdrawalNo = withdrawalNo,
                    MemberNo = memberNo,
                    CompanyCode = companyCode,
                    WithdrawalDate = dto.WithdrawalDate,
                    WithdrawalType = dto.WithdrawalType,
                    Status = "Pending",
                    TotalSharesValue = calculation.TotalSharesValue,
                    TotalDeposits = calculation.TotalDeposits,
                    OutstandingLoans = calculation.OutstandingLoans,
                    PenaltiesAndDeductions = calculation.PenaltiesAndDeductions,
                    NetPayableAmount = calculation.NetPayableAmount,
                    PaymentMethod = dto.PaymentMethod,
                    BankName = dto.BankName,
                    BankAccountNo = dto.BankAccountNo,
                    AccountName = dto.AccountName,
                    ChequeNo = dto.ChequeNo,
                    MobileNo = dto.MobileNo,
                    Remarks = dto.Remarks,
                    DocumentPath = dto.DocumentPath,
                    GlAccountNo = membersEquityAccount?.AccNo, // Store the account number
                    GlAccountName = membersEquityAccount?.Glaccname,
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.Now
                };

                _context.MemberWithdrawals.Add(withdrawal);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(withdrawal, "CREATE", createdBy, member);

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal request {withdrawalNo} created for member {memberNo}");
                return withdrawal;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error creating withdrawal for member {memberNo}");
                throw;
            }
        }

        public async Task<bool> ProcessPaymentAsync(int id, string processedBy, DateTime paymentDate, string paymentReference)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals
                    .Include(w => w.Member)
                    .FirstOrDefaultAsync(w => w.Id == id);

                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Approved")
                {
                    throw new InvalidOperationException($"Cannot process payment for withdrawal in '{withdrawal.Status}' status");
                }

                // Verify payment date is not in future
                if (paymentDate > DateTime.Now)
                {
                    throw new InvalidOperationException("Payment date cannot be in the future");
                }

                // Verify payment reference is provided
                if (string.IsNullOrWhiteSpace(paymentReference))
                {
                    throw new InvalidOperationException("Payment reference is required");
                }

                // Update withdrawal
                var oldStatus = withdrawal.Status;
                withdrawal.Status = "Completed";
                withdrawal.PaymentDate = paymentDate;
                withdrawal.PaymentReference = paymentReference;
                withdrawal.ProcessedBy = processedBy;
                withdrawal.ProcessedDate = DateTime.Now;
                withdrawal.ModifiedBy = processedBy;
                withdrawal.ModifiedAt = DateTime.Now;

                // Update member status to withdrawn
                var member = withdrawal.Member;
                if (member != null)
                {
                    var oldWithdrawnStatus = member.Withdrawn;
                    member.Withdrawn = true;
                    member.Transferdate = paymentDate;
                    member.Status = 0; // Inactive
                    member.Memberwitrawaldate = paymentDate;
                    member.AuditDateTime = DateTime.Now;
                    member.AuditTime = DateTime.Now;

                    _context.Members.Update(member);
                }

                await _context.SaveChangesAsync();

                // Create GL transaction for the payment using dynamic account selection
                if (!string.IsNullOrEmpty(withdrawal.GlAccountNo))
                {
                    try
                    {
                        // Get the source account (Members' Equity)
                        var sourceAccount = await _context.GlSetup
                            .FirstOrDefaultAsync(g => g.AccNo == withdrawal.GlAccountNo &&
                                                      g.CompanyCode == withdrawal.CompanyCode &&
                                                      g.Status == true);

                        // Get the destination account (Cash/Bank) - dynamically select based on payment method
                        GlSetup destinationAccount = null;

                        // Select destination account based on payment method
                        if (withdrawal.PaymentMethod == "Bank Transfer")
                        {
                            destinationAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == withdrawal.CompanyCode &&
                                                          g.Type == "ASSET" &&
                                                          g.SubType == "BANK" &&
                                                          g.Status == true);
                        }
                        else if (withdrawal.PaymentMethod == "Cash")
                        {
                            destinationAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == withdrawal.CompanyCode &&
                                                          g.Type == "ASSET" &&
                                                          g.SubType == "CASH" &&
                                                          g.Status == true);
                        }
                        else if (withdrawal.PaymentMethod == "Mobile Money")
                        {
                            destinationAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == withdrawal.CompanyCode &&
                                                          g.Type == "ASSET" &&
                                                          g.SubType == "MOBILE_MONEY" &&
                                                          g.Status == true);
                        }
                        else
                        {
                            // Default to cash/bank account
                            destinationAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == withdrawal.CompanyCode &&
                                                          g.Type == "ASSET" &&
                                                          (g.SubType == "CASH_BANK" || g.SubType == "CASH") &&
                                                          g.Status == true);
                        }

                        // If still no destination account found, try by AccCategory
                        if (destinationAccount == null)
                        {
                            destinationAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == withdrawal.CompanyCode &&
                                                          g.AccCategory == "CASH" &&
                                                          g.Status == true);
                        }

                        if (sourceAccount != null && destinationAccount != null)
                        {
                            var glTransaction = new Gltransaction
                            {
                                TransDate = paymentDate,
                                Amount = withdrawal.NetPayableAmount,
                                DrAccNo = sourceAccount.AccNo,      // Debit Members' Equity (reducing liability)
                                CrAccNo = destinationAccount.AccNo,  // Credit Cash/Bank (reducing asset)
                                Temp = "WITHDRAWAL",
                                DocumentNo = withdrawal.WithdrawalNo,
                                Source = "MEMBER_WITHDRAWAL",
                                CompanyCode = withdrawal.CompanyCode,
                                TransDescript = $"Member withdrawal - {withdrawal.MemberNo} - {member?.Surname} {member?.OtherNames}",
                                AuditTime = DateTime.Now,
                                AuditId = processedBy,
                                Cash = 0,
                                DocPosted = 1,
                                Module = "MEMBER",
                                ReconId = 0
                            };
                            _context.Gltransactions.Add(glTransaction);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"GL Transaction created for withdrawal {withdrawal.WithdrawalNo}");
                        }
                        else
                        {
                            _logger.LogWarning($"Could not create GL transaction: Source Account: {sourceAccount?.AccNo}, Destination Account: {destinationAccount?.AccNo}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error creating GL transaction for withdrawal");
                        // Don't throw - GL transaction is not critical for withdrawal completion
                    }
                }

                // Record blockchain transaction with all final details
                await RecordBlockchainTransaction(withdrawal, "COMPLETE", processedBy, member, new
                {
                    OldStatus = oldStatus,
                    PaymentDate = paymentDate,
                    PaymentReference = paymentReference,
                    MemberWithdrawn = true,
                    FinalAmount = withdrawal.NetPayableAmount
                });

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} payment processed. Member {member?.MemberNo} marked as withdrawn.");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error processing payment for withdrawal {id}");
                throw;
            }
        }
        private async Task<GlSetup> GetGlAccountByTypeAsync(string companyCode, string accountType, string subType = null, string category = null)
        {
            var query = _context.GlSetup
                .Where(g => g.CompanyCode == companyCode && g.Status == true);

            if (!string.IsNullOrEmpty(accountType))
            {
                query = query.Where(g => g.Type == accountType);
            }

            if (!string.IsNullOrEmpty(subType))
            {
                query = query.Where(g => g.SubType == subType);
            }

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(g => g.AccCategory == category);
            }

            return await query.FirstOrDefaultAsync();
        }

        private async Task<string> GenerateWithdrawalNumberAsync(string companyCode)
        {
            var prefix = "WDR";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastWithdrawal = await _context.MemberWithdrawals
                .Where(w => w.CompanyCode == companyCode && w.WithdrawalNo.StartsWith($"{prefix}{date}"))
                .OrderByDescending(w => w.WithdrawalNo)
                .FirstOrDefaultAsync();

            if (lastWithdrawal != null && lastWithdrawal.WithdrawalNo.Length > 11)
            {
                var sequenceStr = lastWithdrawal.WithdrawalNo.Substring(11);
                if (int.TryParse(sequenceStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        private async Task RecordBlockchainTransaction(MemberWithdrawal withdrawal, string action, string performedBy, Member member, object additionalData = null)
        {
            try
            {
                var blockchainData = new
                {
                    Id = withdrawal.Id,
                    WithdrawalNo = withdrawal.WithdrawalNo,
                    MemberNo = withdrawal.MemberNo,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : "Unknown",
                    MemberIdNumber = member?.Idno,
                    MemberPhone = member?.PhoneNo,
                    MemberEmail = member?.Email,
                    WithdrawalType = withdrawal.WithdrawalType,
                    WithdrawalDate = withdrawal.WithdrawalDate,
                    TotalSharesValue = withdrawal.TotalSharesValue,
                    TotalDeposits = withdrawal.TotalDeposits,
                    OutstandingLoans = withdrawal.OutstandingLoans,
                    PenaltiesAndDeductions = withdrawal.PenaltiesAndDeductions,
                    NetPayableAmount = withdrawal.NetPayableAmount,
                    PaymentMethod = withdrawal.PaymentMethod,
                    BankName = withdrawal.BankName,
                    BankAccountNo = withdrawal.BankAccountNo,
                    AccountName = withdrawal.AccountName,
                    ChequeNo = withdrawal.ChequeNo,
                    MobileNo = withdrawal.MobileNo,
                    GlAccountNo = withdrawal.GlAccountNo,
                    GlAccountName = withdrawal.GlAccountName,
                    Status = withdrawal.Status,
                    Action = action,
                    PerformedBy = performedBy,
                    PerformedAt = DateTime.Now,
                    AdditionalData = additionalData,
                    CompanyCode = withdrawal.CompanyCode
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    $"MEMBER_WITHDRAWAL_{action}",
                    withdrawal.MemberNo,
                    withdrawal.CompanyCode,
                    withdrawal.NetPayableAmount,
                    withdrawal.WithdrawalNo,
                    blockchainData);

                if (blockchainTx != null)
                {
                    withdrawal.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Blockchain transaction recorded for withdrawal {action}: {blockchainTx.TransactionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to record blockchain transaction for withdrawal {action}");
            }
        }

        public async Task<MemberWithdrawal> UpdateWithdrawalAsync(int id, MemberWithdrawalDTO dto, string modifiedBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals
                    .Include(w => w.Member)
                    .FirstOrDefaultAsync(w => w.Id == id);

                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Pending")
                {
                    throw new InvalidOperationException($"Cannot update withdrawal in '{withdrawal.Status}' status");
                }

                // Store old values for audit
                var oldValues = new
                {
                    withdrawal.WithdrawalType,
                    withdrawal.WithdrawalDate,
                    withdrawal.PaymentMethod,
                    withdrawal.BankName,
                    withdrawal.BankAccountNo,
                    withdrawal.AccountName,
                    withdrawal.ChequeNo,
                    withdrawal.MobileNo,
                    withdrawal.Remarks,
                    withdrawal.DocumentPath
                };

                // Update fields
                withdrawal.WithdrawalType = dto.WithdrawalType;
                withdrawal.WithdrawalDate = dto.WithdrawalDate;
                withdrawal.PaymentMethod = dto.PaymentMethod;
                withdrawal.BankName = dto.BankName;
                withdrawal.BankAccountNo = dto.BankAccountNo;
                withdrawal.AccountName = dto.AccountName;
                withdrawal.ChequeNo = dto.ChequeNo;
                withdrawal.MobileNo = dto.MobileNo;
                withdrawal.Remarks = dto.Remarks;
                withdrawal.DocumentPath = dto.DocumentPath;
                withdrawal.ModifiedBy = modifiedBy;
                withdrawal.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction with audit trail
                await RecordBlockchainTransaction(withdrawal, "UPDATE", modifiedBy, withdrawal.Member, oldValues);

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} updated");
                return withdrawal;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating withdrawal {id}");
                throw;
            }
        }

        public async Task<bool> CancelWithdrawalAsync(int id, string reason, string cancelledBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals
                    .Include(w => w.Member)
                    .FirstOrDefaultAsync(w => w.Id == id);

                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Pending" && withdrawal.Status != "Approved")
                {
                    throw new InvalidOperationException($"Cannot cancel withdrawal in '{withdrawal.Status}' status");
                }

                var oldStatus = withdrawal.Status;
                withdrawal.Status = "Cancelled";
                withdrawal.Remarks = $"Cancelled: {reason}";
                withdrawal.ModifiedBy = cancelledBy;
                withdrawal.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(withdrawal, "CANCEL", cancelledBy, withdrawal.Member, new { OldStatus = oldStatus, Reason = reason });

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} cancelled");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error cancelling withdrawal {id}");
                throw;
            }
        }

        public async Task<bool> DeleteWithdrawalAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals.FindAsync(id);
                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Pending")
                {
                    throw new InvalidOperationException($"Cannot delete withdrawal in '{withdrawal.Status}' status");
                }

                _context.MemberWithdrawals.Remove(withdrawal);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} deleted");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error deleting withdrawal {id}");
                throw;
            }
        }

        public async Task<bool> ApproveWithdrawalAsync(int id, string approvedBy, string comments)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals
                    .Include(w => w.Member)
                    .FirstOrDefaultAsync(w => w.Id == id);

                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Pending")
                {
                    throw new InvalidOperationException($"Cannot approve withdrawal in '{withdrawal.Status}' status");
                }

                // Verify eligibility again before approval
                var eligibility = await CheckWithdrawalEligibilityAsync(withdrawal.MemberNo, withdrawal.CompanyCode);
                if (!eligibility)
                {
                    throw new InvalidOperationException("Member is no longer eligible for withdrawal");
                }

                // Verify notice period again
                var saccoParams = await _saccoService.GetSaccoParametersAsync(withdrawal.CompanyCode);
                if (withdrawal.Member.ApplicDate.HasValue)
                {
                    var noticeDate = withdrawal.Member.ApplicDate.Value.AddDays(saccoParams.WithdrawalNoticeDays);
                    if (DateTime.Now < noticeDate)
                    {
                        throw new InvalidOperationException($"Cannot approve withdrawal before notice period ends on {noticeDate:dd/MM/yyyy}");
                    }
                }

                // Create approval record
                var approval = new WithdrawalApproval
                {
                    WithdrawalId = withdrawal.Id,
                    WithdrawalNo = withdrawal.WithdrawalNo,
                    CompanyCode = withdrawal.CompanyCode,
                    ApprovalLevel = "Level1",
                    ApprovalStatus = "Approved",
                    ApprovedBy = approvedBy,
                    ApprovalDate = DateTime.Now,
                    Comments = comments
                };

                _context.WithdrawalApprovals.Add(approval);

                // Update withdrawal status
                var oldStatus = withdrawal.Status;
                withdrawal.Status = "Approved";
                withdrawal.ApprovedBy = approvedBy;
                withdrawal.ApprovalDate = DateTime.Now;
                withdrawal.ApprovalComments = comments;
                withdrawal.ModifiedBy = approvedBy;
                withdrawal.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(withdrawal, "APPROVE", approvedBy, withdrawal.Member, new { OldStatus = oldStatus, Comments = comments });

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} approved by {approvedBy}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error approving withdrawal {id}");
                throw;
            }
        }

        public async Task<bool> RejectWithdrawalAsync(int id, string rejectedBy, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var withdrawal = await _context.MemberWithdrawals
                    .Include(w => w.Member)
                    .FirstOrDefaultAsync(w => w.Id == id);

                if (withdrawal == null)
                {
                    throw new InvalidOperationException($"Withdrawal with ID {id} not found");
                }

                if (withdrawal.Status != "Pending")
                {
                    throw new InvalidOperationException($"Cannot reject withdrawal in '{withdrawal.Status}' status");
                }

                var oldStatus = withdrawal.Status;
                withdrawal.Status = "Rejected";
                withdrawal.ApprovalComments = reason;
                withdrawal.ModifiedBy = rejectedBy;
                withdrawal.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(withdrawal, "REJECT", rejectedBy, withdrawal.Member, new { OldStatus = oldStatus, Reason = reason });

                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawal {withdrawal.WithdrawalNo} rejected by {rejectedBy}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error rejecting withdrawal {id}");
                throw;
            }
        }

        public async Task<MemberWithdrawal> GetWithdrawalByIdAsync(int id)
        {
            return await _context.MemberWithdrawals
                .Include(w => w.Member)
                .Include(w => w.Approvals)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<MemberWithdrawal> GetWithdrawalByNoAsync(string withdrawalNo, string companyCode)
        {
            return await _context.MemberWithdrawals
                .Include(w => w.Member)
                .Include(w => w.Approvals)
                .FirstOrDefaultAsync(w => w.WithdrawalNo == withdrawalNo && w.CompanyCode == companyCode);
        }

        public async Task<List<WithdrawalResponseDTO>> GetWithdrawalsByMemberAsync(string memberNo, string companyCode)
        {
            return await _context.MemberWithdrawals
                .Where(w => w.MemberNo == memberNo && w.CompanyCode == companyCode)
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new WithdrawalResponseDTO
                {
                    Id = w.Id,
                    WithdrawalNo = w.WithdrawalNo,
                    MemberNo = w.MemberNo,
                    MemberName = w.Member != null ? $"{w.Member.Surname} {w.Member.OtherNames}".Trim() : "",
                    WithdrawalDate = w.WithdrawalDate,
                    WithdrawalType = w.WithdrawalType,
                    Status = w.Status,
                    TotalSharesValue = w.TotalSharesValue,
                    OutstandingLoans = w.OutstandingLoans,
                    NetPayableAmount = w.NetPayableAmount,
                    PaymentMethod = w.PaymentMethod,
                    PaymentDate = w.PaymentDate,
                    ApprovedBy = w.ApprovedBy,
                    ApprovalDate = w.ApprovalDate,
                    BlockchainTxId = w.BlockchainTxId,
                    CreatedAt = w.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<WithdrawalResponseDTO>> GetWithdrawalsByStatusAsync(string status, string companyCode)
        {
            var query = _context.MemberWithdrawals
                .Include(w => w.Member)
                .Where(w => w.CompanyCode == companyCode);

            if (status != "All")
            {
                query = query.Where(w => w.Status == status);
            }

            return await query
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new WithdrawalResponseDTO
                {
                    Id = w.Id,
                    WithdrawalNo = w.WithdrawalNo,
                    MemberNo = w.MemberNo,
                    MemberName = w.Member != null ? $"{w.Member.Surname} {w.Member.OtherNames}".Trim() : "",
                    WithdrawalDate = w.WithdrawalDate,
                    WithdrawalType = w.WithdrawalType,
                    Status = w.Status,
                    TotalSharesValue = w.TotalSharesValue,
                    OutstandingLoans = w.OutstandingLoans,
                    NetPayableAmount = w.NetPayableAmount,
                    PaymentMethod = w.PaymentMethod,
                    PaymentDate = w.PaymentDate,
                    ApprovedBy = w.ApprovedBy,
                    ApprovalDate = w.ApprovalDate,
                    BlockchainTxId = w.BlockchainTxId,
                    CreatedAt = w.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<WithdrawalCalculationDTO> CalculateWithdrawalAmountAsync(string memberNo, string companyCode)
        {
            try
            {
                // Get member's total shares value from all share types
                var shares = await _context.Shares
                    .Where(s => s.MemberNo == memberNo && s.CompanyCode == companyCode)
                    .ToListAsync();

                var totalSharesValue = shares.Sum(s => s.TotalShares ?? 0);

                // Get member's total deposits (contributions) - ONLY contributions, not loans
                var totalDeposits = await _context.Contribs
                    .Where(c => c.MemberNo == memberNo && c.CompanyCode == companyCode)
                    .SumAsync(c => c.Amount ?? 0);

                // Get outstanding loans - loans that are not fully repaid
                var outstandingLoans = await _context.Loans
                    .Where(l => l.MemberNo == memberNo &&
                               l.CompanyCode == companyCode &&
                               l.Status != (int)Status.Closed &&
                               l.Status != (int)Status.WrittenOff &&
                               l.Status != (int)Status.Rejected)
                    .Join(_context.Loanbal,
                          l => l.LoanNo,
                          lb => lb.LoanNo,
                          (l, lb) => lb)
                    .SumAsync(lb => lb.Balance + lb.IntrOwed + lb.Penalty);

                // Get any penalties from overdue loans
                var penalties = await _context.Loans
                    .Where(l => l.MemberNo == memberNo &&
                               l.CompanyCode == companyCode &&
                               l.Status != (int)Status.Closed &&
                               l.Status != (int)Status.WrittenOff)
                    .Join(_context.Loanbal,
                          l => l.LoanNo,
                          lb => lb.LoanNo,
                          (l, lb) => lb)
                    .SumAsync(lb => lb.Penalty);

                // Calculate any early withdrawal penalties (if applicable)
                var saccoParams = await _saccoService.GetSaccoParametersAsync(companyCode);
                decimal earlyWithdrawalPenalty = 0;

                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

                if (member?.ApplicDate.HasValue == true)
                {
                    var membershipMonths = (DateTime.Now - member.ApplicDate.Value).TotalDays / 30;
                    if (membershipMonths < saccoParams.MembershipMaturityMonths)
                    {
                        // Apply penalty for early withdrawal (e.g., 10% of shares)
                        earlyWithdrawalPenalty = totalSharesValue * 0.10m;
                    }
                }

                var totalPenalties = penalties + earlyWithdrawalPenalty;

                // Check if member has outstanding loans
                var hasOutstandingLoans = outstandingLoans > 0;

                // Net payable amount = shares + deposits - loans - penalties
                var netPayableAmount = totalSharesValue + totalDeposits - outstandingLoans - totalPenalties;

                // Build eligibility message
                var eligibilityMessage = "";
                if (hasOutstandingLoans)
                {
                    eligibilityMessage = $"Member has outstanding loans of KES {outstandingLoans:N0}. " +
                                        "All loans must be cleared before withdrawal.";
                }
                else if (totalSharesValue <= 0 && totalDeposits <= 0)
                {
                    eligibilityMessage = "Member has no shares or deposits to withdraw.";
                }
                else if (netPayableAmount <= 0)
                {
                    eligibilityMessage = $"Net payable amount is KES {netPayableAmount:N0}. " +
                                        "Cannot process withdrawal with negative or zero amount.";
                }
                else
                {
                    eligibilityMessage = "Member is eligible for withdrawal.";
                }

                return new WithdrawalCalculationDTO
                {
                    TotalSharesValue = totalSharesValue,
                    TotalDeposits = totalDeposits,
                    OutstandingLoans = outstandingLoans,
                    PenaltiesAndDeductions = totalPenalties,
                    NetPayableAmount = Math.Max(0, netPayableAmount),
                    HasOutstandingLoans = hasOutstandingLoans,
                    IsEligibleForWithdrawal = !hasOutstandingLoans && netPayableAmount > 0 && totalSharesValue + totalDeposits > 0,
                    EligibilityMessage = eligibilityMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating withdrawal amount for member {memberNo}");
                throw;
            }
        }

        public async Task<bool> CheckWithdrawalEligibilityAsync(string memberNo, string companyCode)
        {
            var calculation = await CalculateWithdrawalAmountAsync(memberNo, companyCode);
            return calculation.IsEligibleForWithdrawal && calculation.NetPayableAmount > 0;
        }

        public async Task<bool> RecordWithdrawalOnBlockchainAsync(int id)
        {
            try
            {
                var withdrawal = await GetWithdrawalByIdAsync(id);
                if (withdrawal == null)
                {
                    return false;
                }

                await RecordBlockchainTransaction(withdrawal, "VERIFY", "SYSTEM", withdrawal.Member);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording withdrawal {id} on blockchain");
                return false;
            }
        }
    }
}