using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.Security.Claims;
using System.Text.Json;

namespace SACCOBlockChainSystem.Services
{
    public class LoanService : ILoanService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LoanService> _logger;
        private readonly IBlockchainService _blockchainService;
        private readonly IMemberService _memberService;
        private readonly ILoanTypeService _loanTypeService;
        private readonly IShareService _shareService;
        private readonly AuditTrailService _auditService;
        private readonly IHttpContextAccessor _httpContextAccessor; 

        public LoanService(
            ApplicationDbContext context,
            ILogger<LoanService> logger,
            IBlockchainService blockchainService,
            IMemberService memberService,
            ILoanTypeService loanTypeService,
            AuditTrailService auditService,
            IShareService shareService,
            IHttpContextAccessor httpContextAccessor) 
        {
            _context = context;
            _logger = logger;
            _blockchainService = blockchainService;
            _memberService = memberService;
            _loanTypeService = loanTypeService;
            _shareService = shareService;
            _auditService = auditService;
            _httpContextAccessor = httpContextAccessor; 
        }


        #region Loan Deletion - Permanent Delete

        public async Task<bool> DeleteLoanAsync(string loanNo, string companyCode, string deletedBy, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Permanently deleting loan {loanNo} for company {companyCode}. Reason: {reason}");

                // 1. Get the loan to verify it exists and get member info for blockchain
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    throw new InvalidOperationException($"Loan {loanNo} not found");
                }

                _logger.LogInformation($"Found loan - Status: {loan.Status}, Amount: {loan.LoanAmt:C}");

                // Store loan info for blockchain before deletion
                var loanInfo = new
                {
                    loan.LoanNo,
                    loan.MemberNo,
                    loan.LoanAmt,
                    loan.Status,
                    loan.ApplicDate,
                    DeletedBy = deletedBy,
                    Reason = reason,
                    DeletedAt = DateTime.Now
                };

                // ============================================================
                // 2. DELETE FROM LOANGUAR (Member Guarantors)
                // ============================================================
                var memberGuarantors = await _context.Loanguar
                    .Where(g => g.LoanNo == loanNo)
                    .ToListAsync();

                if (memberGuarantors.Any())
                {
                    _logger.LogInformation($"Deleting {memberGuarantors.Count} member guarantor records");
                    _context.Loanguar.RemoveRange(memberGuarantors);
                }

                // ============================================================
                // 3. DELETE FROM COLLOANGUAR (Collateral Guarantees)
                // ============================================================
                var collateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo)
                    .ToListAsync();

                if (collateralGuarantees.Any())
                {
                    _logger.LogInformation($"Deleting {collateralGuarantees.Count} collateral guarantee records");
                    _context.ColloanGuars.RemoveRange(collateralGuarantees);
                }

                // ============================================================
                // 4. DELETE FROM APPRAISAL
                // ============================================================
                var appraisal = await _context.Appraisal
                    .FirstOrDefaultAsync(a => a.LoanNo == loanNo);

                if (appraisal != null)
                {
                    _logger.LogInformation($"Deleting appraisal record for loan {loanNo}");
                    _context.Appraisal.Remove(appraisal);
                }

                // ============================================================
                // 5. DELETE FROM ENDMAIN (Endorsement)
                // ============================================================
                var endorsement = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);

                if (endorsement != null)
                {
                    _logger.LogInformation($"Deleting endorsement record for loan {loanNo}");
                    _context.Endmain.Remove(endorsement);
                }

                // ============================================================
                // 6. DELETE FROM CHEQUES
                // ============================================================
                var cheque = await _context.Cheques
                    .FirstOrDefaultAsync(c => c.LoanNo == loanNo && c.CompanyCode == companyCode);

                if (cheque != null)
                {
                    _logger.LogInformation($"Deleting cheque record for loan {loanNo}");
                    _context.Cheques.Remove(cheque);
                }

                // ============================================================
                // 7. DELETE FROM LOANBAL (Loan Balance)
                // ============================================================
                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);

                if (loanbal != null)
                {
                    _logger.LogInformation($"Deleting loan balance record for loan {loanNo}");
                    _context.Loanbal.Remove(loanbal);
                }

                // ============================================================
                // 8. DELETE FROM LOANSCHEDULE (Repayment Schedule)
                // ============================================================
                var schedules = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo)
                    .ToListAsync();

                if (schedules.Any())
                {
                    _logger.LogInformation($"Deleting {schedules.Count} schedule records for loan {loanNo}");
                    _context.LoanSchedules.RemoveRange(schedules);
                }

                // ============================================================
                // 9. DELETE FROM REPAY (Repayment Records)
                // ============================================================
                var repayments = await _context.Repay
                    .Where(r => r.LoanNo == loanNo)
                    .ToListAsync();

                if (repayments.Any())
                {
                    _logger.LogInformation($"Deleting {repayments.Count} repayment records for loan {loanNo}");
                    _context.Repay.RemoveRange(repayments);
                }

                // ============================================================
                // 10. DELETE ANY GLTRANSACTIONS related to this loan
                // ============================================================
                // Get voucher numbers from cheque if exists
                string voucherNo = cheque?.Voucherno;

                var glTransactions = await _context.Gltransactions
                    .Where(g => g.DocumentNo == voucherNo || g.TransDescript.Contains(loanNo))
                    .ToListAsync();

                if (glTransactions.Any())
                {
                    _logger.LogInformation($"Deleting {glTransactions.Count} GL transaction records for loan {loanNo}");
                    _context.Gltransactions.RemoveRange(glTransactions);
                }

                // ============================================================
                // 11. RECORD BLOCKCHAIN TRANSACTION BEFORE DELETING THE LOAN
                // ============================================================
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_PERMANENTLY_DELETED",
                    MemberNo = loan.MemberNo,
                    CompanyCode = companyCode,
                    Amount = loan.LoanAmt ?? 0,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(loanInfo),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(loanInfo),
                    OffChainReferenceId = loanNo,
                    Status = "CONFIRMED",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // ============================================================
                // 12. FINALLY, DELETE THE LOAN ITSELF
                // ============================================================
                _logger.LogInformation($"Deleting loan {loanNo} from Loans table");
                _context.Loans.Remove(loan);

                // Save all changes
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Loan {loanNo} PERMANENTLY DELETED successfully. " +
                    $"Removed: {memberGuarantors.Count} guarantors, " +
                    $"{collateralGuarantees.Count} collateral, " +
                    $"{schedules.Count} schedule entries, " +
                    $"{repayments.Count} repayments, " +
                    $"{glTransactions.Count} GL transactions");

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error permanently deleting loan {loanNo}");
                throw;
            }
        }

        #endregion

        #region Loan Application

        public async Task<Loan> ApplyForLoanAsync(LoanApplicationDTO application)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var validation = await ValidateLoanApplicationAsync(application);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Loan validation failed: {validation.Message}");
                }

                var eligibility = await CheckMemberEligibilityAsync(application.MemberNo, application.LoanCode, application.CompanyCode);
                if (!eligibility.IsEligible)
                {
                    throw new InvalidOperationException($"Member not eligible: {eligibility.Message}");
                }

                // Fetch the member to get IdNo and other details
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == application.MemberNo && m.CompanyCode == application.CompanyCode);

                if (member == null)
                {
                    throw new InvalidOperationException($"Member {application.MemberNo} not found");
                }

                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(l => l.LoanCode == application.LoanCode && l.CompanyCode == application.CompanyCode);

                if (loanType == null)
                {
                    throw new InvalidOperationException($"Loan type {application.LoanCode} not found");
                }

                // Validate that the requested repayment period does not exceed the loan type's maximum
                var maxRepayPeriod = loanType.RepayPeriod ?? 360; // Default to 360 months (30 years) if not set
                if (application.RepayPeriod > maxRepayPeriod)
                {
                    throw new InvalidOperationException($"Repayment period of {application.RepayPeriod} months exceeds the maximum allowed of {maxRepayPeriod} months for this loan type.");
                }

                if (application.RepayPeriod < 1)
                {
                    throw new InvalidOperationException("Repayment period must be at least 1 month.");
                }

                var loanNo = await GenerateLoanNumberAsync(loanType.LoanCode, application.MemberNo, application.CompanyCode);

                decimal interestRate = 0;
                if (!string.IsNullOrEmpty(loanType.Interest) && decimal.TryParse(loanType.Interest, out interestRate))
                {
                    interestRate = Math.Round(interestRate, 4, MidpointRounding.AwayFromZero);
                }

                var loan = new Loan
                {
                    LoanNo = loanNo,
                    MemberNo = application.MemberNo,
                    LoanCode = application.LoanCode,
                    CompanyCode = application.CompanyCode,
                    LoanAmt = application.PrincipalAmount,
                    MaxLoanamt = application.PrincipalAmount,
                    IdNo = member.Idno,
                    Interest = interestRate,
                    RepayPeriod = application.RepayPeriod,
                    ApplicDate = application.ApplicationDate,
                    Status = (int)Status.Draft,
                    Purpose = application.Purpose,
                    AddSecurity = application.Remarks,
                    Guaranteed = ParseRequiredGuarantors(loanType.Guarantor).ToString(),
                    RepayMethod = loanType.Repaymethod ?? "AMT",
                    BasicSalary = 0,
                    Repayrate = 0,
                    Sharecapital = 0,
                    Run = 0,
                    Run2 = 0,
                    AuditTime = DateTime.Now,
                    Posted = "Draft",
                    UserName = application.CreatedBy,
                    AuditDateTime = DateTime.Now,
                    BlockchainTxId = null
                };

                _context.Loans.Add(loan);
                await _context.SaveChangesAsync();

                // Store guarantors data for audit
                var guarantorsList = new List<object>();
                if (application.Guarantors != null && application.Guarantors.Any())
                {
                    foreach (var guarantor in application.Guarantors)
                    {
                        var loanGuarantor = new Loanguar
                        {
                            LoanNo = loanNo,
                            MemberNo = guarantor.GuarantorMemberNo,
                            Amount = guarantor.GuaranteeAmount,
                            Balance = guarantor.GuaranteeAmount,
                            CompanyCode = application.CompanyCode,
                            AuditTime = DateTime.Now,
                            Transfered = false
                        };
                        _context.Loanguar.Add(loanGuarantor);

                        guarantorsList.Add(new
                        {
                            guarantor.GuarantorMemberNo,
                            guarantor.GuaranteeAmount,
                            guarantor.GuarantorName
                        });
                    }
                    await _context.SaveChangesAsync();
                }

                // ============================================================
                // CREATE BLOCKCHAIN TRANSACTION
                // ============================================================
                var blockchainData = new
                {
                    LoanNo = loanNo,
                    MemberNo = application.MemberNo,
                    MemberIdNo = member.Idno,
                    MemberName = $"{member.Surname} {member.OtherNames}",
                    LoanCode = application.LoanCode,
                    LoanTypeName = loanType.LoanType1,
                    PrincipalAmount = application.PrincipalAmount,
                    InterestRate = interestRate,
                    RepayPeriod = application.RepayPeriod,
                    MaxRepayPeriodAllowed = maxRepayPeriod,
                    RepayMethod = loanType.Repaymethod ?? "AMT",
                    ApplicationDate = application.ApplicationDate,
                    Purpose = application.Purpose,
                    Remarks = application.Remarks,
                    Guarantors = guarantorsList,
                    CreatedBy = application.CreatedBy,
                    Status = "Draft"
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_APPLICATION",
                    MemberNo = application.MemberNo,
                    CompanyCode = application.CompanyCode,
                    Amount = application.PrincipalAmount,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = loanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                loan.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                // ============================================================
                // SAVE AUDIT TRAIL
                // ============================================================

                // Create audit extra data
                var auditExtraData = new
                {
                    loanNo = loanNo,
                    memberNumber = application.MemberNo,
                    memberName = $"{member.Surname} {member.OtherNames}",
                    memberIdNo = member.Idno,
                    loanCode = application.LoanCode,
                    loanTypeName = loanType.LoanType1,
                    principalAmount = application.PrincipalAmount,
                    interestRate = interestRate,
                    repayPeriod = application.RepayPeriod,
                    maxRepayPeriodAllowed = maxRepayPeriod,
                    repayMethod = loanType.Repaymethod ?? "AMT",
                    applicationDate = application.ApplicationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    purpose = application.Purpose ?? "",
                    remarks = application.Remarks ?? "",
                    numberOfGuarantors = guarantorsList.Count,
                    guarantors = guarantorsList,
                    status = "Draft",
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Create a copy of the loan object for NewValue (what was just saved)
                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.MemberNo,
                    loan.LoanCode,
                    loan.LoanAmt,
                    loan.MaxLoanamt,
                    loan.IdNo,
                    loan.Interest,
                    loan.RepayPeriod,
                    loan.ApplicDate,
                    loan.Status,
                    loan.Purpose,
                    loan.AddSecurity,
                    loan.Guaranteed,
                    loan.RepayMethod,
                    loan.Posted,
                    loan.UserName,
                    loan.CompanyCode,
                    BlockchainTxId = blockchainTx.TransactionId,
                    CreatedAt = DateTime.Now,
                    CreatedBy = application.CreatedBy
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,  // For Insert, OldValue is null (nothing existed before)
                    newModel: loanForAudit,  // This will be serialized to NewValue column
                    tableName: "Loans",
                    recordId: loanNo,
                    userId: application.CreatedBy,
                    userName: application.CreatedBy,
                    companyCode: application.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                await transaction.CommitAsync();

                _logger.LogInformation($"Loan application {loanNo} created successfully for member {application.MemberNo}");

                return loan;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating loan application");
                throw;
            }
        }

        //public async Task<Loan> ApplyForLoanAsync(LoanApplicationDTO application)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        var validation = await ValidateLoanApplicationAsync(application);
        //        if (!validation.IsValid)
        //        {
        //            throw new InvalidOperationException($"Loan validation failed: {validation.Message}");
        //        }

        //        var eligibility = await CheckMemberEligibilityAsync(application.MemberNo, application.LoanCode, application.CompanyCode);
        //        if (!eligibility.IsEligible)
        //        {
        //            throw new InvalidOperationException($"Member not eligible: {eligibility.Message}");
        //        }

        //        // Fetch the member to get IdNo and other details
        //        var member = await _context.Members
        //            .FirstOrDefaultAsync(m => m.MemberNo == application.MemberNo && m.CompanyCode == application.CompanyCode);

        //        if (member == null)
        //        {
        //            throw new InvalidOperationException($"Member {application.MemberNo} not found");
        //        }

        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(l => l.LoanCode == application.LoanCode && l.CompanyCode == application.CompanyCode);

        //        if (loanType == null)
        //        {
        //            throw new InvalidOperationException($"Loan type {application.LoanCode} not found");
        //        }

        //        // Validate that the requested repayment period does not exceed the loan type's maximum
        //        var maxRepayPeriod = loanType.RepayPeriod ?? 360; // Default to 360 months (30 years) if not set
        //        if (application.RepayPeriod > maxRepayPeriod)
        //        {
        //            throw new InvalidOperationException($"Repayment period of {application.RepayPeriod} months exceeds the maximum allowed of {maxRepayPeriod} months for this loan type.");
        //        }

        //        if (application.RepayPeriod < 1)
        //        {
        //            throw new InvalidOperationException("Repayment period must be at least 1 month.");
        //        }

        //        var loanNo = await GenerateLoanNumberAsync(loanType.LoanCode, application.MemberNo, application.CompanyCode);

        //        decimal interestRate = 0;
        //        if (!string.IsNullOrEmpty(loanType.Interest) && decimal.TryParse(loanType.Interest, out interestRate))
        //        {
        //            interestRate = Math.Round(interestRate, 4, MidpointRounding.AwayFromZero);
        //        }

        //        var loan = new Loan
        //        {
        //            LoanNo = loanNo,
        //            MemberNo = application.MemberNo,
        //            LoanCode = application.LoanCode,
        //            CompanyCode = application.CompanyCode,
        //            LoanAmt = application.PrincipalAmount,
        //            MaxLoanamt = application.PrincipalAmount,
        //            IdNo = member.Idno,
        //            Interest = interestRate,
        //            RepayPeriod = application.RepayPeriod,    
        //            ApplicDate = application.ApplicationDate, 
        //            Status = (int)Status.Draft,
        //            Purpose = application.Purpose,
        //            AddSecurity = application.Remarks,
        //            Guaranteed = ParseRequiredGuarantors(loanType.Guarantor).ToString(),
        //            RepayMethod = loanType.Repaymethod ?? "AMT",
        //            BasicSalary = 0,
        //            Repayrate = 0,
        //            Sharecapital = 0,
        //            Run = 0,
        //            Run2 = 0,
        //            AuditTime = DateTime.Now,
        //            Posted = "Draft",
        //            UserName = application.CreatedBy,
        //            AuditDateTime = DateTime.Now,
        //            BlockchainTxId = null
        //        };

        //        _context.Loans.Add(loan);
        //        await _context.SaveChangesAsync();

        //        if (application.Guarantors != null && application.Guarantors.Any())
        //        {
        //            foreach (var guarantor in application.Guarantors)
        //            {
        //                var loanGuarantor = new Loanguar
        //                {
        //                    LoanNo = loanNo,
        //                    MemberNo = guarantor.GuarantorMemberNo,
        //                    Amount = guarantor.GuaranteeAmount,
        //                    Balance = guarantor.GuaranteeAmount,
        //                    CompanyCode = application.CompanyCode,
        //                    AuditTime = DateTime.Now,
        //                    Transfered = false
        //                };
        //                _context.Loanguar.Add(loanGuarantor);
        //            }
        //            await _context.SaveChangesAsync();
        //        }

        //        var blockchainData = new
        //        {
        //            LoanNo = loanNo,
        //            MemberNo = application.MemberNo,
        //            MemberIdNo = member.Idno,
        //            LoanCode = application.LoanCode,
        //            PrincipalAmount = application.PrincipalAmount,
        //            InterestRate = interestRate,
        //            RepayPeriod = application.RepayPeriod,        
        //            MaxRepayPeriodAllowed = maxRepayPeriod,
        //            RepayMethod = loanType.Repaymethod ?? "AMT",
        //            ApplicationDate = application.ApplicationDate, 
        //            CreatedBy = application.CreatedBy,
        //            Status = "Draft"
        //        };

        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "LOAN_APPLICATION",
        //            MemberNo = application.MemberNo,
        //            CompanyCode = application.CompanyCode,
        //            Amount = application.PrincipalAmount,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = loanNo,
        //            Status = "PENDING",
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        loan.BlockchainTxId = blockchainTx.TransactionId;
        //        await _context.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        return loan;
        //    }
        //    catch (Exception)
        //    {
        //        await transaction.RollbackAsync();
        //        throw;
        //    }
        //}

        public async Task<(bool IsEligible, string Message, bool HasValidShares, decimal TotalEligibleShares, decimal MaxLoanAmount)>
            CheckMemberEligibilityWithContributionsAsync(string memberNo, string companyCode)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                return (false, "Member not found", false, 0, 0);
            }

            _logger.LogInformation($"=== Checking loan eligibility for member: {memberNo} ===");

            var saccoParams = await _context.SaccoParram
                .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

            if (saccoParams != null && saccoParams.MembershipMaturityMonths > 0)
            {
                var effectDate = member.EffectDate ?? member.ApplicDate ?? DateTime.Now;
                var membershipMonths = ((DateTime.Now - effectDate).Days) / 30;

                if (membershipMonths < saccoParams.MembershipMaturityMonths)
                {
                    return (false, $"Member must be active for {saccoParams.MembershipMaturityMonths} months before applying for a loan. Current membership: {membershipMonths} months.", false, 0, 0);
                }
            }

            decimal totalEligibleAmount = 0;
            var eligibleBreakdown = new List<string>();

            // 2.1 PRIMARY SOURCE: GET DEPOSITSAMOUNT FROM CONTRIBSHARE TABLE
            // This is the member's actual savings/deposits - ONLY source for loan eligibility
            var depositsTotal = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo &&
                             cs.CompanyCode == companyCode)
                .SumAsync(cs => cs.DepositsAmount ?? 0);

            if (depositsTotal > 0)
            {
                totalEligibleAmount = depositsTotal; // Use ONLY deposits, nothing else
                eligibleBreakdown.Add($"Savings/Deposits: {depositsTotal:C}");
                _logger.LogInformation($"Found DepositsAmount: {depositsTotal:C} - This is the ONLY eligible amount for loans");
            }
            else
            {
                _logger.LogInformation($"No DepositsAmount found for member {memberNo}");
            }

            // 2.2 LEGACY FALLBACK - ONLY if NO DepositsAmount found
            // This ensures backward compatibility for members without deposits
            if (totalEligibleAmount == 0)
            {
                _logger.LogInformation("No DepositsAmount found, falling back to Shares table for legacy support...");

                // Get all valid share types based on boolean flags
                var validShareTypes = await _context.Sharetypes
                    .Where(s => s.CompanyCode == companyCode &&
                               (s.UsedToGuarantee == true || s.UsedToOffset == true) &&
                               s.Withdrawable == true)
                    .ToListAsync();

                if (validShareTypes.Any())
                {
                    foreach (var shareType in validShareTypes)
                    {
                        var memberShares = await _context.Shares
                            .Where(s => s.MemberNo == memberNo &&
                                       s.Sharescode == shareType.SharesCode &&
                                       s.CompanyCode == companyCode)
                            .SumAsync(s => s.TotalShares ?? 0);

                        if (memberShares > 0)
                        {
                            totalEligibleAmount += memberShares;
                            eligibleBreakdown.Add($"{shareType.SharesType ?? shareType.SharesCode}: {memberShares:C}");
                            _logger.LogWarning($"Using legacy Shares table for eligibility: {memberShares:C}");
                        }
                    }
                }
                else
                {
                    // If no valid share types, get all shares as last resort
                    var allShares = await _context.Shares
                        .Where(s => s.MemberNo == memberNo && s.CompanyCode == companyCode)
                        .SumAsync(s => s.TotalShares ?? 0);

                    if (allShares > 0)
                    {
                        totalEligibleAmount += allShares;
                        eligibleBreakdown.Add($"All Shares (legacy fallback): {allShares:C}");
                        _logger.LogWarning($"Using all shares as legacy fallback: {allShares:C}");
                    }
                }
            }

            // 2.3 Check for locked shares (used as guarantor)
            var lockedShares = await GetSharesLockedForGuaranteeAsync(memberNo, null, companyCode);
            var availableAmount = totalEligibleAmount - lockedShares;

            _logger.LogInformation($"Total Eligible (Deposits only): {totalEligibleAmount:C}, Locked: {lockedShares:C}, Available: {availableAmount:C}");


            if (availableAmount <= 0)
            {
                if (totalEligibleAmount > 0)
                {
                    return (false, $"All eligible deposits ({totalEligibleAmount:C}) are locked as guarantees for other loans. Available: {availableAmount:C}", true, totalEligibleAmount, 0);
                }
                return (false, $"Member has no savings/deposits. Please make a deposit/savings contribution first before applying for a loan.", false, 0, 0);
            }

            // Get minimum requirement from SaccoParram or default to 1000
            var minRequirement = saccoParams?.SignificantLoanBalance ?? 1000m;

            if (availableAmount < minRequirement)
            {
                return (false, $"Member's eligible deposits ({availableAmount:C}) is below minimum requirement of {minRequirement:C}. Please increase savings/deposits.", true, availableAmount, 0);
            }

            // Get loan-to-deposit ratio from SaccoParram or default to 3
            decimal loanToDepositRatio = 3m;

            // Try to get ratio from valid share types first
            var validShareTypesForRatio = await _context.Sharetypes
                .Where(s => s.CompanyCode == companyCode &&
                           s.LoanToShareRatio.HasValue &&
                           s.LoanToShareRatio.Value > 0)
                .ToListAsync();

            if (validShareTypesForRatio.Any())
            {
                loanToDepositRatio = validShareTypesForRatio
                    .Select(s => (decimal)s.LoanToShareRatio.Value)
                    .Max();
            }

            var maxLoanAmount = availableAmount * loanToDepositRatio;

            // Apply any maximum loan limit from SaccoParram
            var maxLoanLimit = saccoParams?.SignificantLoanBalance.HasValue == true
                ? saccoParams.SignificantLoanBalance.Value * loanToDepositRatio
                : maxLoanAmount;

            var finalMaxLoan = Math.Min(maxLoanAmount, maxLoanLimit);

            var existingLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo &&
                            l.CompanyCode == companyCode &&
                            l.Status != (int)Status.Closed &&
                            l.Status != (int)Status.Disbursed)
                .ToListAsync();

            var outstandingLoanBalance = existingLoans.Sum(l => l.LoanAmt ?? 0);

            if (outstandingLoanBalance > 0)
            {
                _logger.LogInformation($"Member has existing loans: {outstandingLoanBalance:C}");
                // Reduce max loan by outstanding balance
                finalMaxLoan = Math.Max(0, finalMaxLoan - outstandingLoanBalance);

                if (finalMaxLoan <= 0)
                {
                    return (false, $"Member has outstanding loan balance of {outstandingLoanBalance:C} which exceeds or equals eligible loan amount.", true, availableAmount, 0);
                }
            }

            var breakdownMessage = string.Join(", ", eligibleBreakdown);

            var successMessage = $"Member is eligible for a loan up to {finalMaxLoan:C}. " +
                                $"Eligible deposits breakdown: {breakdownMessage}. " +
                                $"Available deposits: {availableAmount:C}. " +
                                $"Loan-to-deposit ratio: {loanToDepositRatio}:1";

            _logger.LogInformation($"✓ Member {memberNo} is eligible. Max Loan: {finalMaxLoan:C}");

            return (true,
                    successMessage,
                    true,
                    availableAmount,
                    finalMaxLoan);
        }
        public async Task<bool> HasActiveLoansAsync(string memberNo, string companyCode)
        {
            return await _context.Loans
                .AnyAsync(l => l.MemberNo == memberNo &&
                               l.CompanyCode == companyCode &&
                               l.Status != (int)Status.Closed &&
                               l.Status != (int)Status.Rejected &&
                               l.Status != (int)Status.WrittenOff);
        }

        public async Task<decimal> GetGuarantorTotalGuaranteesAsync(string memberNo, string companyCode)
        {
            return await _context.Loanguar
                .Where(g => g.MemberNo == memberNo &&
                            g.CompanyCode == companyCode &&
                            g.Transfered == false &&
                            (g.Balance > 0))
                .SumAsync(g => g.Amount ?? 0);
        }

        public async Task<(bool HasExistingLoan, string Message, List<LoanSummaryDTO> ExistingLoans)> CheckExistingLoansAsync(string memberNo, string companyCode)
        {
            var existingLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo &&
                            l.CompanyCode == companyCode &&
                            l.Status != (int)Status.Closed &&
                            l.Status != (int)Status.Rejected &&
                            l.Status != (int)Status.WrittenOff)
                .OrderByDescending(l => l.ApplicDate)
                .Select(l => new LoanSummaryDTO
                {
                    LoanNo = l.LoanNo,
                    MemberNo = l.MemberNo,
                    MemberName = "",
                    LoanType = "",
                    PrincipalAmount = l.LoanAmt ?? 0,
                    ApprovedAmount = 0,
                    DisbursedAmount = 0,
                    OutstandingBalance = 0,
                    ArrearsAmount = 0,
                    LoanStatus = l.Status.ToString(),
                    ApplicationDate = l.ApplicDate,
                    DisbursementDate = null,
                    MaturityDate = null,
                    DaysOverdue = 0,
                    InterestRate = l.Interest ?? 0,
                    MonthlyInstallment = 0,
                    InstallmentsPaid = 0,
                    TotalInstallments = 0
                })
                .ToListAsync();

            if (existingLoans.Any())
            {
                var statusList = string.Join(", ", existingLoans.Select(l => $"{l.LoanNo} ({l.LoanStatus})"));
                var message = $"Member has existing loan(s) that are still active: {statusList}. " +
                             "Please clear existing loans before applying for a new loan.";

                return (true, message, existingLoans);
            }

            return (false, "No existing active loans", new List<LoanSummaryDTO>());
        }

        private int ParseRequiredGuarantors(string guarantorValue)
        {
            if (string.IsNullOrEmpty(guarantorValue))
                return 0;

            if (new[] { "Yes", "Y", "1" }.Contains(guarantorValue, StringComparer.OrdinalIgnoreCase))
                return 1;

            if (new[] { "No", "N", "0" }.Contains(guarantorValue, StringComparer.OrdinalIgnoreCase))
                return 0;

            if (int.TryParse(guarantorValue, out int count))
                return count;

            return 0;
        }

        public async Task<DateTime?> GetLastRepaymentDateAsync(string loanNo)
        {
            try
            {
                var lastRepayment = await _context.Repay
                    .Where(r => r.LoanNo == loanNo)
                    .OrderByDescending(r => r.AuditTime)
                    .FirstOrDefaultAsync();

                return lastRepayment?.AuditTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting last repayment date for loan {loanNo}");
                return null;
            }
        }

        public async Task<Loan> GetLoanByNoAsync(string loanNo, string companyCode)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

            if (loan == null)
            {
                throw new InvalidOperationException($"Loan {loanNo} not found");
            }

            // DON'T MODIFY THE INTEREST RATE - just return as stored
            // Remove these lines:
            // var loanBalance = await _context.Loanbal
            //     .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);
            // if (loanBalance != null)
            // {
            //     loan.LoanAmt = loanBalance.Balance;
            //     loan.Interest = loanBalance.IntrOwed;  // THIS IS WRONG - IntrOwed is interest owed, not interest rate
            // }

            return loan;
        }

        public async Task<List<LoanSummaryDTO>> GetMemberLoansAsync(string memberNo, string companyCode)
        {
            try
            {
                var loans = await _context.Loans
                    .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode)
                    .OrderByDescending(l => l.ApplicDate)
                    .Select(l => new LoanSummaryDTO
                    {
                        LoanNo = l.LoanNo,
                        MemberNo = l.MemberNo,
                        MemberName = "",
                        LoanType = "",
                        PrincipalAmount = l.LoanAmt ?? 0,
                        ApprovedAmount = 0,
                        DisbursedAmount = l.LoanAmt ?? 0,
                        OutstandingBalance = 0,
                        ArrearsAmount = 0,
                        LoanStatus = l.Status.ToString(),
                        ApplicationDate = l.ApplicDate,
                        DisbursementDate = null,
                        MaturityDate = null,
                        DaysOverdue = 0,
                        InterestRate = l.Interest ?? 0,
                        MonthlyInstallment = 0,
                        InstallmentsPaid = 0,
                        TotalInstallments = 0
                    })
                    .ToListAsync();

                return loans;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting member loans for {memberNo}");
                return new List<LoanSummaryDTO>();
            }
        }

        public async Task<List<LoanSummaryDTO>> SearchLoansAsync(LoanSearchDTO searchDto)
        {
            var query = _context.Loans
                .Where(l => l.CompanyCode == searchDto.CompanyCode);

            if (!string.IsNullOrEmpty(searchDto.MemberNo))
            {
                query = query.Where(l => l.MemberNo == searchDto.MemberNo);
            }

            if (!string.IsNullOrEmpty(searchDto.LoanNo))
            {
                query = query.Where(l => l.LoanNo.Contains(searchDto.LoanNo));
            }

            if (!string.IsNullOrEmpty(searchDto.LoanStatus) && int.TryParse(searchDto.LoanStatus, out int status))
            {
                query = query.Where(l => l.Status == status);
            }
            else if (!string.IsNullOrEmpty(searchDto.LoanStatus))
            {
                var statusEnum = Enum.Parse<Status>(searchDto.LoanStatus);
                query = query.Where(l => l.Status == (int)statusEnum);
            }

            if (!string.IsNullOrEmpty(searchDto.LoanCode))
            {
                query = query.Where(l => l.LoanCode == searchDto.LoanCode);
            }

            if (searchDto.FromDate.HasValue)
            {
                query = query.Where(l => l.ApplicDate >= searchDto.FromDate.Value);
            }

            if (searchDto.ToDate.HasValue)
            {
                query = query.Where(l => l.ApplicDate <= searchDto.ToDate.Value);
            }

            if (searchDto.MinAmount.HasValue)
            {
                query = query.Where(l => l.LoanAmt >= searchDto.MinAmount.Value);
            }

            if (searchDto.MaxAmount.HasValue)
            {
                query = query.Where(l => l.LoanAmt <= searchDto.MaxAmount.Value);
            }

            var loans = await query
                .OrderByDescending(l => l.ApplicDate)
                .ToListAsync();

            var result = new List<LoanSummaryDTO>();

            foreach (var loan in loans)
            {
                // Get member name
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == searchDto.CompanyCode);

                var memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;
                if (string.IsNullOrEmpty(memberName)) memberName = loan.MemberNo;

                // Get loan type name
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == searchDto.CompanyCode);

                var loanTypeName = loanType?.LoanType1 ?? loan.LoanCode ?? "Unknown";

                // Get approved amount from Endmain (endorsement)
                var endmain = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == loan.LoanNo && e.CompanyCode == searchDto.CompanyCode);

                var approvedAmount = endmain?.AmtApproved ?? 0;

                // Get outstanding balance from Loanbal table
                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loan.LoanNo && lb.Companycode == searchDto.CompanyCode);

                var outstandingBalance = loanbal?.Balance ?? 0;

                // Get total guarantee for this loan
                var totalGuarantee = await GetTotalGuaranteeForLoanAsync(loan.LoanNo, searchDto.CompanyCode);

                // Calculate disbursed amount (if loan is disbursed or endorsed)
                var disbursedAmount = (loan.Status == (int)Status.Disbursed || loan.Status == (int)Status.Endorsed)
                    ? approvedAmount
                    : 0;

                // Get status name
                var statusName = ((Status)(loan.Status ?? 0)).ToString();

                result.Add(new LoanSummaryDTO
                {
                    LoanNo = loan.LoanNo,
                    MemberNo = loan.MemberNo,
                    MemberName = memberName,
                    LoanType = loanTypeName,
                    PrincipalAmount = loan.LoanAmt ?? 0,        
                    MaxLoanamt = loan.MaxLoanamt ?? 0,          
                    ApprovedAmount = approvedAmount,            
                    DisbursedAmount = disbursedAmount,          
                    OutstandingBalance = outstandingBalance,    
                    TotalGuarantee = totalGuarantee,            
                    ArrearsAmount = 0,
                    LoanStatus = statusName,
                    ApplicationDate = loan.ApplicDate,
                    DisbursementDate = loan.AuditDateTime,
                    MaturityDate = loanbal?.LastDate,
                    DaysOverdue = 0,
                    InterestRate = loan.Interest ?? 0,
                    MonthlyInstallment = loanbal?.RepayRate ?? 0,
                    InstallmentsPaid = await _context.Repay.CountAsync(r => r.LoanNo == loan.LoanNo && r.Posted == true),
                    TotalInstallments = loan.RepayPeriod ?? 0,
                    RequiredGuarantors = 0
                });
            }

            return result;
        }

        public async Task<LoanDashboardDTO> GetLoanDashboardAsync(string companyCode)
        {
            var loans = await _context.Loans
                .Where(l => l.CompanyCode == companyCode)
                .ToListAsync();

            var dashboard = new LoanDashboardDTO
            {
                TotalLoans = loans.Count,
                TotalLoanAmount = loans.Sum(l => l.LoanAmt ?? 0),
                TotalDisbursed = loans.Where(l => l.Status == (int)Status.Disbursed || l.Status == (int)Status.Endorsed)
                    .Sum(l => l.LoanAmt ?? 0),
                TotalOutstanding = 0,
                TotalRepaid = 0,
                TotalArrears = 0,
                PendingApplications = loans.Count(l => l.Status == (int)Status.Draft),
                UnderAppraisal = loans.Count(l => l.Status == (int)Status.UnderAppraisal),
                PendingApproval = loans.Count(l => l.Status == (int)Status.Submitted),
                PendingFinalApproval = loans.Count(l => l.Status == (int)Status.Approved),
                ApprovedPendingDisbursement = loans.Count(l => l.Status == (int)Status.Approved),
                ActiveLoans = loans.Count(l => l.Status == (int)Status.Disbursed),
                OverdueLoans = 0,
                DefaultedLoans = loans.Count(l => l.Status == (int)Status.Defaulted)
            };

            dashboard.LoansByStatus = loans
                .GroupBy(l => l.Status ?? 0)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            dashboard.LoanPortfolioByType = new Dictionary<string, decimal>();

            dashboard.RecentLoans = await SearchLoansAsync(new LoanSearchDTO
            {
                CompanyCode = companyCode
            });

            dashboard.RecentLoans = dashboard.RecentLoans.Take(10).ToList();

            return dashboard;
        }

        #endregion


        #region Guarantor Management

        public async Task<Loan> GetLoanByNoForDisplayAsync(string loanNo, string companyCode)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

            if (loan == null)
            {
                throw new InvalidOperationException($"Loan {loanNo} not found");
            }

            return loan;
        }


        public async Task<Loanguar> AssignGuarantorAsync(string loanNo, GuarantorAssignmentDTO guarantor, string assignedBy)
        {
            var loan = await GetLoanByNoForDisplayAsync(loanNo, guarantor.CompanyCode);

            _logger.LogInformation($"Assigning guarantor to loan {loanNo}. Current status: {loan.Status}");

            if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
            {
                throw new InvalidOperationException($"Cannot assign guarantors to loan in status '{loan.Status}'. Loan must be in Draft or Submitted status.");
            }

            // Get loan type to check if self guarantee is allowed
            var loanType = await _context.Loantypes
                .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == loan.CompanyCode);

            bool isSelfGuarantee = loanType?.SelfGuarantee ?? false;

            int maxGuarantors = 5;

            try
            {
                var saccoParams = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == guarantor.CompanyCode);

                if (saccoParams != null)
                {
                    maxGuarantors = saccoParams.MaxGuarantor;
                    _logger.LogInformation($"Max guarantors from SACCO parameters: {maxGuarantors}");
                }
                else
                {
                    _logger.LogWarning($"No SACCO parameters found for company {guarantor.CompanyCode}. Using default: {maxGuarantors}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting SACCO parameters for company {guarantor.CompanyCode}. Using default: {maxGuarantors}");
            }

            var currentGuarantors = await _context.Loanguar
                .CountAsync(g => g.LoanNo == loanNo && g.Transfered == false);

            if (currentGuarantors >= maxGuarantors)
            {
                throw new InvalidOperationException($"Maximum number of guarantors ({maxGuarantors}) already assigned");
            }

            // Check self guarantee logic
            bool isSelfGuarantor = guarantor.GuarantorMemberNo == loan.MemberNo;

            if (isSelfGuarantor && !isSelfGuarantee)
            {
                _logger.LogWarning($"Self guarantee blocked: isSelfGuarantee={isSelfGuarantee}, Guarantor={guarantor.GuarantorMemberNo}, Applicant={loan.MemberNo}");
                throw new InvalidOperationException("Self guarantee is not allowed for this loan type. Please add another member as guarantor.");
            }

            if (isSelfGuarantor && isSelfGuarantee)
            {
                _logger.LogInformation($"Self guarantee is allowed for this loan type. Applicant {loan.MemberNo} can guarantee their own loan.");
            }

            // Get guarantor member details for FullNames
            var guarantorMember = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == guarantor.GuarantorMemberNo && m.CompanyCode == loan.CompanyCode);

            string guarantorFullName = "";
            if (guarantorMember != null)
            {
                guarantorFullName = $"{guarantorMember.Surname ?? ""} {guarantorMember.OtherNames ?? ""}".Trim();
                if (string.IsNullOrEmpty(guarantorFullName))
                {
                    guarantorFullName = guarantor.GuarantorMemberNo;
                }
            }

            // Validate eligibility (skip for self guarantee)
            if (!isSelfGuarantor)
            {
                var eligibility = await ValidateGuarantorEligibilityAsync(
                    guarantor.GuarantorMemberNo,
                    guarantor.GuaranteeAmount,
                    loan.CompanyCode);

                if (!eligibility)
                {
                    throw new InvalidOperationException("Guarantor is not eligible. Ensure member is active and has sufficient shares.");
                }
            }

            var existing = await _context.Loanguar
                .FirstOrDefaultAsync(g => g.LoanNo == loanNo && g.MemberNo == guarantor.GuarantorMemberNo && g.Transfered == false);

            if (existing != null)
            {
                throw new InvalidOperationException($"Guarantor {guarantor.GuarantorMemberNo} is already assigned to this loan");
            }

            // Store the old loan status for audit (before any changes)
            int oldLoanStatus = (int)loan.Status;
            string oldLoanPosted = loan.Posted ?? "";

            // Create and save the guarantor with ALL fields
            var loanGuarantor = new Loanguar
            {
                LoanNo = loanNo,
                MemberNo = guarantor.GuarantorMemberNo,
                CompanyCode = loan.CompanyCode,
                Amount = guarantor.GuaranteeAmount,
                Balance = guarantor.GuaranteeAmount,
                AuditTime = DateTime.Now,
                AuditId = assignedBy,
                Transfered = false,
                FullNames = guarantorFullName,
                Description = guarantor.Remarks,
                Tguaranto = guarantor.GuaranteeAmount,
                Transdate = DateTime.Now,
                Collateral = isSelfGuarantor ? "Self Guarantee" : "Member Guarantee"
            };

            _context.Loanguar.Add(loanGuarantor);
            await _context.SaveChangesAsync();

            var updatedGuarantorCount = await _context.Loanguar
                .CountAsync(g => g.LoanNo == loanNo && g.Transfered == false);

            bool loanStatusChanged = false;

            // UPDATE LOAN STATUS to Submitted when guarantors are added
            if (loan.Status == (int)Status.Draft && updatedGuarantorCount > 0)
            {
                loan.Status = (int)Status.Submitted;
                loan.Posted = "SUBMIT";
                loan.UserName = assignedBy;
                loan.AuditDateTime = DateTime.Now;
                loanStatusChanged = true;

                _context.Loans.Update(loan);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Loan {loanNo} status updated from Draft to Submitted. Total guarantors: {updatedGuarantorCount}");
            }

            // Create blockchain data (use shorter property names to avoid truncation)
            var blockchainData = new
            {
                Id = loanGuarantor.Id,
                LoanNo = loanNo,
                G = guarantor.GuarantorMemberNo,
                GName = guarantorFullName,
                Amt = guarantor.GuaranteeAmount,
                By = assignedBy,
                Date = DateTime.Now,
                Remarks = guarantor.Remarks ?? "",
                LoanAmt = loan.LoanAmt,
                TotalG = updatedGuarantorCount,
                Status = loan.Status,
                IsSelf = isSelfGuarantor
            };

            var blockchainTx = new BlockchainTransaction
            {
                TransactionId = Guid.NewGuid().ToString(),
                TransactionType = "LOAN_GUARANTOR_ASSIGNED",
                MemberNo = loan.MemberNo,
                CompanyCode = loan.CompanyCode,
                Amount = guarantor.GuaranteeAmount,
                Timestamp = DateTime.Now,
                DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                OffChainReferenceId = $"{loanNo}-{guarantor.GuarantorMemberNo}",
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.BlockchainTransactions.Add(blockchainTx);
            await _context.SaveChangesAsync();

            loanGuarantor.BlockchainTxId = blockchainTx.TransactionId;
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Blockchain transaction recorded for guarantor assignment: {blockchainTx.TransactionId}");

            // ============================================================
            // SAVE AUDIT TRAIL FOR GUARANTOR ASSIGNMENT
            // ============================================================

            // Create audit extra data
            var auditExtraData = new
            {
                loanNo = loanNo,
                applicantMemberNo = loan.MemberNo,
                guarantorMemberNo = guarantor.GuarantorMemberNo,
                guarantorName = guarantorFullName,
                guaranteeAmount = guarantor.GuaranteeAmount,
                isSelfGuarantee = isSelfGuarantor,
                remarks = guarantor.Remarks ?? "",
                assignedBy = assignedBy,
                assignedDate = DateTime.Now,
                totalGuarantorsAfter = updatedGuarantorCount,
                maxGuarantorsAllowed = maxGuarantors,
                loanStatusBefore = oldLoanStatus,
                loanStatusAfter = loan.Status,
                loanStatusChanged = loanStatusChanged,
                currentLoanAmount = loan.LoanAmt,
                blockchainTxId = blockchainTx.TransactionId
            };

            // Create a copy of the guarantor object for NewValue
            var guarantorForAudit = new
            {
                loanGuarantor.Id,
                loanGuarantor.LoanNo,
                loanGuarantor.MemberNo,
                loanGuarantor.Amount,
                loanGuarantor.Balance,
                loanGuarantor.FullNames,
                loanGuarantor.Description,
                loanGuarantor.Collateral,
                loanGuarantor.Transfered,
                AssignedBy = assignedBy,
                AssignedDate = DateTime.Now,
                BlockchainTxId = blockchainTx.TransactionId
            };

            await _auditService.SaveLogAsync(
                actionType: AuditActionType.Insert,
                oldModel: null,  // For Insert, OldValue is null (no previous guarantor record)
                newModel: guarantorForAudit,  // This will be serialized to NewValue column
                tableName: "Loanguar",
                recordId: loanGuarantor.Id.ToString(),
                userId: assignedBy,
                userName: assignedBy,
                companyCode: loan.CompanyCode,
                module: "LoanManagement",
                extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                blockchainTxId: blockchainTx.TransactionId
            );

            // If loan status changed, also audit the loan status change
            if (loanStatusChanged)
            {
                var loanAuditExtraData = new
                {
                    loanNo = loanNo,
                    statusChangedFrom = oldLoanStatus,
                    statusChangedTo = loan.Status,
                    reason = $"First guarantor assigned. Total guarantors: {updatedGuarantorCount}",
                    triggeredBy = assignedBy,
                    triggeredDate = DateTime.Now,
                    guarantorId = loanGuarantor.Id,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.Status,
                    loan.Posted,
                    loan.UserName,
                    loan.AuditDateTime,
                    UpdatedBy = assignedBy,
                    UpdateReason = "Guarantor assigned - loan status changed to Submitted"
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new { Status = oldLoanStatus, Posted = oldLoanPosted },
                    newModel: loanForAudit,
                    tableName: "Loans",
                    recordId: loanNo,
                    userId: assignedBy,
                    userName: assignedBy,
                    companyCode: loan.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                _logger.LogInformation($"Loan status change audited for {loanNo}");
            }

            return loanGuarantor;
        }

        //public async Task<Loanguar> AssignGuarantorAsync(string loanNo, GuarantorAssignmentDTO guarantor, string assignedBy)
        //{
        //    var loan = await GetLoanByNoForDisplayAsync(loanNo, guarantor.CompanyCode);

        //    _logger.LogInformation($"Assigning guarantor to loan {loanNo}. Current status: {loan.Status}");

        //    if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
        //    {
        //        throw new InvalidOperationException($"Cannot assign guarantors to loan in status '{loan.Status}'. Loan must be in Draft or Submitted status.");
        //    }

        //    // Get loan type to check if self guarantee is allowed
        //    var loanType = await _context.Loantypes
        //        .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == loan.CompanyCode);

        //    bool isSelfGuarantee = loanType?.SelfGuarantee ?? false;

        //    int maxGuarantors = 5;

        //    try
        //    {
        //        var saccoParams = await _context.SaccoParram
        //            .FirstOrDefaultAsync(s => s.CompanyCode == guarantor.CompanyCode);

        //        if (saccoParams != null)
        //        {
        //            maxGuarantors = saccoParams.MaxGuarantor;
        //            _logger.LogInformation($"Max guarantors from SACCO parameters: {maxGuarantors}");
        //        }
        //        else
        //        {
        //            _logger.LogWarning($"No SACCO parameters found for company {guarantor.CompanyCode}. Using default: {maxGuarantors}");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Error getting SACCO parameters for company {guarantor.CompanyCode}. Using default: {maxGuarantors}");
        //    }

        //    var currentGuarantors = await _context.Loanguar
        //        .CountAsync(g => g.LoanNo == loanNo && g.Transfered == false);

        //    if (currentGuarantors >= maxGuarantors)
        //    {
        //        throw new InvalidOperationException($"Maximum number of guarantors ({maxGuarantors}) already assigned");
        //    }

        //    // Check self guarantee logic
        //    bool isSelfGuarantor = guarantor.GuarantorMemberNo == loan.MemberNo;

        //    if (isSelfGuarantor && !isSelfGuarantee)
        //    {
        //        _logger.LogWarning($"Self guarantee blocked: isSelfGuarantee={isSelfGuarantee}, Guarantor={guarantor.GuarantorMemberNo}, Applicant={loan.MemberNo}");
        //        throw new InvalidOperationException("Self guarantee is not allowed for this loan type. Please add another member as guarantor.");
        //    }

        //    if (isSelfGuarantor && isSelfGuarantee)
        //    {
        //        _logger.LogInformation($"Self guarantee is allowed for this loan type. Applicant {loan.MemberNo} can guarantee their own loan.");
        //    }

        //    // Get guarantor member details for FullNames
        //    var guarantorMember = await _context.Members
        //        .FirstOrDefaultAsync(m => m.MemberNo == guarantor.GuarantorMemberNo && m.CompanyCode == loan.CompanyCode);

        //    string guarantorFullName = "";
        //    if (guarantorMember != null)
        //    {
        //        guarantorFullName = $"{guarantorMember.Surname ?? ""} {guarantorMember.OtherNames ?? ""}".Trim();
        //        if (string.IsNullOrEmpty(guarantorFullName))
        //        {
        //            guarantorFullName = guarantor.GuarantorMemberNo;
        //        }
        //    }

        //    // Validate eligibility (skip for self guarantee)
        //    if (!isSelfGuarantor)
        //    {
        //        var eligibility = await ValidateGuarantorEligibilityAsync(
        //            guarantor.GuarantorMemberNo,
        //            guarantor.GuaranteeAmount,
        //            loan.CompanyCode);

        //        if (!eligibility)
        //        {
        //            throw new InvalidOperationException("Guarantor is not eligible. Ensure member is active and has sufficient shares.");
        //        }
        //    }

        //    var existing = await _context.Loanguar
        //        .FirstOrDefaultAsync(g => g.LoanNo == loanNo && g.MemberNo == guarantor.GuarantorMemberNo && g.Transfered == false);

        //    if (existing != null)
        //    {
        //        throw new InvalidOperationException($"Guarantor {guarantor.GuarantorMemberNo} is already assigned to this loan");
        //    }

        //    // Create and save the guarantor with ALL fields
        //    var loanGuarantor = new Loanguar
        //    {
        //        LoanNo = loanNo,
        //        MemberNo = guarantor.GuarantorMemberNo,
        //        CompanyCode = loan.CompanyCode,
        //        Amount = guarantor.GuaranteeAmount,
        //        Balance = guarantor.GuaranteeAmount,
        //        AuditTime = DateTime.Now,
        //        AuditId = assignedBy,
        //        Transfered = false,
        //        FullNames = guarantorFullName,
        //        Description = guarantor.Remarks,
        //        Tguaranto = guarantor.GuaranteeAmount,
        //        Transdate = DateTime.Now,
        //        Collateral = isSelfGuarantor ? "Self Guarantee" : "Member Guarantee"
        //    };

        //    _context.Loanguar.Add(loanGuarantor);
        //    await _context.SaveChangesAsync();

        //    var updatedGuarantorCount = await _context.Loanguar
        //        .CountAsync(g => g.LoanNo == loanNo && g.Transfered == false);

        //    // UPDATE LOAN STATUS to Submitted when guarantors are added
        //    if (loan.Status == (int)Status.Draft && updatedGuarantorCount > 0)
        //    {
        //        loan.Status = (int)Status.Submitted;
        //        loan.Posted = "SUBMIT";
        //        loan.UserName = assignedBy;
        //        loan.AuditDateTime = DateTime.Now;

        //        _context.Loans.Update(loan);
        //        await _context.SaveChangesAsync();

        //        _logger.LogInformation($"Loan {loanNo} status updated from Draft to Submitted. Total guarantors: {updatedGuarantorCount}");
        //    }

        //    // Create blockchain data (use shorter property names to avoid truncation)
        //    var blockchainData = new
        //    {
        //        Id = loanGuarantor.Id,
        //        LoanNo = loanNo,
        //        G = guarantor.GuarantorMemberNo,
        //        Amt = guarantor.GuaranteeAmount,
        //        By = assignedBy,
        //        Date = DateTime.Now,
        //        Remarks = guarantor.Remarks ?? "",
        //        LoanAmt = loan.LoanAmt,
        //        TotalG = updatedGuarantorCount,
        //        Status = loan.Status
        //    };

        //    var blockchainTx = new BlockchainTransaction
        //    {
        //        TransactionId = Guid.NewGuid().ToString(),
        //        TransactionType = "LOAN_GUARANTOR_ASSIGNED",
        //        MemberNo = loan.MemberNo,
        //        CompanyCode = loan.CompanyCode,
        //        Amount = guarantor.GuaranteeAmount,
        //        Timestamp = DateTime.Now,
        //        DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //        PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //        OffChainReferenceId = $"{loanNo}-{guarantor.GuarantorMemberNo}",
        //        Status = "PENDING",
        //        CreatedAt = DateTime.Now
        //    };

        //    _context.BlockchainTransactions.Add(blockchainTx);
        //    await _context.SaveChangesAsync();

        //    loanGuarantor.BlockchainTxId = blockchainTx.TransactionId;
        //    await _context.SaveChangesAsync();

        //    _logger.LogInformation($"Blockchain transaction recorded for guarantor assignment: {blockchainTx.TransactionId}");

        //    return loanGuarantor;
        //}

        public async Task<List<GuarantorResponseDTO>> GetLoanGuarantorsAsync(string loanNo)
        {
            var guarantors = await _context.Loanguar
                .Where(g => g.LoanNo == loanNo)
                .Select(g => new GuarantorResponseDTO
                {
                    Id = g.Id,
                    LoanNo = g.LoanNo,
                    GuarantorMemberNo = g.MemberNo,
                    GuarantorName = g.FullNames ?? "",
                    IdNo = _context.Members.Where(m => m.MemberNo == g.MemberNo).Select(m => m.Idno).FirstOrDefault() ?? "",
                    PhoneNo = _context.Members.Where(m => m.MemberNo == g.MemberNo).Select(m => m.PhoneNo).FirstOrDefault() ?? "",
                    GuaranteeAmount = g.Amount ?? 0,
                    AvailableShares = 0,
                    // ✅ FIX: Use "Pending" instead of "Active" for newly created guarantors
                    Status = g.Transfered == false ? "Pending" : "Released",
                    AssignedDate = g.AuditTime ?? DateTime.Now,
                    ApprovedDate = null,
                    ApprovedBy = null,
                    Remarks = g.Description
                })
                .ToListAsync();

            return guarantors;
        }

        public async Task<bool> ReleaseGuarantorAsync(int guarantorId, string releasedBy)
        {
            var guarantor = await _context.Loanguar
                .FirstOrDefaultAsync(g => g.Id == guarantorId);

            if (guarantor == null)
            {
                throw new InvalidOperationException("Guarantor not found");
            }

            if (guarantor.Transfered == true)
            {
                throw new InvalidOperationException($"Cannot release guarantor that is already transferred");
            }

            guarantor.Transfered = true;
            guarantor.Transdate = DateTime.Now;
            await _context.SaveChangesAsync();

            var blockchainData = new
            {
                Id = guarantor.Id,
                LoanNo = guarantor.LoanNo,
                GuarantorMemberNo = guarantor.MemberNo,
                GuaranteeAmount = guarantor.Amount,
                ReleasedBy = releasedBy,
                ReleasedDate = DateTime.Now
            };

            var blockchainTx = new BlockchainTransaction
            {
                TransactionId = Guid.NewGuid().ToString(),
                TransactionType = "LOAN_GUARANTOR_RELEASED",
                MemberNo = guarantor.MemberNo,
                CompanyCode = guarantor.CompanyCode,
                Amount = guarantor.Amount ?? 0,
                Timestamp = DateTime.Now,
                DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                OffChainReferenceId = $"{guarantor.LoanNo}-{guarantor.MemberNo}-released",
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.BlockchainTransactions.Add(blockchainTx);
            await _context.SaveChangesAsync();

            guarantor.BlockchainTxId = blockchainTx.TransactionId;
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Blockchain transaction recorded for guarantor release: {blockchainTx.TransactionId}");

            return true;
        }
        public async Task<bool> ValidateGuarantorEligibilityAsync(string memberNo, decimal guaranteeAmount, string companyCode)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                _logger.LogWarning($"Guarantor {memberNo} not found");
                return false;
            }

            // Check if member is active
            if (member.Withdrawn == true || member.Archived == true || member.Dormant == 1)
            {
                _logger.LogWarning($"Guarantor {memberNo} is not active (Withdrawn:{member.Withdrawn}, Archived:{member.Archived}, Dormant:{member.Dormant})");
                return false;
            }

            // 1. GET TOTAL DEPOSITSAMOUNT FROM CONTRIBSHARE TABLE
            var totalDeposits = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                .SumAsync(cs => cs.DepositsAmount ?? 0);

            if (totalDeposits <= 0)
            {
                _logger.LogWarning($"Guarantor {memberNo} has no deposits. Total DepositsAmount: {totalDeposits:C}");
                return false;
            }

            _logger.LogInformation($"Guarantor {memberNo}: Total DepositsAmount = {totalDeposits:C}");

            // 2. CHECK MINIMUM REQUIREMENT
            var saccoParams = await _context.SaccoParram
                .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

            var minDepositRequirement = saccoParams?.MinGuarantor ?? 0;

            if (minDepositRequirement > 0 && totalDeposits < minDepositRequirement)
            {
                _logger.LogWarning($"Guarantor {memberNo} deposits ({totalDeposits:C}) below minimum requirement ({minDepositRequirement:C})");
                return false;
            }

            // 3. CHECK EXISTING GUARANTEES (deposits already locked for other loans)
            var existingGuarantees = await _context.Loanguar
                .Where(g => g.MemberNo == memberNo &&
                           g.CompanyCode == companyCode &&
                           g.Transfered == false &&
                           (g.Balance > 0 || (g.Amount > 0 && g.Balance == null)))
                .SumAsync(g => g.Amount ?? 0);

            var availableDeposits = totalDeposits - existingGuarantees;

            if (availableDeposits <= 0)
            {
                _logger.LogWarning($"Guarantor {memberNo} has all deposits locked. Total: {totalDeposits:C}, Locked: {existingGuarantees:C}, Available: {availableDeposits:C}");
                return false;
            }

            // 4. CHECK IF GUARANTEE AMOUNT CAN BE COVERED (1:1 ratio - no multiplication)
            if (guaranteeAmount > availableDeposits)
            {
                _logger.LogWarning($"Guarantor {memberNo} cannot guarantee {guaranteeAmount:C}. Available deposits: {availableDeposits:C}");
                return false;
            }

            _logger.LogInformation($"Guarantor {memberNo} is eligible. Total deposits: {totalDeposits:C}, Locked: {existingGuarantees:C}, Available: {availableDeposits:C}, Can guarantee: {guaranteeAmount:C}");

            return true;
        }
        public async Task<decimal> GetMemberEligibleSharesForGuaranteeAsync(string memberNo, string companyCode)
        {
            // Get share types that can be used for guarantee
            var eligibleShareTypes = await _context.Sharetypes
                .Where(s => s.CompanyCode == companyCode && s.UsedToGuarantee == true)
                .ToListAsync();

            if (!eligibleShareTypes.Any())
            {
                _logger.LogWarning($"No share types configured with UsedToGuarantee=true for company {companyCode}");
                return 0;
            }

            decimal totalEligibleShares = 0;

            foreach (var shareType in eligibleShareTypes)
            {
                var memberShares = await _context.Shares
                    .Where(s => s.MemberNo == memberNo &&
                               s.Sharescode == shareType.SharesCode &&
                               s.CompanyCode == companyCode)
                    .SumAsync(s => s.TotalShares ?? 0);

                if (memberShares > 0)
                {
                    totalEligibleShares += memberShares;
                    _logger.LogInformation($"Share type {shareType.SharesType}: {memberShares:N0}");
                }
            }

            return totalEligibleShares;
        }

        #endregion


        #region Collateral Guarantee Management
        public async Task<List<MemberCollateralDTO>> GetMemberAvailableCollateralsAsync(string memberNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Getting available collaterals for member: {memberNo}");

                // Get all collateral types from Collateral table
                var collateralTypes = await _context.Collaterals
                    .Where(c => c.CompanyCode == companyCode)
                    .ToDictionaryAsync(c => c.ColCode, c => c);

                // Get all collateral guarantees for this member (active ones)
                var activeGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.MemberNo == memberNo && cg.CompanyCode == companyCode && cg.Balance > 0)
                    .ToListAsync();

                // Get loans that are still active for these guarantees
                var activeLoanNos = activeGuarantees.Select(cg => cg.LoanNo).Distinct().ToList();
                var activeLoans = await _context.Loans
                    .Where(l => activeLoanNos.Contains(l.LoanNo) &&
                               l.CompanyCode == companyCode &&
                               l.Status != (int)Status.Closed &&
                               l.Status != (int)Status.Rejected &&
                               l.Status != (int)Status.WrittenOff)
                    .Select(l => l.LoanNo)
                    .ToListAsync();

                // Filter only guarantees for active loans
                var activeGuaranteesForActiveLoans = activeGuarantees
                    .Where(cg => activeLoans.Contains(cg.LoanNo))
                    .ToList();

                // Group by collateral to calculate used amounts
                var usedAmountByCollateral = activeGuaranteesForActiveLoans
                    .GroupBy(cg => cg.ColCode)
                    .ToDictionary(g => g.Key, g => g.Sum(cg => cg.Balance));

                var result = new List<MemberCollateralDTO>();

                foreach (var guarantee in activeGuaranteesForActiveLoans)
                {
                    if (!collateralTypes.ContainsKey(guarantee.ColCode))
                        continue;

                    var collateral = collateralTypes[guarantee.ColCode];

                    result.Add(new MemberCollateralDTO
                    {
                        Id = guarantee.Id,
                        ColCode = guarantee.ColCode,
                        Coldescription = collateral.Coldescription,
                        DocNo = guarantee.DocNo,
                        MarketValue = guarantee.Mktvalue,
                        Percentage = collateral.Percentage,
                        MaxGuaranteeAmount = guarantee.Mktvalue * (decimal)(collateral.Percentage / 100),
                        CurrentlyUsedAmount = guarantee.Balance,
                        AvailableAmount = (guarantee.Mktvalue * (decimal)(collateral.Percentage / 100)) - guarantee.Balance,
                        IsActive = true,
                        LoanNoGuaranteeing = guarantee.LoanNo
                    });
                }

                // Also include collaterals that are not yet assigned to any loan
                // Note: This would require a MemberCollateral table - for now, we assume
                // collaterals are added via the Collateral table and assigned to members via ColloanGuar

                _logger.LogInformation($"Found {result.Count} active collateral guarantees for member {memberNo}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting available collaterals for member {memberNo}");
                return new List<MemberCollateralDTO>();
            }
        }

        public async Task<ColloanGuar> AssignCollateralGuaranteeAsync(CollateralGuaranteeDTO guaranteeDto, string assignedBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Assigning collateral {guaranteeDto.ColCode} to loan {guaranteeDto.LoanNo}");

                // Get the loan
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == guaranteeDto.LoanNo && l.CompanyCode == guaranteeDto.CompanyCode);

                if (loan == null)
                    throw new InvalidOperationException($"Loan {guaranteeDto.LoanNo} not found");

                // Store old loan status for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                bool loanStatusChanged = false;

                // Check loan status
                if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
                    throw new InvalidOperationException($"Cannot assign collateral to loan in status '{loan.Status}'");

                // Get collateral type
                var collateral = await _context.Collaterals
                    .FirstOrDefaultAsync(c => c.ColCode == guaranteeDto.ColCode && c.CompanyCode == guaranteeDto.CompanyCode);

                if (collateral == null)
                    throw new InvalidOperationException($"Collateral type {guaranteeDto.ColCode} not found");

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == guaranteeDto.MemberNo && m.CompanyCode == guaranteeDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : guaranteeDto.MemberNo;

                // Check if this document is already used for an active loan
                var existingGuarantee = await _context.ColloanGuars
                    .FirstOrDefaultAsync(cg => cg.ColCode == guaranteeDto.ColCode &&
                                               cg.DocNo == guaranteeDto.DocNo &&
                                               cg.MemberNo == guaranteeDto.MemberNo &&
                                               cg.Balance > 0 &&
                                               cg.CompanyCode == guaranteeDto.CompanyCode);

                if (existingGuarantee != null)
                    throw new InvalidOperationException($"This collateral (DocNo: {guaranteeDto.DocNo}) is already used to guarantee loan {existingGuarantee.LoanNo}");

                // Calculate maximum allowed guarantee amount based on collateral percentage
                decimal maxGuaranteeAmount = guaranteeDto.MarketValue * (decimal)(collateral.Percentage / 100);

                if (guaranteeDto.GuaranteeAmount > maxGuaranteeAmount)
                    throw new InvalidOperationException($"Guarantee amount {guaranteeDto.GuaranteeAmount:C} exceeds maximum allowed {maxGuaranteeAmount:C} ({collateral.Percentage}% of market value)");

                // Check if loan amount is covered
                var existingCollateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == guaranteeDto.LoanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                var existingLoanguar = await _context.Loanguar
                    .Where(lg => lg.LoanNo == guaranteeDto.LoanNo && lg.Transfered == false)
                    .SumAsync(lg => lg.Amount ?? 0);

                var totalGuaranteeBefore = existingCollateralGuarantees + existingLoanguar;
                var totalGuaranteeAfter = totalGuaranteeBefore + guaranteeDto.GuaranteeAmount;
                var loanAmount = loan.LoanAmt ?? 0;

                if (totalGuaranteeAfter > loanAmount)
                    throw new InvalidOperationException($"Total guarantee amount ({totalGuaranteeAfter:C}) exceeds loan amount ({loanAmount:C})");

                // Create the collateral guarantee record
                var colloanGuar = new ColloanGuar
                {
                    ColCode = guaranteeDto.ColCode,
                    MemberNo = guaranteeDto.MemberNo,
                    DocNo = guaranteeDto.DocNo,
                    Mktvalue = guaranteeDto.MarketValue,
                    LoanNo = guaranteeDto.LoanNo,
                    Balance = guaranteeDto.GuaranteeAmount,
                    AuditId = assignedBy,
                    CompanyCode = guaranteeDto.CompanyCode
                };

                _context.ColloanGuars.Add(colloanGuar);
                await _context.SaveChangesAsync();

                // ============================================================
                // UPDATE LOAN STATUS AFTER ADDING COLLATERAL
                // ============================================================
                // Check if loan is now fully guaranteed or has any guarantee
                var updatedCollateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == guaranteeDto.LoanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                var updatedMemberGuarantees = await _context.Loanguar
                    .Where(lg => lg.LoanNo == guaranteeDto.LoanNo && lg.Transfered == false)
                    .SumAsync(lg => lg.Amount ?? 0);

                var newTotalGuarantee = updatedCollateralGuarantees + updatedMemberGuarantees;

                // If loan is fully guaranteed or has any guarantee, update status to Submitted
                if (loan.Status == (int)Status.Draft && newTotalGuarantee > 0)
                {
                    loan.Status = (int)Status.Submitted;
                    loan.Posted = "SUBMIT";
                    loan.UserName = assignedBy;
                    loan.AuditDateTime = DateTime.Now;
                    loanStatusChanged = true;
                    _context.Loans.Update(loan);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Loan {loan.LoanNo} status updated from Draft to Submitted. Total guarantees: {newTotalGuarantee:C}");
                }
                else if (newTotalGuarantee >= loanAmount && loan.Status != (int)Status.Closed)
                {
                    // If fully guaranteed, ensure status is Submitted (or Approved if you want auto-approve)
                    if (loan.Status == (int)Status.Draft)
                    {
                        loan.Status = (int)Status.Submitted;
                        loan.Posted = "SUBMIT";
                        loan.UserName = assignedBy;
                        loan.AuditDateTime = DateTime.Now;
                        loanStatusChanged = true;
                        _context.Loans.Update(loan);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Loan {loan.LoanNo} is now fully guaranteed! Status: Submitted");
                    }
                }

                // Record blockchain transaction
                var blockchainData = new
                {
                    Action = "COLLATERAL_GUARANTEE_ASSIGN",
                    CollateralGuaranteeId = colloanGuar.Id,
                    LoanNo = guaranteeDto.LoanNo,
                    MemberNo = guaranteeDto.MemberNo,
                    MemberName = memberName,
                    ColCode = guaranteeDto.ColCode,
                    ColDescription = collateral.Coldescription,
                    DocNo = guaranteeDto.DocNo,
                    MarketValue = guaranteeDto.MarketValue,
                    GuaranteeAmount = guaranteeDto.GuaranteeAmount,
                    Percentage = collateral.Percentage,
                    TotalGuaranteeBefore = totalGuaranteeBefore,
                    TotalGuaranteeAfter = newTotalGuarantee,
                    LoanAmount = loanAmount,
                    LoanStatusBefore = oldLoanStatus,
                    LoanStatusAfter = loan.Status,
                    AssignedBy = assignedBy,
                    AssignedAt = DateTime.Now
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "COLLATERAL_GUARANTEE_ASSIGN",
                    MemberNo = guaranteeDto.MemberNo,
                    CompanyCode = guaranteeDto.CompanyCode,
                    Amount = guaranteeDto.GuaranteeAmount,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = guaranteeDto.LoanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                colloanGuar.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Collateral guarantee assigned successfully. Id: {colloanGuar.Id}, Loan Status: {loan.Status}");

                // ============================================================
                // SAVE AUDIT TRAIL FOR COLLATERAL GUARANTEE ASSIGNMENT
                // ============================================================

                // Create audit extra data
                var auditExtraData = new
                {
                    loanNo = guaranteeDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    collateralOwnerMemberNo = guaranteeDto.MemberNo,
                    collateralOwnerName = memberName,
                    colCode = guaranteeDto.ColCode,
                    colDescription = collateral.Coldescription,
                    documentNo = guaranteeDto.DocNo,
                    marketValue = guaranteeDto.MarketValue,
                    guaranteeAmount = guaranteeDto.GuaranteeAmount,
                    percentageUsed = collateral.Percentage,
                    maxGuaranteeAllowed = maxGuaranteeAmount,
                    totalGuaranteeBefore = totalGuaranteeBefore,
                    totalGuaranteeAfter = newTotalGuarantee,
                    loanAmount = loanAmount,
                    isFullyGuaranteed = newTotalGuarantee >= loanAmount,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanStatusChanged = loanStatusChanged,
                    assignedBy = assignedBy,
                    assignedDate = DateTime.Now,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Create a copy of the collateral guarantee object for NewValue
                var collateralForAudit = new
                {
                    colloanGuar.Id,
                    colloanGuar.ColCode,
                    colloanGuar.MemberNo,
                    colloanGuar.DocNo,
                    colloanGuar.Mktvalue,
                    colloanGuar.LoanNo,
                    colloanGuar.Balance,
                    colloanGuar.AuditId,
                    colloanGuar.CompanyCode,
                    AssignedBy = assignedBy,
                    AssignedDate = DateTime.Now,
                    CollateralDescription = collateral.Coldescription,
                    PercentageUsed = collateral.Percentage,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,  // For Insert, OldValue is null (no previous collateral record)
                    newModel: collateralForAudit,  // This will be serialized to NewValue column
                    tableName: "ColloanGuar",
                    recordId: colloanGuar.Id.ToString(),
                    userId: assignedBy,
                    userName: assignedBy,
                    companyCode: guaranteeDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // If loan status changed, also audit the loan status change
                if (loanStatusChanged)
                {
                    var loanAuditExtraData = new
                    {
                        loanNo = guaranteeDto.LoanNo,
                        statusChangedFrom = oldLoanStatus,
                        statusChangedTo = loan.Status,
                        reason = $"Collateral guarantee assigned. Total guarantees: {newTotalGuarantee:C}",
                        triggeredBy = assignedBy,
                        triggeredDate = DateTime.Now,
                        collateralId = colloanGuar.Id,
                        collateralDocNo = guaranteeDto.DocNo,
                        collateralAmount = guaranteeDto.GuaranteeAmount,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var loanForAudit = new
                    {
                        loan.LoanNo,
                        loan.Status,
                        loan.Posted,
                        loan.UserName,
                        loan.AuditDateTime,
                        UpdatedBy = assignedBy,
                        UpdateReason = "Collateral guarantee assigned - loan status changed to Submitted"
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Update,
                        oldModel: new { Status = oldLoanStatus, Posted = oldLoanPosted },
                        newModel: loanForAudit,
                        tableName: "Loans",
                        recordId: guaranteeDto.LoanNo,
                        userId: assignedBy,
                        userName: assignedBy,
                        companyCode: guaranteeDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );

                    _logger.LogInformation($"Loan status change audited for {guaranteeDto.LoanNo}");
                }

                await transaction.CommitAsync();

                return colloanGuar;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error assigning collateral guarantee for loan {guaranteeDto.LoanNo}");
                throw;
            }
        }

        //public async Task<ColloanGuar> AssignCollateralGuaranteeAsync(CollateralGuaranteeDTO guaranteeDto, string assignedBy)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Assigning collateral {guaranteeDto.ColCode} to loan {guaranteeDto.LoanNo}");

        //        // Get the loan
        //        var loan = await _context.Loans
        //            .FirstOrDefaultAsync(l => l.LoanNo == guaranteeDto.LoanNo && l.CompanyCode == guaranteeDto.CompanyCode);

        //        if (loan == null)
        //            throw new InvalidOperationException($"Loan {guaranteeDto.LoanNo} not found");

        //        // Check loan status
        //        if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
        //            throw new InvalidOperationException($"Cannot assign collateral to loan in status '{loan.Status}'");

        //        // Get collateral type
        //        var collateral = await _context.Collaterals
        //            .FirstOrDefaultAsync(c => c.ColCode == guaranteeDto.ColCode && c.CompanyCode == guaranteeDto.CompanyCode);

        //        if (collateral == null)
        //            throw new InvalidOperationException($"Collateral type {guaranteeDto.ColCode} not found");

        //        // Check if this document is already used for an active loan
        //        var existingGuarantee = await _context.ColloanGuars
        //            .FirstOrDefaultAsync(cg => cg.ColCode == guaranteeDto.ColCode &&
        //                                       cg.DocNo == guaranteeDto.DocNo &&
        //                                       cg.MemberNo == guaranteeDto.MemberNo &&
        //                                       cg.Balance > 0 &&
        //                                       cg.CompanyCode == guaranteeDto.CompanyCode);

        //        if (existingGuarantee != null)
        //            throw new InvalidOperationException($"This collateral (DocNo: {guaranteeDto.DocNo}) is already used to guarantee loan {existingGuarantee.LoanNo}");

        //        // Calculate maximum allowed guarantee amount based on collateral percentage
        //        decimal maxGuaranteeAmount = guaranteeDto.MarketValue * (decimal)(collateral.Percentage / 100);

        //        if (guaranteeDto.GuaranteeAmount > maxGuaranteeAmount)
        //            throw new InvalidOperationException($"Guarantee amount {guaranteeDto.GuaranteeAmount:C} exceeds maximum allowed {maxGuaranteeAmount:C} ({collateral.Percentage}% of market value)");

        //        // Check if loan amount is covered
        //        var existingCollateralGuarantees = await _context.ColloanGuars
        //            .Where(cg => cg.LoanNo == guaranteeDto.LoanNo && cg.Balance > 0)
        //            .SumAsync(cg => cg.Balance);

        //        var existingLoanguar = await _context.Loanguar
        //            .Where(lg => lg.LoanNo == guaranteeDto.LoanNo && lg.Transfered == false)
        //            .SumAsync(lg => lg.Amount ?? 0);

        //        var totalGuarantee = existingCollateralGuarantees + existingLoanguar + guaranteeDto.GuaranteeAmount;
        //        var loanAmount = loan.LoanAmt ?? 0;

        //        if (totalGuarantee > loanAmount)
        //            throw new InvalidOperationException($"Total guarantee amount ({totalGuarantee:C}) exceeds loan amount ({loanAmount:C})");

        //        // Create the collateral guarantee record
        //        var colloanGuar = new ColloanGuar
        //        {
        //            ColCode = guaranteeDto.ColCode,
        //            MemberNo = guaranteeDto.MemberNo,
        //            DocNo = guaranteeDto.DocNo,
        //            Mktvalue = guaranteeDto.MarketValue,
        //            LoanNo = guaranteeDto.LoanNo,
        //            Balance = guaranteeDto.GuaranteeAmount,
        //            AuditId = assignedBy,
        //            CompanyCode = guaranteeDto.CompanyCode
        //        };

        //        _context.ColloanGuars.Add(colloanGuar);
        //        await _context.SaveChangesAsync();

        //        // ============================================================
        //        // FIX: UPDATE LOAN STATUS AFTER ADDING COLLATERAL
        //        // ============================================================
        //        // Check if loan is now fully guaranteed or has any guarantee
        //        var updatedCollateralGuarantees = await _context.ColloanGuars
        //            .Where(cg => cg.LoanNo == guaranteeDto.LoanNo && cg.Balance > 0)
        //            .SumAsync(cg => cg.Balance);

        //        var updatedMemberGuarantees = await _context.Loanguar
        //            .Where(lg => lg.LoanNo == guaranteeDto.LoanNo && lg.Transfered == false)
        //            .SumAsync(lg => lg.Amount ?? 0);

        //        var newTotalGuarantee = updatedCollateralGuarantees + updatedMemberGuarantees;

        //        // If loan is fully guaranteed or has any guarantee, update status to Submitted
        //        if (loan.Status == (int)Status.Draft && newTotalGuarantee > 0)
        //        {
        //            loan.Status = (int)Status.Submitted;
        //            loan.Posted = "SUBMIT";
        //            loan.UserName = assignedBy;
        //            loan.AuditDateTime = DateTime.Now;
        //            _context.Loans.Update(loan);
        //            await _context.SaveChangesAsync();
        //            _logger.LogInformation($"Loan {loan.LoanNo} status updated from Draft to Submitted. Total guarantees: {newTotalGuarantee:C}");
        //        }
        //        else if (newTotalGuarantee >= loanAmount && loan.Status != (int)Status.Closed)
        //        {
        //            // If fully guaranteed, ensure status is Submitted (or Approved if you want auto-approve)
        //            if (loan.Status == (int)Status.Draft)
        //            {
        //                loan.Status = (int)Status.Submitted;
        //                loan.Posted = "SUBMIT";
        //                loan.UserName = assignedBy;
        //                loan.AuditDateTime = DateTime.Now;
        //                _context.Loans.Update(loan);
        //                await _context.SaveChangesAsync();
        //                _logger.LogInformation($"Loan {loan.LoanNo} is now fully guaranteed! Status: Submitted");
        //            }
        //        }

        //        // Record blockchain transaction
        //        var blockchainData = new
        //        {
        //            Action = "COLLATERAL_GUARANTEE_ASSIGN",
        //            CollateralGuaranteeId = colloanGuar.Id,
        //            LoanNo = guaranteeDto.LoanNo,
        //            MemberNo = guaranteeDto.MemberNo,
        //            ColCode = guaranteeDto.ColCode,
        //            DocNo = guaranteeDto.DocNo,
        //            MarketValue = guaranteeDto.MarketValue,
        //            GuaranteeAmount = guaranteeDto.GuaranteeAmount,
        //            Percentage = collateral.Percentage,
        //            TotalGuaranteeAfter = newTotalGuarantee,
        //            LoanStatusAfter = loan.Status,
        //            AssignedBy = assignedBy,
        //            AssignedAt = DateTime.Now
        //        };

        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "COLLATERAL_GUARANTEE_ASSIGN",
        //            MemberNo = guaranteeDto.MemberNo,
        //            CompanyCode = guaranteeDto.CompanyCode,
        //            Amount = guaranteeDto.GuaranteeAmount,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = guaranteeDto.LoanNo,
        //            Status = "PENDING",
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        colloanGuar.BlockchainTxId = blockchainTx.TransactionId;
        //        await _context.SaveChangesAsync();

        //        _logger.LogInformation($"Collateral guarantee assigned successfully. Id: {colloanGuar.Id}, Loan Status: {loan.Status}");

        //        await transaction.CommitAsync();

        //        return colloanGuar;
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error assigning collateral guarantee for loan {guaranteeDto.LoanNo}");
        //        throw;
        //    }
        //}
        public async Task<List<CollateralGuaranteeResponseDTO>> GetLoanCollateralGuaranteesAsync(string loanNo)
        {
            try
            {
                _logger.LogInformation($"Getting collateral guarantees for loan: {loanNo}");

                // Get all collateral guarantees for this loan with Balance > 0
                var guarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .ToListAsync();

                _logger.LogInformation($"Found {guarantees.Count} collateral guarantee records for loan {loanNo}");

                if (!guarantees.Any())
                    return new List<CollateralGuaranteeResponseDTO>();

                // Get collateral descriptions
                var colCodes = guarantees.Select(g => g.ColCode).Distinct().ToList();
                var collaterals = await _context.Collaterals
                    .Where(c => colCodes.Contains(c.ColCode))
                    .ToDictionaryAsync(c => c.ColCode, c => c);

                var result = new List<CollateralGuaranteeResponseDTO>();

                foreach (var guarantee in guarantees)
                {
                    var collateral = collaterals.GetValueOrDefault(guarantee.ColCode);

                    result.Add(new CollateralGuaranteeResponseDTO
                    {
                        Id = guarantee.Id,
                        ColCode = guarantee.ColCode,
                        Coldescription = collateral?.Coldescription ?? guarantee.ColCode,
                        DocNo = guarantee.DocNo,
                        MarketValue = guarantee.Mktvalue,
                        GuaranteeAmount = guarantee.Balance,
                        RemainingBalance = guarantee.Balance,
                        AssignedDate =  DateTime.Now,
                        BlockchainTxId = guarantee.BlockchainTxId
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting collateral guarantees for loan {loanNo}");
                return new List<CollateralGuaranteeResponseDTO>();
            }
        }

        //public async Task<List<CollateralGuaranteeResponseDTO>> GetLoanCollateralGuaranteesAsync(string loanNo)
        //{
        //    try
        //    {
        //        _logger.LogInformation($"Getting collateral guarantees for loan: {loanNo}");

        //        // Get all collateral guarantees for this loan with Balance > 0
        //        var guarantees = await _context.ColloanGuars
        //            .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
        //            .ToListAsync();

        //        _logger.LogInformation($"Found {guarantees.Count} collateral guarantee records for loan {loanNo}");

        //        if (!guarantees.Any())
        //            return new List<CollateralGuaranteeResponseDTO>();

        //        // Get collateral descriptions
        //        var colCodes = guarantees.Select(g => g.ColCode).Distinct().ToList();
        //        var collaterals = await _context.Collaterals
        //            .Where(c => colCodes.Contains(c.ColCode))
        //            .ToDictionaryAsync(c => c.ColCode, c => c);

        //        _logger.LogInformation($"Found {collaterals.Count} collateral types for codes: {string.Join(", ", colCodes)}");

        //        var result = new List<CollateralGuaranteeResponseDTO>();

        //        foreach (var guarantee in guarantees)
        //        {
        //            var collateral = collaterals.GetValueOrDefault(guarantee.ColCode);

        //            _logger.LogInformation($"  - Guarantee: ColCode={guarantee.ColCode}, DocNo={guarantee.DocNo}, Balance={guarantee.Balance:C}");

        //            result.Add(new CollateralGuaranteeResponseDTO
        //            {
        //                Id = guarantee.Id,
        //                ColCode = guarantee.ColCode,
        //                Coldescription = collateral?.Coldescription ?? guarantee.ColCode,
        //                DocNo = guarantee.DocNo,
        //                MarketValue = guarantee.Mktvalue,
        //                GuaranteeAmount = guarantee.Balance,
        //                RemainingBalance = guarantee.Balance,
        //                AssignedDate = DateTime.Now,
        //                BlockchainTxId = guarantee.BlockchainTxId
        //            });
        //        }

        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Error getting collateral guarantees for loan {loanNo}");
        //        return new List<CollateralGuaranteeResponseDTO>();
        //    }
        //}

        public async Task<bool> ReleaseCollateralGuaranteeAsync(long collateralGuaranteeId, string releasedBy, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var guarantee = await _context.ColloanGuars
                    .FirstOrDefaultAsync(cg => cg.Id == collateralGuaranteeId);

                if (guarantee == null)
                    throw new InvalidOperationException($"Collateral guarantee with ID {collateralGuaranteeId} not found");

                if (guarantee.Balance <= 0)
                    throw new InvalidOperationException("Collateral guarantee already released");

                var originalBalance = guarantee.Balance;

                // Get the loan before releasing
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == guarantee.LoanNo && l.CompanyCode == guarantee.CompanyCode);

                // Release by setting balance to 0
                guarantee.Balance = 0;
                guarantee.AuditId = releasedBy;

                await _context.SaveChangesAsync();

                // ============================================================
                // FIX: RECALCULATE LOAN STATUS AFTER RELEASING COLLATERAL
                // ============================================================
                if (loan != null)
                {
                    // Get remaining guarantees
                    var remainingCollateralGuarantees = await _context.ColloanGuars
                        .Where(cg => cg.LoanNo == guarantee.LoanNo && cg.Balance > 0)
                        .SumAsync(cg => cg.Balance);

                    var remainingMemberGuarantees = await _context.Loanguar
                        .Where(lg => lg.LoanNo == guarantee.LoanNo && lg.Transfered == false)
                        .SumAsync(lg => lg.Amount ?? 0);

                    var totalRemainingGuarantee = remainingCollateralGuarantees + remainingMemberGuarantees;
                    var loanAmount = loan.LoanAmt ?? 0;

                    // If no guarantees left, revert to Draft
                    if (totalRemainingGuarantee <= 0 && loan.Status == (int)Status.Submitted)
                    {
                        loan.Status = (int)Status.Draft;
                        loan.Posted = "Draft";
                        loan.UserName = releasedBy;
                        loan.AuditDateTime = DateTime.Now;
                        _context.Loans.Update(loan);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Loan {loan.LoanNo} reverted to Draft - no guarantees remaining");
                    }
                    // If still has guarantees but not fully covered, keep as Submitted
                    else if (totalRemainingGuarantee < loanAmount && loan.Status == (int)Status.Submitted)
                    {
                        // Keep as Submitted, no change needed
                        _logger.LogInformation($"Loan {loan.LoanNo} remains in Submitted status with {totalRemainingGuarantee:C} guarantee remaining");
                    }
                }

                // Record blockchain transaction for release
                var blockchainData = new
                {
                    Action = "COLLATERAL_GUARANTEE_RELEASE",
                    CollateralGuaranteeId = guarantee.Id,
                    LoanNo = guarantee.LoanNo,
                    MemberNo = guarantee.MemberNo,
                    ColCode = guarantee.ColCode,
                    DocNo = guarantee.DocNo,
                    OriginalBalance = originalBalance,
                    ReleasedBy = releasedBy,
                    Reason = reason,
                    ReleasedAt = DateTime.Now
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "COLLATERAL_GUARANTEE_RELEASE",
                    MemberNo = guarantee.MemberNo,
                    CompanyCode = guarantee.CompanyCode,
                    Amount = originalBalance,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = guarantee.LoanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                guarantee.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Collateral guarantee {collateralGuaranteeId} released. Amount: {originalBalance:C}, Reason: {reason}");

                await transaction.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error releasing collateral guarantee {collateralGuaranteeId}");
                throw;
            }
        }
        public async Task<decimal> GetTotalCollateralGuaranteeAmountAsync(string loanNo)
        {
            try
            {
                return await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting total collateral guarantee for loan {loanNo}");
                return 0;
            }
        }
        public async Task<decimal> GetTotalGuaranteeForLoanAsync(string loanNo, string companyCode)
        {
            try
            {
                // Get member guarantors total
                var memberGuarantee = await _context.Loanguar
                    .Where(g => g.LoanNo == loanNo && g.Transfered == false)
                    .SumAsync(g => g.Amount ?? 0);

                // Get collateral guarantees total
                var collateralGuarantee = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                var total = memberGuarantee + collateralGuarantee;

                _logger.LogInformation($"Loan {loanNo} - Member Guarantee: {memberGuarantee:C}, Collateral Guarantee: {collateralGuarantee:C}, Total: {total:C}");

                return total;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting total guarantee for loan {loanNo}");
                return 0;
            }
        }
        public async Task<(bool IsValid, string Message, AvailableCollateralDTO? Data)> ValidateCollateralForLoanAsync(
            string memberNo, string colCode, string loanNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Validating collateral {colCode} for member {memberNo} on loan {loanNo}");

                // Get the loan
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                    return (false, "Loan not found", null);

                // Get the collateral type
                var collateral = await _context.Collaterals
                    .FirstOrDefaultAsync(c => c.ColCode == colCode && c.CompanyCode == companyCode);

                if (collateral == null)
                    return (false, $"Collateral type {colCode} not found", null);

                // Check if this member owns this collateral (this would come from a MemberCollateral table)
                // For now, we assume the member can use any collateral type
                // In a real system, you'd have a MemberCollateral table linking members to their collaterals

                // Get existing collateral guarantees for this loan
                var existingCollateralGuarantee = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                // Get existing member guarantees
                var existingMemberGuarantee = await _context.Loanguar
                    .Where(lg => lg.LoanNo == loanNo && lg.Transfered == false)
                    .SumAsync(lg => lg.Amount ?? 0);

                var totalExistingGuarantee = existingCollateralGuarantee + existingMemberGuarantee;
                var remainingLoanAmount = (loan.LoanAmt ?? 0) - totalExistingGuarantee;

                if (remainingLoanAmount <= 0)
                    return (false, "Loan is already fully guaranteed", null);

                // Maximum guarantee from this collateral
                decimal maxGuaranteeAmount = 0;
                // This would need the member's specific collateral market value
                // For now, return the collateral type info without specific amount

                var availableCollateral = new AvailableCollateralDTO
                {
                    ColCode = collateral.ColCode,
                    Coldescription = collateral.Coldescription,
                    Percentage = collateral.Percentage,
                    MaxGuaranteeAmount = 0, // Would need member's specific collateral value
                    IsAvailable = true,
                    OriginalMarketValue = 0,
                    ExistingGuaranteeBalance = 0
                };

                return (true, $"Collateral {collateral.Coldescription} can be used up to {collateral.Percentage}% of its market value", availableCollateral);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating collateral for loan {loanNo}");
                return (false, $"Error validating collateral: {ex.Message}", null);
            }
        }

        #endregion


        #region Loan Appraisal

        public async Task<Appraisal> AppraiseLoanAsync(LoanAppraisalDTO appraisalDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Starting appraisal for loan {appraisalDto.LoanNo}");

                var loan = await GetLoanByNoForDisplayAsync(appraisalDto.LoanNo, appraisalDto.CompanyCode);

                if (loan == null)
                {
                    throw new InvalidOperationException($"Loan {appraisalDto.LoanNo} not found");
                }

                _logger.LogInformation($"Current loan status: {loan.Status}");
                _logger.LogInformation($"Recommended amount: {appraisalDto.RecommendedAmount}");
                _logger.LogInformation($"Appraisal decision: {appraisalDto.AppraisalDecision}");

                if (loan.Status != (int)Status.Submitted)
                {
                    throw new InvalidOperationException($"Loan cannot be appraised. Current status: {loan.Status}. Expected: Submitted");
                }

                var existingAppraisal = await _context.Appraisal
                    .FirstOrDefaultAsync(a => a.LoanNo == appraisalDto.LoanNo);

                if (existingAppraisal != null)
                {
                    throw new InvalidOperationException("This loan has already been appraised.");
                }

                // Get member details
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == appraisalDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;

                // Get loan type to get interest rate
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == appraisalDto.CompanyCode);

                // ============================================================
                // STORE OLD VALUES FOR AUDIT
                // ============================================================
                decimal oldLoanAmount = loan.LoanAmt ?? 0;
                decimal oldInterestRate = loan.Interest ?? 0;
                int oldRepayPeriod = loan.RepayPeriod ?? 0;
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";

                // ============================================================
                // GET GUARANTEES AND CHECK SELF-GUARANTEE
                // ============================================================
                var memberGuarantors = await _context.Loanguar
                    .Where(g => g.LoanNo == appraisalDto.LoanNo && g.Transfered == false)
                    .ToListAsync();
                var totalMemberGuarantee = memberGuarantors.Sum(g => g.Amount ?? 0);

                var collateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == appraisalDto.LoanNo && cg.Balance > 0)
                    .ToListAsync();
                var totalCollateralGuarantee = collateralGuarantees.Sum(cg => cg.Balance);

                var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;
                var isApplicantGuarantor = memberGuarantors.Any(g => g.MemberNo == loan.MemberNo);

                // Check if loan requires guarantors
                var requiresGuarantor = !string.IsNullOrEmpty(loanType.Guarantor) &&
                                        loanType.Guarantor != "No" &&
                                        loanType.Guarantor != "N";

                bool canProceedToAppraisal = false;

                if (requiresGuarantor)
                {
                    if (isSelfGuarantee && isApplicantGuarantor)
                    {
                        canProceedToAppraisal = true;
                        _logger.LogInformation($"Self-guarantee enabled - Applicant is guarantor. Proceeding with appraisal even with partial guarantee. Total: {totalGuarantee:C}, Loan: {loan.LoanAmt:C}");
                    }
                    else if (totalGuarantee >= (loan.LoanAmt ?? 0))
                    {
                        canProceedToAppraisal = true;
                        _logger.LogInformation($"Loan fully guaranteed: {totalGuarantee:C} >= {loan.LoanAmt:C}");
                    }
                    else if (totalGuarantee > 0)
                    {
                        if (isSelfGuarantee)
                        {
                            canProceedToAppraisal = true;
                            _logger.LogInformation($"Partial guarantee ({totalGuarantee:C}) allowed due to self-guarantee enabled");
                        }
                        else
                        {
                            throw new InvalidOperationException($"Loan is not fully guaranteed. Total guarantee: {totalGuarantee:C}, Loan amount: {loan.LoanAmt:C}. Remaining: {(loan.LoanAmt ?? 0) - totalGuarantee:C}. Please add more guarantees or enable self-guarantee.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot appraise loan. No guarantees found. Please add member guarantors or collateral guarantees.");
                    }
                }
                else
                {
                    canProceedToAppraisal = true;
                }

                if (!canProceedToAppraisal)
                {
                    throw new InvalidOperationException("Cannot proceed with appraisal. Please ensure guarantees are in place.");
                }

                // Calculate values
                decimal memberShares = member?.ShareCap ?? 0;
                decimal monthlyIncome = (decimal)(member?.MonthlyContr ?? 0);
                decimal existingLoans = appraisalDto.ExistingLoanObligations;
                decimal recommendedAmount = appraisalDto.RecommendedAmount;

                // INTEREST RATE - USE AS-IS FROM LOAN TYPE (store as percentage, e.g., 15 for 15%)
                decimal interestRatePercent = 0;
                if (loanType != null && !string.IsNullOrEmpty(loanType.Interest))
                {
                    string interestStr = loanType.Interest.ToString().Replace("%", "");
                    if (decimal.TryParse(interestStr, out interestRatePercent))
                    {
                        if (interestRatePercent < 1 && interestRatePercent > 0)
                        {
                            interestRatePercent = interestRatePercent * 100;
                        }
                    }
                }

                decimal interestRateDecimal = interestRatePercent / 100;
                decimal monthlyInterestRate = interestRateDecimal / 12;
                int repayPeriod = appraisalDto.RecommendedPeriod;

                decimal monthlyPayment = 0;
                decimal totalInterest = 0;

                if (monthlyInterestRate > 0 && repayPeriod > 0)
                {
                    decimal factor = (decimal)Math.Pow((double)(1 + monthlyInterestRate), repayPeriod);
                    monthlyPayment = recommendedAmount * monthlyInterestRate * factor / (factor - 1);
                    totalInterest = (monthlyPayment * repayPeriod) - recommendedAmount;
                }
                else
                {
                    monthlyPayment = recommendedAmount / repayPeriod;
                }

                var appraisal = new Appraisal
                {
                    LoanNo = appraisalDto.LoanNo,
                    CompanyCode = appraisalDto.CompanyCode,
                    MemberNo = loan.MemberNo,
                    AppraisDate = DateTime.Now,
                    AuditTime = DateTime.Now,
                    AuditID = appraisalDto.AppraisedBy,
                    OfficerNames = appraisalDto.AppraisedBy,
                    TransactionNo = Guid.NewGuid().ToString().Substring(0, 15),
                    Salary = monthlyIncome,
                    Allowances = 0,
                    Shares = memberShares,
                    Loans = existingLoans,
                    Deductions = 0,
                    AmtRecommended = recommendedAmount,
                    TotalDeductions = 0,
                    Principal = recommendedAmount,
                    Interest = interestRatePercent,
                    TotalInterest = totalInterest,
                    RepayMethod = loan?.RepayMethod ?? "STL",
                    RepayRate = monthlyPayment,
                    Reason = appraisalDto.AppraisalNotes,
                    TInterest = interestRatePercent,
                    NetMonthlySalary = monthlyIncome - existingLoans,
                    SocietyPayment = monthlyPayment,
                    ExpectedNetSalary = monthlyIncome - existingLoans - monthlyPayment,
                    DeductionToGross = 0,
                    TotalDedNewLoanToGross = 0,
                    NetSalaryToGross = 0,
                    TotalLoanToGross = 0,
                    TotalCoopDedToGross = 0,
                    BankLoan = 0,
                    Nssf = 0,
                    CopLoanded = 0,
                    OtherDed = 0,
                    StatutoryDed = 0,
                    StatutoryDedToGross = 0,
                    TotalDedToGrossLessStatutory = 0,
                    NoOfLoans = await _context.Loans.CountAsync(l => l.MemberNo == loan.MemberNo && l.CompanyCode == appraisalDto.CompanyCode),
                    LoanGuarantor = 0
                };

                _logger.LogInformation($"Created appraisal record with decision: {appraisalDto.AppraisalDecision}");
                _logger.LogInformation($"Interest Rate: {interestRatePercent}%");

                string oldStatus = loan.Status.ToString();
                bool loanStatusChanged = false;

                if (appraisalDto.AppraisalDecision == "Recommend")
                {
                    loan.LoanAmt = recommendedAmount;
                    loan.Interest = interestRatePercent;
                    loan.RepayPeriod = repayPeriod;
                    loan.Status = (int)Status.Approved;
                    loan.Posted = "APPROVED";
                    loan.UserName = appraisalDto.AppraisedBy;
                    loan.AuditDateTime = DateTime.Now;
                    loanStatusChanged = true;

                    _logger.LogInformation($"Loan status updated from {oldStatus} to Approved");
                }
                else if (appraisalDto.AppraisalDecision == "NotRecommend")
                {
                    loan.Status = (int)Status.Rejected;
                    loan.Posted = "REJECTED";
                    loan.AddSecurity = $"Rejected at appraisal: {appraisalDto.AppraisalNotes}";
                    loan.UserName = appraisalDto.AppraisedBy;
                    loan.AuditDateTime = DateTime.Now;
                    loanStatusChanged = true;

                    _logger.LogInformation($"Loan status updated from {oldStatus} to Rejected");
                }
                else
                {
                    loan.Status = (int)Status.Approved;
                    loan.Posted = "APPROVED";
                    loan.UserName = appraisalDto.AppraisedBy;
                    loan.AuditDateTime = DateTime.Now;
                    loanStatusChanged = true;
                }

                _context.Appraisal.Add(appraisal);
                _context.Loans.Update(loan);
                await _context.SaveChangesAsync();

                // Blockchain transaction
                var blockchainData = new
                {
                    LoanNo = appraisalDto.LoanNo,
                    Amt = recommendedAmount,
                    Rate = interestRatePercent,
                    Period = repayPeriod,
                    Decision = appraisalDto.AppraisalDecision,
                    TotalMemberGuarantee = totalMemberGuarantee,
                    TotalCollateralGuarantee = totalCollateralGuarantee,
                    TotalGuarantee = totalGuarantee,
                    IsSelfGuarantee = isSelfGuarantee,
                    IsApplicantGuarantor = isApplicantGuarantor,
                    By = appraisalDto.AppraisedBy,
                    Date = DateTime.Now,
                    MemberName = memberName,
                    MonthlyIncome = monthlyIncome,
                    ExistingLoans = existingLoans,
                    MonthlyPayment = monthlyPayment,
                    TotalInterest = totalInterest
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_APPRAISAL",
                    MemberNo = loan.MemberNo,
                    CompanyCode = loan.CompanyCode,
                    Amount = recommendedAmount,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = loan.LoanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                appraisal.BlockchainTxId = blockchainTx.TransactionId;
                loan.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Blockchain transaction recorded for loan appraisal: {blockchainTx.TransactionId}");

                // ============================================================
                // SAVE AUDIT TRAIL FOR APPRAISAL
                // ============================================================

                // Create audit extra data
                var auditExtraData = new
                {
                    loanNo = appraisalDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    applicantName = memberName,
                    appraisalDecision = appraisalDto.AppraisalDecision,
                    recommendedAmount = recommendedAmount,
                    originalLoanAmount = oldLoanAmount,
                    amountChanged = oldLoanAmount != recommendedAmount,
                    recommendedPeriod = repayPeriod,
                    originalRepayPeriod = oldRepayPeriod,
                    periodChanged = oldRepayPeriod != repayPeriod,
                    interestRate = interestRatePercent,
                    originalInterestRate = oldInterestRate,
                    interestRateChanged = oldInterestRate != interestRatePercent,
                    monthlyPayment = monthlyPayment,
                    totalInterest = totalInterest,
                    totalMemberGuarantee = totalMemberGuarantee,
                    totalCollateralGuarantee = totalCollateralGuarantee,
                    totalGuarantee = totalGuarantee,
                    isSelfGuarantee = isSelfGuarantee,
                    isApplicantGuarantor = isApplicantGuarantor,
                    requiresGuarantor = requiresGuarantor,
                    memberMonthlyIncome = monthlyIncome,
                    memberExistingLoans = existingLoans,
                    memberNetIncome = monthlyIncome - existingLoans,
                    expectedNetAfterLoan = monthlyIncome - existingLoans - monthlyPayment,
                    appraisalNotes = appraisalDto.AppraisalNotes ?? "",
                    appraisedBy = appraisalDto.AppraisedBy,
                    appraisedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanStatusChanged = loanStatusChanged,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Create a copy of the appraisal object for NewValue
                var appraisalForAudit = new
                {
                    appraisal.Id,
                    appraisal.LoanNo,
                    appraisal.MemberNo,
                    appraisal.AppraisDate,
                    appraisal.AmtRecommended,
                    appraisal.Principal,
                    appraisal.Interest,
                    appraisal.TInterest,
                   // appraisal.RepayPeriod = repayPeriod,
                    appraisal.RepayRate,
                    appraisal.TotalInterest,
                    appraisal.Reason,
                    appraisal.OfficerNames,
                    appraisal.Salary,
                    appraisal.Loans,
                    appraisal.Shares,
                    appraisal.NetMonthlySalary,
                    appraisal.SocietyPayment,
                    appraisal.ExpectedNetSalary,
                    AppraisedBy = appraisalDto.AppraisedBy,
                    AppraisalDecision = appraisalDto.AppraisalDecision,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,  // For Insert, OldValue is null (no previous appraisal record)
                    newModel: appraisalForAudit,  // This will be serialized to NewValue column
                    tableName: "Appraisal",
                    recordId: appraisal.Id.ToString(),
                    userId: appraisalDto.AppraisedBy,
                    userName: appraisalDto.AppraisedBy,
                    companyCode: appraisalDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE
                // ============================================================
                if (loanStatusChanged)
                {
                    var loanAuditExtraData = new
                    {
                        loanNo = appraisalDto.LoanNo,
                        statusChangedFrom = oldLoanStatus,
                        statusChangedTo = loan.Status,
                        reason = $"Loan appraised with decision: {appraisalDto.AppraisalDecision}",
                        appraisalNotes = appraisalDto.AppraisalNotes ?? "",
                        recommendedAmount = recommendedAmount,
                        originalLoanAmount = oldLoanAmount,
                        recommendedPeriod = repayPeriod,
                        originalRepayPeriod = oldRepayPeriod,
                        interestRate = interestRatePercent,
                        triggeredBy = appraisalDto.AppraisedBy,
                        triggeredDate = DateTime.Now,
                        appraisalId = appraisal.Id,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var loanForAudit = new
                    {
                        loan.LoanNo,
                        loan.LoanAmt,
                        loan.Interest,
                        loan.RepayPeriod,
                        loan.Status,
                        loan.Posted,
                        loan.UserName,
                        loan.AuditDateTime,
                        loan.AddSecurity,
                        UpdatedBy = appraisalDto.AppraisedBy,
                        UpdateReason = $"Loan {appraisalDto.AppraisalDecision.ToLower()} during appraisal"
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Update,
                        oldModel: new
                        {
                            Status = oldLoanStatus,
                            Posted = oldLoanPosted,
                            LoanAmt = oldLoanAmount,
                            Interest = oldInterestRate,
                            RepayPeriod = oldRepayPeriod
                        },
                        newModel: loanForAudit,
                        tableName: "Loans",
                        recordId: appraisalDto.LoanNo,
                        userId: appraisalDto.AppraisedBy,
                        userName: appraisalDto.AppraisedBy,
                        companyCode: appraisalDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );

                    _logger.LogInformation($"Loan status change audited for {appraisalDto.LoanNo}");
                }

                await transaction.CommitAsync();

                return appraisal;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error in AppraiseLoanAsync for loan {appraisalDto.LoanNo}");
                throw;
            }
        }

        //public async Task<Appraisal> AppraiseLoanAsync(LoanAppraisalDTO appraisalDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Starting appraisal for loan {appraisalDto.LoanNo}");

        //        var loan = await GetLoanByNoForDisplayAsync(appraisalDto.LoanNo, appraisalDto.CompanyCode);

        //        if (loan == null)
        //        {
        //            throw new InvalidOperationException($"Loan {appraisalDto.LoanNo} not found");
        //        }

        //        _logger.LogInformation($"Current loan status: {loan.Status}");
        //        _logger.LogInformation($"Recommended amount: {appraisalDto.RecommendedAmount}");
        //        _logger.LogInformation($"Appraisal decision: {appraisalDto.AppraisalDecision}");

        //        if (loan.Status != (int)Status.Submitted)
        //        {
        //            throw new InvalidOperationException($"Loan cannot be appraised. Current status: {loan.Status}. Expected: Submitted");
        //        }

        //        var existingAppraisal = await _context.Appraisal
        //            .FirstOrDefaultAsync(a => a.LoanNo == appraisalDto.LoanNo);

        //        if (existingAppraisal != null)
        //        {
        //            throw new InvalidOperationException("This loan has already been appraised.");
        //        }

        //        // Get member details
        //        var member = await _context.Members
        //            .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == appraisalDto.CompanyCode);

        //        // Get loan type to get interest rate
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == appraisalDto.CompanyCode);

        //        // ============================================================
        //        // GET GUARANTEES AND CHECK SELF-GUARANTEE
        //        // ============================================================
        //        var memberGuarantors = await _context.Loanguar
        //            .Where(g => g.LoanNo == appraisalDto.LoanNo && g.Transfered == false)
        //            .ToListAsync();
        //        var totalMemberGuarantee = memberGuarantors.Sum(g => g.Amount ?? 0);

        //        var collateralGuarantees = await _context.ColloanGuars
        //            .Where(cg => cg.LoanNo == appraisalDto.LoanNo && cg.Balance > 0)
        //            .ToListAsync();
        //        var totalCollateralGuarantee = collateralGuarantees.Sum(cg => cg.Balance);

        //        var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
        //        var isSelfGuarantee = loanType?.SelfGuarantee ?? false;
        //        var isApplicantGuarantor = memberGuarantors.Any(g => g.MemberNo == loan.MemberNo);

        //        // ✅ FIX: Check if loan requires guarantors
        //        var requiresGuarantor = !string.IsNullOrEmpty(loanType.Guarantor) &&
        //                                loanType.Guarantor != "No" &&
        //                                loanType.Guarantor != "N";

        //        // ✅ FIX: For self-guarantee enabled loans, we don't require full guarantee
        //        bool canProceedToAppraisal = false;

        //        if (requiresGuarantor)
        //        {
        //            if (isSelfGuarantee && isApplicantGuarantor)
        //            {
        //                // Self-guarantee is enabled AND applicant is a guarantor
        //                // They can proceed even if not fully guaranteed
        //                canProceedToAppraisal = true;
        //                _logger.LogInformation($"Self-guarantee enabled - Applicant is guarantor. Proceeding with appraisal even with partial guarantee. Total: {totalGuarantee:C}, Loan: {loan.LoanAmt:C}");
        //            }
        //            else if (totalGuarantee >= (loan.LoanAmt ?? 0))
        //            {
        //                // Fully guaranteed by other members or collateral
        //                canProceedToAppraisal = true;
        //                _logger.LogInformation($"Loan fully guaranteed: {totalGuarantee:C} >= {loan.LoanAmt:C}");
        //            }
        //            else if (totalGuarantee > 0)
        //            {
        //                // Has some guarantee but not full - only allow if self-guarantee is enabled
        //                if (isSelfGuarantee)
        //                {
        //                    canProceedToAppraisal = true;
        //                    _logger.LogInformation($"Partial guarantee ({totalGuarantee:C}) allowed due to self-guarantee enabled");
        //                }
        //                else
        //                {
        //                    throw new InvalidOperationException($"Loan is not fully guaranteed. Total guarantee: {totalGuarantee:C}, Loan amount: {loan.LoanAmt:C}. Remaining: {(loan.LoanAmt ?? 0) - totalGuarantee:C}. Please add more guarantees or enable self-guarantee.");
        //                }
        //            }
        //            else
        //            {
        //                throw new InvalidOperationException("Cannot appraise loan. No guarantees found. Please add member guarantors or collateral guarantees.");
        //            }
        //        }
        //        else
        //        {
        //            // Loan doesn't require guarantors - always proceed
        //            canProceedToAppraisal = true;
        //        }

        //        if (!canProceedToAppraisal)
        //        {
        //            throw new InvalidOperationException("Cannot proceed with appraisal. Please ensure guarantees are in place.");
        //        }

        //        // Calculate values
        //        decimal memberShares = member?.ShareCap ?? 0;
        //        decimal monthlyIncome = (decimal)(member?.MonthlyContr ?? 0);
        //        decimal existingLoans = appraisalDto.ExistingLoanObligations;
        //        decimal recommendedAmount = appraisalDto.RecommendedAmount;

        //        // INTEREST RATE - USE AS-IS FROM LOAN TYPE (store as percentage, e.g., 15 for 15%)
        //        decimal interestRatePercent = 0;
        //        if (loanType != null && !string.IsNullOrEmpty(loanType.Interest))
        //        {
        //            string interestStr = loanType.Interest.ToString().Replace("%", "");
        //            if (decimal.TryParse(interestStr, out interestRatePercent))
        //            {
        //                // If value is less than 1 (e.g., 0.15), convert to percentage (15)
        //                if (interestRatePercent < 1 && interestRatePercent > 0)
        //                {
        //                    interestRatePercent = interestRatePercent * 100;
        //                }
        //            }
        //        }

        //        // For calculation, convert to decimal (e.g., 15% = 0.15)
        //        decimal interestRateDecimal = interestRatePercent / 100;
        //        decimal monthlyInterestRate = interestRateDecimal / 12;
        //        int repayPeriod = appraisalDto.RecommendedPeriod;

        //        // Calculate monthly payment (EMI)
        //        decimal monthlyPayment = 0;
        //        decimal totalInterest = 0;

        //        if (monthlyInterestRate > 0 && repayPeriod > 0)
        //        {
        //            decimal factor = (decimal)Math.Pow((double)(1 + monthlyInterestRate), repayPeriod);
        //            monthlyPayment = recommendedAmount * monthlyInterestRate * factor / (factor - 1);
        //            totalInterest = (monthlyPayment * repayPeriod) - recommendedAmount;
        //        }
        //        else
        //        {
        //            monthlyPayment = recommendedAmount / repayPeriod;
        //        }

        //        var appraisal = new Appraisal
        //        {
        //            LoanNo = appraisalDto.LoanNo,
        //            CompanyCode = appraisalDto.CompanyCode,
        //            MemberNo = loan.MemberNo,
        //            AppraisDate = DateTime.Now,
        //            AuditTime = DateTime.Now,
        //            AuditID = appraisalDto.AppraisedBy,
        //            OfficerNames = appraisalDto.AppraisedBy,
        //            TransactionNo = Guid.NewGuid().ToString().Substring(0, 15),
        //            Salary = monthlyIncome,
        //            Allowances = 0,
        //            Shares = memberShares,
        //            Loans = existingLoans,
        //            Deductions = 0,
        //            AmtRecommended = recommendedAmount,
        //            TotalDeductions = 0,
        //            Principal = recommendedAmount,
        //            Interest = interestRatePercent,
        //            TotalInterest = totalInterest,
        //            RepayMethod = loan?.RepayMethod ?? "STL",
        //            RepayRate = monthlyPayment,
        //            Reason = appraisalDto.AppraisalNotes,
        //            TInterest = interestRatePercent,
        //            NetMonthlySalary = monthlyIncome - existingLoans,
        //            SocietyPayment = monthlyPayment,
        //            ExpectedNetSalary = monthlyIncome - existingLoans - monthlyPayment,
        //            DeductionToGross = 0,
        //            TotalDedNewLoanToGross = 0,
        //            NetSalaryToGross = 0,
        //            TotalLoanToGross = 0,
        //            TotalCoopDedToGross = 0,
        //            BankLoan = 0,
        //            Nssf = 0,
        //            CopLoanded = 0,
        //            OtherDed = 0,
        //            StatutoryDed = 0,
        //            StatutoryDedToGross = 0,
        //            TotalDedToGrossLessStatutory = 0,
        //            NoOfLoans = await _context.Loans.CountAsync(l => l.MemberNo == loan.MemberNo && l.CompanyCode == appraisalDto.CompanyCode),
        //            LoanGuarantor = 0
        //        };

        //        _logger.LogInformation($"Created appraisal record with decision: {appraisalDto.AppraisalDecision}");
        //        _logger.LogInformation($"Interest Rate: {interestRatePercent}%");

        //        string oldStatus = loan.Status.ToString();

        //        if (appraisalDto.AppraisalDecision == "Recommend")
        //        {
        //            loan.LoanAmt = recommendedAmount;
        //            loan.Interest = interestRatePercent;
        //            loan.RepayPeriod = repayPeriod;
        //            loan.Status = (int)Status.Approved;
        //            loan.Posted = "APPROVED";
        //            loan.UserName = appraisalDto.AppraisedBy;
        //            loan.AuditDateTime = DateTime.Now;

        //            _logger.LogInformation($"Loan status updated from {oldStatus} to Approved");
        //        }
        //        else if (appraisalDto.AppraisalDecision == "NotRecommend")
        //        {
        //            loan.Status = (int)Status.Rejected;
        //            loan.Posted = "REJECTED";
        //            loan.AddSecurity = $"Rejected at appraisal: {appraisalDto.AppraisalNotes}";
        //            loan.UserName = appraisalDto.AppraisedBy;
        //            loan.AuditDateTime = DateTime.Now;

        //            _logger.LogInformation($"Loan status updated from {oldStatus} to Rejected");
        //        }
        //        else
        //        {
        //            loan.Status = (int)Status.Approved;
        //            loan.Posted = "APPROVED";
        //            loan.UserName = appraisalDto.AppraisedBy;
        //            loan.AuditDateTime = DateTime.Now;
        //        }

        //        _context.Appraisal.Add(appraisal);
        //        _context.Loans.Update(loan);
        //        await _context.SaveChangesAsync();

        //        // Blockchain transaction
        //        var blockchainData = new
        //        {
        //            LoanNo = appraisalDto.LoanNo,
        //            Amt = recommendedAmount,
        //            Rate = interestRatePercent,
        //            Period = repayPeriod,
        //            Decision = appraisalDto.AppraisalDecision,
        //            TotalMemberGuarantee = totalMemberGuarantee,
        //            TotalCollateralGuarantee = totalCollateralGuarantee,
        //            TotalGuarantee = totalGuarantee,
        //            IsSelfGuarantee = isSelfGuarantee,
        //            IsApplicantGuarantor = isApplicantGuarantor,
        //            By = appraisalDto.AppraisedBy,
        //            Date = DateTime.Now
        //        };

        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "LOAN_APPRAISAL",
        //            MemberNo = loan.MemberNo,
        //            CompanyCode = loan.CompanyCode,
        //            Amount = recommendedAmount,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = loan.LoanNo,
        //            Status = "PENDING",
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        appraisal.BlockchainTxId = blockchainTx.TransactionId;
        //        loan.BlockchainTxId = blockchainTx.TransactionId;
        //        await _context.SaveChangesAsync();

        //        _logger.LogInformation($"Blockchain transaction recorded for loan appraisal: {blockchainTx.TransactionId}");

        //        await transaction.CommitAsync();

        //        return appraisal;
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error in AppraiseLoanAsync for loan {appraisalDto.LoanNo}");
        //        throw;
        //    }
        //}

        public async Task<Appraisal?> GetLoanAppraisalAsync(string loanNo)
        {
            return await _context.Appraisal
                .FirstOrDefaultAsync(a => a.LoanNo == loanNo);
        }

        public async Task<Endmain> ApproveLoanAsync(LoanApprovalDTO approvalDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var loan = await GetLoanByNoForDisplayAsync(approvalDto.LoanNo, approvalDto.CompanyCode);

                if (loan == null)
                {
                    throw new InvalidOperationException($"Loan {approvalDto.LoanNo} not found");
                }

                // Store old loan values for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                string oldAddSecurity = loan.AddSecurity ?? "";
                decimal oldLoanAmount = loan.LoanAmt ?? 0;

                var appraisal = await GetLoanAppraisalAsync(approvalDto.LoanNo);

                if (appraisal == null)
                {
                    throw new InvalidOperationException("Loan must be appraised before approval");
                }

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == approvalDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;

                bool isApproved = approvalDto.ApprovalStatus == "Approved";
                bool isRejected = approvalDto.ApprovalStatus == "Rejected";
                bool loanStatusChanged = true;
                Cheque? cheque = null;

                var endmain = new Endmain
                {
                    LoanNo = approvalDto.LoanNo,
                    CompanyCode = approvalDto.CompanyCode,
                    MinuteNo = Guid.NewGuid().ToString().Substring(0, 10),
                    MeetingDate = DateTime.Now,
                    AmtApproved = approvalDto.ApprovedAmount ?? appraisal.AmtRecommended ?? 0,
                    Accepted = approvalDto.ApprovalStatus,
                    ChairSigned = approvalDto.ApprovedBy,
                    SecSigned = approvalDto.ApprovedBy,
                    MembSigned = loan.MemberNo,
                    Reasons = approvalDto.ApprovalComments,
                    Remarks = approvalDto.RejectionReason,
                    AuditId = approvalDto.ApprovedBy,
                    AuditTime = DateTime.Now,
                    TransactionNo = Guid.NewGuid().ToString().Substring(0, 15)
                };

                _context.Endmain.Add(endmain);
                await _context.SaveChangesAsync();

                if (isApproved)
                {
                    loan.LoanAmt = endmain.AmtApproved;
                    loan.Status = (int)Status.Approved;
                    loan.Posted = "Approved";
                    loan.UserName = approvalDto.ApprovedBy;
                    loan.AuditDateTime = DateTime.Now;

                    await _context.SaveChangesAsync();

                    cheque = new Cheque
                    {
                        LoanNo = approvalDto.LoanNo,
                        MemberNo = loan.MemberNo,
                        CompanyCode = approvalDto.CompanyCode,
                        Amount = endmain.AmtApproved,
                        AmountIssued = endmain.AmtApproved,
                        DateIssued = DateTime.Now,
                        Status = "Pending",
                        AuditId = approvalDto.ApprovedBy,
                        AuditTime = DateTime.Now,
                        TransactionNo = Guid.NewGuid().ToString().Substring(0, 15),
                        Voucherno = Guid.NewGuid().ToString().Substring(0, 10),
                        Voucheramount = endmain.AmtApproved,
                        Paymethod = "BANK",
                        Amountinword = endmain.AmtApproved.ToString(),
                        Refloan = true,
                        Dregard = 0,
                        PaidBf = 0,
                        OrgAmt = endmain.AmtApproved,
                        LoanAcc = "LOAN_ASSET_ACCOUNT",
                        ContraAcc = "BANK_ACCOUNT",
                        PremiumAcc = "PREMIUM_ACCOUNT",
                        Offsetamount = 0,
                        IntrOwed = 0
                    };

                    _context.Cheques.Add(cheque);
                    await _context.SaveChangesAsync();

                    // Blockchain transaction for Cheque
                    var blockchainChequeData = new
                    {
                        ChequeId = cheque.Id,
                        LoanNo = approvalDto.LoanNo,
                        Amount = cheque.Amount,
                        DateIssued = cheque.DateIssued,
                        Status = cheque.Status,
                        VoucherNo = cheque.Voucherno,
                        MemberName = memberName,
                        ApprovedBy = approvalDto.ApprovedBy
                    };

                    var blockchainChequeTx = new BlockchainTransaction
                    {
                        TransactionId = Guid.NewGuid().ToString(),
                        TransactionType = "CHEQUE_CREATED",
                        MemberNo = loan.MemberNo,
                        CompanyCode = loan.CompanyCode,
                        Amount = cheque.Amount ?? 0,
                        Timestamp = DateTime.Now,
                        DataHash = await _blockchainService.GenerateTransactionHash(blockchainChequeData),
                        PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainChequeData),
                        OffChainReferenceId = cheque.Voucherno,
                        Status = "PENDING",
                        CreatedAt = DateTime.Now
                    };

                    _context.BlockchainTransactions.Add(blockchainChequeTx);
                    await _context.SaveChangesAsync();

                    cheque.BlockchainTxId = blockchainChequeTx.TransactionId;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Cheque created for loan {approvalDto.LoanNo}: {cheque.Voucherno}");
                }
                else if (isRejected)
                {
                    loan.Status = (int)Status.Rejected;
                    loan.AddSecurity = $"Rejected: {approvalDto.RejectionReason}";
                    loan.UserName = approvalDto.ApprovedBy;
                    loan.AuditDateTime = DateTime.Now;
                    await _context.SaveChangesAsync();
                }

                // Blockchain transaction for Loan Approval
                var blockchainData = new
                {
                    EndmainId = endmain.Id,
                    LoanNo = approvalDto.LoanNo,
                    ApprovalStatus = approvalDto.ApprovalStatus,
                    ApprovedAmount = endmain.AmtApproved,
                    AppraisedAmount = appraisal.AmtRecommended,
                    ApprovalComments = approvalDto.ApprovalComments,
                    RejectionReason = approvalDto.RejectionReason,
                    ApprovedBy = approvalDto.ApprovedBy,
                    ApprovalDate = DateTime.Now,
                    IsFinalApproval = approvalDto.IsFinalApproval,
                    LoanStatusAfter = loan.Status,
                    MemberName = memberName,
                    ChequeCreated = cheque != null,
                    ChequeVoucherNo = cheque?.Voucherno,
                    ChequeAmount = cheque?.Amount
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_APPROVAL",
                    MemberNo = loan.MemberNo,
                    CompanyCode = loan.CompanyCode,
                    Amount = endmain.AmtApproved,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = loan.LoanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                endmain.BlockchainTxId = blockchainTx.TransactionId;
                loan.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Blockchain transaction recorded for loan approval: {blockchainTx.TransactionId}");

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN APPROVAL (ENDMAIN)
                // ============================================================

                // Create audit extra data for Endmain
                var endmainAuditExtraData = new
                {
                    loanNo = approvalDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    applicantName = memberName,
                    approvalStatus = approvalDto.ApprovalStatus,
                    approvedAmount = endmain.AmtApproved,
                    appraisedAmount = appraisal.AmtRecommended,
                    amountDifference = endmain.AmtApproved - (appraisal.AmtRecommended ?? 0),
                    approvalComments = approvalDto.ApprovalComments ?? "",
                    rejectionReason = approvalDto.RejectionReason ?? "",
                    minuteNo = endmain.MinuteNo,
                    meetingDate = endmain.MeetingDate,
                    chairSigned = endmain.ChairSigned,
                    secSigned = endmain.SecSigned,
                    isFinalApproval = approvalDto.IsFinalApproval,
                    approvedBy = approvalDto.ApprovedBy,
                    approvedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanStatusChanged = loanStatusChanged,
                    loanAmountBefore = oldLoanAmount,
                    loanAmountAfter = loan.LoanAmt,
                    amountChanged = oldLoanAmount != loan.LoanAmt,
                    chequeCreated = cheque != null,
                    chequeVoucherNo = cheque?.Voucherno,
                    chequeAmount = cheque?.Amount,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Create a copy of the Endmain object for NewValue
                var endmainForAudit = new
                {
                    endmain.Id,
                    endmain.LoanNo,
                    endmain.MinuteNo,
                    endmain.MeetingDate,
                    endmain.AmtApproved,
                    endmain.Accepted,
                    endmain.ChairSigned,
                    endmain.SecSigned,
                    endmain.MembSigned,
                    endmain.Reasons,
                    endmain.Remarks,
                    ApprovedBy = approvalDto.ApprovedBy,
                    ApprovalDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,  // For Insert, OldValue is null (no previous endmain record)
                    newModel: endmainForAudit,  // This will be serialized to NewValue column
                    tableName: "Endmain",
                    recordId: endmain.Id.ToString(),
                    userId: approvalDto.ApprovedBy,
                    userName: approvalDto.ApprovedBy,
                    companyCode: approvalDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(endmainAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE
                // ============================================================
                var loanAuditExtraData = new
                {
                    loanNo = approvalDto.LoanNo,
                    statusChangedFrom = oldLoanStatus,
                    statusChangedTo = loan.Status,
                    reason = isApproved ? "Loan approved by committee" : "Loan rejected by committee",
                    approvalComments = approvalDto.ApprovalComments ?? "",
                    rejectionReason = approvalDto.RejectionReason ?? "",
                    approvedAmount = endmain.AmtApproved,
                    appraisedAmount = appraisal.AmtRecommended,
                    minuteNo = endmain.MinuteNo,
                    approvedBy = approvalDto.ApprovedBy,
                    approvedDate = DateTime.Now,
                    chequeCreated = cheque != null,
                    chequeVoucherNo = cheque?.Voucherno,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.LoanAmt,
                    loan.Status,
                    loan.Posted,
                    loan.UserName,
                    loan.AuditDateTime,
                    loan.AddSecurity,
                    UpdatedBy = approvalDto.ApprovedBy,
                    UpdateReason = isApproved ? "Loan approved" : "Loan rejected"
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new
                    {
                        Status = oldLoanStatus,
                        Posted = oldLoanPosted,
                        LoanAmt = oldLoanAmount,
                        AddSecurity = oldAddSecurity
                    },
                    newModel: loanForAudit,
                    tableName: "Loans",
                    recordId: approvalDto.LoanNo,
                    userId: approvalDto.ApprovedBy,
                    userName: approvalDto.ApprovedBy,
                    companyCode: approvalDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR CHEQUE (IF CREATED)
                // ============================================================
                if (cheque != null)
                {
                    var chequeAuditExtraData = new
                    {
                        loanNo = approvalDto.LoanNo,
                        applicantMemberNo = loan.MemberNo,
                        applicantName = memberName,
                        chequeId = cheque.Id,
                        voucherNo = cheque.Voucherno,
                        chequeAmount = cheque.Amount,
                        amountInWords = cheque.Amountinword,
                        dateIssued = cheque.DateIssued,
                        status = cheque.Status,
                        paymentMethod = cheque.Paymethod,
                        loanAccount = cheque.LoanAcc,
                        contraAccount = cheque.ContraAcc,
                        premiumAccount = cheque.PremiumAcc,
                        createdBy = approvalDto.ApprovedBy,
                        createdDate = DateTime.Now,
                        blockchainTxId = cheque.BlockchainTxId  
                    };

                    var chequeForAudit = new
                    {
                        cheque.Id,
                        cheque.LoanNo,
                        cheque.MemberNo,
                        cheque.Amount,
                        cheque.AmountIssued,
                        cheque.DateIssued,
                        cheque.Status,
                        cheque.Voucherno,
                        cheque.Voucheramount,
                        cheque.Paymethod,
                        cheque.Amountinword,
                        cheque.LoanAcc,
                        cheque.ContraAcc,
                        cheque.PremiumAcc,
                        CreatedBy = approvalDto.ApprovedBy,
                        CreatedDate = DateTime.Now,
                        BlockchainTxId = cheque.BlockchainTxId  
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: chequeForAudit,
                        tableName: "Cheques",
                        recordId: cheque.Id.ToString(),
                        userId: approvalDto.ApprovedBy,
                        userName: approvalDto.ApprovedBy,
                        companyCode: approvalDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(chequeAuditExtraData),
                        blockchainTxId: cheque.BlockchainTxId  
                    );

                    _logger.LogInformation($"Cheque audit recorded for {cheque.Voucherno}");
                }

                _logger.LogInformation($"Loan approval audit completed for {approvalDto.LoanNo}");

                await transaction.CommitAsync();

                return endmain;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error in ApproveLoanAsync for loan {approvalDto.LoanNo}");
                throw;
            }
        }

        //public async Task<Endmain> ApproveLoanAsync(LoanApprovalDTO approvalDto)
        //{
        //    var loan = await GetLoanByNoForDisplayAsync(approvalDto.LoanNo, approvalDto.CompanyCode);

        //    var appraisal = await GetLoanAppraisalAsync(approvalDto.LoanNo);

        //    if (appraisal == null)
        //    {
        //        throw new InvalidOperationException("Loan must be appraised before approval");
        //    }

        //    var endmain = new Endmain
        //    {
        //        LoanNo = approvalDto.LoanNo,
        //        CompanyCode = approvalDto.CompanyCode,
        //        MinuteNo = Guid.NewGuid().ToString().Substring(0, 10),
        //        MeetingDate = DateTime.Now,
        //        AmtApproved = approvalDto.ApprovedAmount ?? appraisal.AmtRecommended ?? 0,
        //        Accepted = approvalDto.ApprovalStatus,
        //        ChairSigned = approvalDto.ApprovedBy,
        //        SecSigned = approvalDto.ApprovedBy,
        //        MembSigned = loan.MemberNo,
        //        Reasons = approvalDto.ApprovalComments,
        //        Remarks = approvalDto.RejectionReason,
        //        AuditId = approvalDto.ApprovedBy,
        //        AuditTime = DateTime.Now,
        //        TransactionNo = Guid.NewGuid().ToString().Substring(0, 15)
        //    };

        //    _context.Endmain.Add(endmain);
        //    await _context.SaveChangesAsync();

        //    if (approvalDto.ApprovalStatus == "Approved")
        //    {
        //        loan.LoanAmt = endmain.AmtApproved;
        //        loan.Status = (int)Status.Approved;
        //        loan.Posted = "Approved";
        //        loan.UserName = approvalDto.ApprovedBy;
        //        loan.AuditDateTime = DateTime.Now;

        //        await _context.SaveChangesAsync();

        //        var cheque = new Cheque
        //        {
        //            LoanNo = approvalDto.LoanNo,
        //            MemberNo = loan.MemberNo,
        //            CompanyCode = approvalDto.CompanyCode,
        //            Amount = endmain.AmtApproved,
        //            AmountIssued = endmain.AmtApproved,
        //            DateIssued = DateTime.Now,
        //            Status = "Pending",
        //            AuditId = approvalDto.ApprovedBy,
        //            AuditTime = DateTime.Now,
        //            TransactionNo = Guid.NewGuid().ToString().Substring(0, 15),
        //            Voucherno = Guid.NewGuid().ToString().Substring(0, 10),
        //            Voucheramount = endmain.AmtApproved,
        //            Paymethod = "BANK",
        //            Amountinword = endmain.AmtApproved.ToString(),
        //            Refloan = true,
        //            Dregard = 0,
        //            PaidBf = 0,
        //            OrgAmt = endmain.AmtApproved,
        //            LoanAcc = "LOAN_ASSET_ACCOUNT",
        //            ContraAcc = "BANK_ACCOUNT",
        //            PremiumAcc = "PREMIUM_ACCOUNT",
        //            Offsetamount = 0,
        //            IntrOwed = 0
        //        };

        //        _context.Cheques.Add(cheque);
        //        await _context.SaveChangesAsync();

        //        var blockchainChequeData = new
        //        {
        //            ChequeId = cheque.Id,
        //            LoanNo = approvalDto.LoanNo,
        //            Amount = cheque.Amount,
        //            DateIssued = cheque.DateIssued,
        //            Status = cheque.Status,
        //            VoucherNo = cheque.Voucherno
        //        };

        //        var blockchainChequeTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "CHEQUE_CREATED",
        //            MemberNo = loan.MemberNo,
        //            CompanyCode = loan.CompanyCode,
        //            Amount = cheque.Amount ?? 0,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainChequeData),
        //            //DataHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(blockchainChequeData))),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainChequeData),
        //            OffChainReferenceId = cheque.Voucherno,
        //            Status = "PENDING",
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainChequeTx);
        //        await _context.SaveChangesAsync();

        //        cheque.BlockchainTxId = blockchainChequeTx.TransactionId;
        //        await _context.SaveChangesAsync();
        //    }
        //    else if (approvalDto.ApprovalStatus == "Rejected")
        //    {
        //        loan.Status = (int)Status.Rejected;
        //        loan.AddSecurity = $"Rejected: {approvalDto.RejectionReason}";
        //        loan.UserName = approvalDto.ApprovedBy;
        //        loan.AuditDateTime = DateTime.Now;
        //        await _context.SaveChangesAsync();
        //    }

        //    var blockchainData = new
        //    {
        //        EndmainId = endmain.Id,
        //        LoanNo = approvalDto.LoanNo,
        //        ApprovalStatus = approvalDto.ApprovalStatus,
        //        ApprovedAmount = endmain.AmtApproved,
        //        ApprovalComments = approvalDto.ApprovalComments,
        //        ApprovedBy = approvalDto.ApprovedBy,
        //        ApprovalDate = DateTime.Now,
        //        IsFinalApproval = approvalDto.IsFinalApproval,
        //        LoanStatusAfter = loan.Status
        //    };

        //    var blockchainTx = new BlockchainTransaction
        //    {
        //        TransactionId = Guid.NewGuid().ToString(),
        //        TransactionType = "LOAN_APPROVAL",
        //        MemberNo = loan.MemberNo,
        //        CompanyCode = loan.CompanyCode,
        //        Amount = endmain.AmtApproved,
        //        Timestamp = DateTime.Now,
        //        DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //        PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //        OffChainReferenceId = loan.LoanNo,
        //        Status = "PENDING",
        //        CreatedAt = DateTime.Now
        //    };

        //    _context.BlockchainTransactions.Add(blockchainTx);
        //    await _context.SaveChangesAsync();

        //    endmain.BlockchainTxId = blockchainTx.TransactionId;
        //    loan.BlockchainTxId = blockchainTx.TransactionId;
        //    await _context.SaveChangesAsync();

        //    _logger.LogInformation($"Blockchain transaction recorded for loan approval: {blockchainTx.TransactionId}");

        //    return endmain;
        //}

        public async Task<List<Endmain>> GetLoanApprovalsAsync(string loanNo)
        {
            var approvals = await _context.Endmain
                .Where(a => a.LoanNo == loanNo)
                .OrderByDescending(a => a.MeetingDate)
                .ToListAsync();

            return approvals;
        }

        public async Task<bool> IsLoanApprovedAsync(string loanNo)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo);

            return loan != null && loan.Status == (int)Status.Approved;
        }

        #endregion


        #region Loan Endorsement/Deduction 

        public async Task<Endmain> CreateEndorsementAsync(LoanEndorsementDTO endorsementDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Creating endorsement for loan {endorsementDto.LoanNo}");

                var validDeductions = endorsementDto.Deductions
                    .Where(d => d.Amount > 0 && !string.IsNullOrEmpty(d.GlAccountNo))
                    .ToList();

                var totalDeductions = validDeductions.Sum(d => d.Amount);

                // Get loan data with LoanType to get LoanAcc
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == endorsementDto.LoanNo && l.CompanyCode == endorsementDto.CompanyCode);

                if (loan == null)
                {
                    throw new InvalidOperationException($"Loan {endorsementDto.LoanNo} not found");
                }

                // Store old loan values for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                decimal oldLoanAamount = loan.Aamount ?? 0;

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == endorsementDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;

                // Get LoanType to retrieve LoanAcc
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == endorsementDto.CompanyCode);

                if (loan.Status != (int)Status.Approved)
                {
                    throw new InvalidOperationException($"Cannot create endorsement for loan in status '{loan.Status}'. Loan must be Approved.");
                }

                if (string.IsNullOrEmpty(endorsementDto.SourceAccountNo))
                {
                    throw new InvalidOperationException("Please select a Source Bank/Account for disbursement.");
                }

                // Check existing endorsement
                var existingEndmain = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == endorsementDto.LoanNo && e.CompanyCode == endorsementDto.CompanyCode);

                if (existingEndmain != null)
                {
                    throw new InvalidOperationException($"Endorsement already exists for loan {endorsementDto.LoanNo}");
                }

                // Get the approved amount from Appraisal (before deductions)
                var appraisal = await _context.Appraisal
                    .FirstOrDefaultAsync(a => a.LoanNo == endorsementDto.LoanNo);

                // The amount approved from appraisal (could be less than applied amount)
                decimal approvedAmount = appraisal?.AmtRecommended ?? loan.LoanAmt ?? 0;

                // Calculate net amount after deductions
                var netAmount = approvedAmount - totalDeductions;

                if (netAmount < 0)
                {
                    throw new InvalidOperationException($"Total deductions ({totalDeductions:C}) cannot exceed approved amount ({approvedAmount:C})");
                }

                var appraisalReason = appraisal?.Reason ?? "APPROVED";

                var minuteNo = await GenerateMinuteNumberAsync(endorsementDto.CompanyCode);
                var voucherNo = await GenerateVoucherNumberAsync(endorsementDto.CompanyCode);
                var chequeNo = await GenerateChequeNumberAsync(endorsementDto.CompanyCode);
                var transactionNo = DateTime.Now.ToString("ddMMyyyyHHmmss");
                var auditTransactionNo = Guid.NewGuid().ToString().Substring(0, 15);

                // Get PremiumAcc from the view (deduction with DeductionCode "INSURANCE")
                var premiumDeduction = validDeductions.FirstOrDefault(d => d.DeductionCode == "INSURANCE" || d.DeductionName.Contains("Insurance"));
                var premiumAcc = premiumDeduction?.GlAccountNo ?? null;

                // Get LoanAcc from LoanType
                var loanAcc = loanType?.LoanAcc ?? "LOAN_ASSET_ACCOUNT";

                // ============================================================
                // CREATE ENDMAIN RECORD
                // AmtApproved = Approved amount from appraisal (before deductions)
                // ============================================================
                var endmain = new Endmain
                {
                    LoanNo = endorsementDto.LoanNo,
                    CompanyCode = endorsementDto.CompanyCode,
                    MinuteNo = minuteNo,
                    MeetingDate = endorsementDto.EndorsementDate,
                    AmtApproved = approvedAmount,  // FIXED: Amount approved from appraisal (before deductions)
                    Accepted = "1",
                    ChairSigned = null,
                    SecSigned = null,
                    MembSigned = null,
                    Reasons = appraisalReason,
                    Remarks = $"Total deductions: {totalDeductions:C}",
                    AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    TransactionNo = auditTransactionNo
                };

                _context.Endmain.Add(endmain);
                await _context.SaveChangesAsync();

                // ============================================================
                // CREATE CHEQUE RECORD
                // Amount = Approved amount from appraisal (before deductions)
                // AmountIssued = Net amount after deductions (what member actually receives)
                // ============================================================
                var cheque = new Cheque
                {
                    LoanNo = endorsementDto.LoanNo,
                    MemberNo = loan.MemberNo,
                    CompanyCode = endorsementDto.CompanyCode,
                    Amount = approvedAmount,  // FIXED: Approved amount from appraisal (before deductions)
                    AmountIssued = netAmount,  // FIXED: Net amount after deductions (what member gets)
                    ChequeNo = chequeNo,
                    Voucherno = voucherNo,
                    Voucheramount = netAmount,
                    DateIssued = endorsementDto.EndorsementDate,
                    Status = "Pending",
                    AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Remarks = endorsementDto.Remarks ?? "",
                    Firstdate = endorsementDto.EndorsementDate,
                    Balance = netAmount,
                    TransactionNo = transactionNo,
                    OrgAmt = approvedAmount,  // Original approved amount
                    LoanAcc = loanAcc,
                    ContraAcc = endorsementDto.SourceAccountNo,
                    PremiumAcc = premiumAcc ?? "PREMIUM_ACCOUNT",
                    UserName = endorsementDto.EndorsedBy ?? "SYSTEM",
                    AuditDateTime = DateTime.Now,
                    BalForward = 0,
                    ProcessingFee = validDeductions.FirstOrDefault(d => d.DeductionCode == "PROC_FEE")?.Amount ?? 0,
                    IntAmount = 0,
                    IntrOwed = 0,
                    CollectorId = null,
                    CollectorName = null,
                    ClerkStaffNo = null,
                    ClerkName = null,
                    Reasons = appraisalReason,
                    Premium = 0,
                    Offsetamount = 0,
                    Amountinword = NumberToWords(netAmount),
                    Refloan = true,
                    Paymethod = "",
                    Dregard = 0,
                    PaidBf = 0,
                    ApiKey = null,
                    SerialNo = null
                };

                _context.Cheques.Add(cheque);
                await _context.SaveChangesAsync();

                // Store GL Transaction IDs for audit
                var glTransactionIds = new List<int>();

                // ============================================================
                // RECORD GL TRANSACTIONS FOR EACH DEDUCTION
                // Debit: Income Account (where fee goes)
                // Credit: Source Account (bank/cash)
                // ============================================================
                foreach (var deduction in validDeductions)
                {
                    var glTransaction = new Gltransaction
                    {
                        TransDate = DateTime.Now,
                        Amount = deduction.Amount,
                        DrAccNo = deduction.GlAccountNo,  // Income account (DEBIT)
                        CrAccNo = endorsementDto.SourceAccountNo,  // Source account (CREDIT)
                        Temp = "ENDORSEMENT",
                        DocumentNo = voucherNo,
                        Source = "LOAN_ENDORSEMENT",
                        CompanyCode = endorsementDto.CompanyCode,
                        TransDescript = $"{deduction.DeductionName} for Loan {endorsementDto.LoanNo}",
                        AuditTime = DateTime.Now,
                        AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
                        Cash = 0,
                        DocPosted = 1,
                        ChequeNo = chequeNo,
                        Dregard = false,
                        Recon = false,
                        TransactionNo = transactionNo,
                        Module = "LOAN",
                        ReconId = 0,
                        AuditDateTime = DateTime.Now
                    };

                    _context.Gltransactions.Add(glTransaction);
                    glTransactionIds.Add((int)glTransaction.Id);
                }

                // ============================================================
                // RECORD NET DISBURSEMENT GL TRANSACTION
                // Debit: Loan Asset Account
                // Credit: Source Account
                // ============================================================
                var netDisbursementGL = new Gltransaction
                {
                    TransDate = DateTime.Now,
                    Amount = netAmount,
                    DrAccNo = loanAcc,  // Loan Asset Account (DEBIT)
                    CrAccNo = endorsementDto.SourceAccountNo,  // Source account (CREDIT)
                    Temp = "DISBURSEMENT",
                    DocumentNo = voucherNo,
                    Source = "LOAN_DISBURSEMENT",
                    CompanyCode = endorsementDto.CompanyCode,
                    TransDescript = $"Net Loan Disbursement - Loan {endorsementDto.LoanNo}",
                    AuditTime = DateTime.Now,
                    AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
                    Cash = 0,
                    DocPosted = 1,
                    ChequeNo = chequeNo,
                    Dregard = false,
                    Recon = false,
                    TransactionNo = transactionNo,
                    Module = "LOAN",
                    ReconId = 0,
                    AuditDateTime = DateTime.Now
                };

                _context.Gltransactions.Add(netDisbursementGL);
                glTransactionIds.Add((int)netDisbursementGL.Id);
                await _context.SaveChangesAsync();

                // Update Loan status to Endorsed
                loan.Status = (int)Status.Endorsed;
                loan.Posted = "Endorsed";
                loan.UserName = endorsementDto.EndorsedBy ?? "SYSTEM";
                loan.AuditDateTime = DateTime.Now;
                // Store the net amount as the disbursable amount
                loan.Aamount = netAmount;

                _context.Loans.Update(loan);
                await _context.SaveChangesAsync();

                string? blockchainTxId = null;

                // Record blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        EndmainId = endmain.Id,
                        LoanNo = endorsementDto.LoanNo,
                        MemberNo = loan.MemberNo,
                        MemberName = memberName,
                        ApprovedAmount = approvedAmount,
                        TotalDeductions = totalDeductions,
                        NetAmount = netAmount,
                        VoucherNo = voucherNo,
                        ChequeNo = chequeNo,
                        SourceAccount = endorsementDto.SourceAccountNo,
                        LoanAcc = loanAcc,
                        PremiumAcc = premiumAcc,
                        Deductions = validDeductions.Select(d => new
                        {
                            d.DeductionCode,
                            d.DeductionName,
                            d.Amount,
                            d.GlAccountNo,
                            d.GlAccountName
                        }).ToList(),
                        EndorsedBy = endorsementDto.EndorsedBy,
                        EndorsementDate = endorsementDto.EndorsementDate,
                        Remarks = endorsementDto.Remarks,
                        LoanStatusAfter = (int)Status.Endorsed
                    };

                    var blockchainTx = new BlockchainTransaction
                    {
                        TransactionId = Guid.NewGuid().ToString(),
                        TransactionType = "LOAN_ENDORSEMENT",
                        MemberNo = loan.MemberNo,
                        CompanyCode = loan.CompanyCode,
                        Amount = netAmount,
                        Timestamp = DateTime.Now,
                        DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                        PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                        OffChainReferenceId = endmain.MinuteNo,
                        Status = "PENDING",
                        CreatedAt = DateTime.Now
                    };

                    _context.BlockchainTransactions.Add(blockchainTx);
                    await _context.SaveChangesAsync();

                    blockchainTxId = blockchainTx.TransactionId;

                    endmain.BlockchainTxId = blockchainTx.TransactionId;
                    cheque.BlockchainTxId = blockchainTx.TransactionId;
                    loan.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record blockchain transaction for endorsement");
                }

                // ============================================================
                // SAVE AUDIT TRAIL FOR ENDORSEMENT (ENDMAIN)
                // ============================================================

                var endmainAuditExtraData = new
                {
                    loanNo = endorsementDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    applicantName = memberName,
                    minuteNo = minuteNo,
                    meetingDate = endorsementDto.EndorsementDate,
                    approvedAmount = approvedAmount,
                    totalDeductions = totalDeductions,
                    netAmount = netAmount,
                    voucherNo = voucherNo,
                    chequeNo = chequeNo,
                    sourceAccountNo = endorsementDto.SourceAccountNo,
                    loanAccount = loanAcc,
                    premiumAccount = premiumAcc,
                    deductions = validDeductions.Select(d => new
                    {
                        d.DeductionCode,
                        d.DeductionName,
                        d.Amount,
                        d.GlAccountNo,
                        d.GlAccountName
                    }).ToList(),
                    appraisalReason = appraisalReason,
                    remarks = endorsementDto.Remarks ?? "",
                    endorsedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    endorsedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanAmountBefore = oldLoanAamount,
                    loanAmountAfter = loan.Aamount,
                    blockchainTxId = blockchainTxId
                };

                var endmainForAudit = new
                {
                    endmain.Id,
                    endmain.LoanNo,
                    endmain.MinuteNo,
                    endmain.MeetingDate,
                    endmain.AmtApproved,
                    endmain.Accepted,
                    endmain.Reasons,
                    endmain.Remarks,
                    endmain.TransactionNo,
                    VoucherNo = voucherNo,
                    ChequeNo = chequeNo,
                    NetAmount = netAmount,
                    TotalDeductions = totalDeductions,
                    EndorsedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    EndorsementDate = endorsementDto.EndorsementDate,
                    BlockchainTxId = blockchainTxId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: endmainForAudit,
                    tableName: "Endmain",
                    recordId: endmain.Id.ToString(),
                    userId: endorsementDto.EndorsedBy ?? "SYSTEM",
                    userName: endorsementDto.EndorsedBy ?? "SYSTEM",
                    companyCode: endorsementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(endmainAuditExtraData),
                    blockchainTxId: blockchainTxId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR CHEQUE
                // ============================================================
                var chequeAuditExtraData = new
                {
                    loanNo = endorsementDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    applicantName = memberName,
                    chequeId = cheque.Id,
                    chequeNo = chequeNo,
                    voucherNo = voucherNo,
                    amount = cheque.Amount,
                    amountIssued = cheque.AmountIssued,
                    netAmount = netAmount,
                    totalDeductions = totalDeductions,
                    dateIssued = cheque.DateIssued,
                    status = cheque.Status,
                    sourceAccount = endorsementDto.SourceAccountNo,
                    loanAccount = loanAcc,
                    premiumAccount = premiumAcc,
                    processingFee = cheque.ProcessingFee,
                    amountInWords = cheque.Amountinword,
                    createdBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    createdDate = DateTime.Now,
                    blockchainTxId = blockchainTxId
                };

                var chequeForAudit = new
                {
                    cheque.Id,
                    cheque.LoanNo,
                    cheque.MemberNo,
                    cheque.Amount,
                    cheque.AmountIssued,
                    cheque.ChequeNo,
                    cheque.Voucherno,
                    cheque.Voucheramount,
                    cheque.DateIssued,
                    cheque.Status,
                    cheque.LoanAcc,
                    cheque.ContraAcc,
                    cheque.PremiumAcc,
                    cheque.ProcessingFee,
                    cheque.Remarks,
                    cheque.Amountinword,
                    CreatedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    CreatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTxId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: chequeForAudit,
                    tableName: "Cheques",
                    recordId: cheque.Id.ToString(),
                    userId: endorsementDto.EndorsedBy ?? "SYSTEM",
                    userName: endorsementDto.EndorsedBy ?? "SYSTEM",
                    companyCode: endorsementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(chequeAuditExtraData),
                    blockchainTxId: blockchainTxId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE
                // ============================================================
                var loanAuditExtraData = new
                {
                    loanNo = endorsementDto.LoanNo,
                    statusChangedFrom = oldLoanStatus,
                    statusChangedTo = loan.Status,
                    reason = "Loan endorsed and ready for disbursement",
                    approvedAmount = approvedAmount,
                    totalDeductions = totalDeductions,
                    netDisbursedAmount = netAmount,
                    voucherNo = voucherNo,
                    chequeNo = chequeNo,
                    sourceAccount = endorsementDto.SourceAccountNo,
                    minuteNo = minuteNo,
                    endorsedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    endorsedDate = DateTime.Now,
                    blockchainTxId = blockchainTxId
                };

                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.LoanAmt,
                    loan.Aamount,
                    loan.Status,
                    loan.Posted,
                    loan.UserName,
                    loan.AuditDateTime,
                    UpdatedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                    UpdateReason = "Loan endorsed - ready for disbursement",
                    NetDisbursedAmount = netAmount
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new
                    {
                        Status = oldLoanStatus,
                        Posted = oldLoanPosted,
                        Aamount = oldLoanAamount
                    },
                    newModel: loanForAudit,
                    tableName: "Loans",
                    recordId: endorsementDto.LoanNo,
                    userId: endorsementDto.EndorsedBy ?? "SYSTEM",
                    userName: endorsementDto.EndorsedBy ?? "SYSTEM",
                    companyCode: endorsementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                    blockchainTxId: blockchainTxId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR GL TRANSACTIONS
                // ============================================================
                if (glTransactionIds.Any())
                {
                    var glAuditExtraData = new
                    {
                        loanNo = endorsementDto.LoanNo,
                        voucherNo = voucherNo,
                        chequeNo = chequeNo,
                        transactionNo = transactionNo,
                        totalTransactions = glTransactionIds.Count,
                        deductionTransactions = validDeductions.Count,
                        netDisbursementTransaction = 1,
                        glTransactionIds = glTransactionIds,
                        createdBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                        createdDate = DateTime.Now,
                        blockchainTxId = blockchainTxId
                    };

                    var glForAudit = new
                    {
                        LoanNo = endorsementDto.LoanNo,
                        VoucherNo = voucherNo,
                        ChequeNo = chequeNo,
                        TransactionNo = transactionNo,
                        Deductions = validDeductions.Select(d => new
                        {
                            d.DeductionCode,
                            d.DeductionName,
                            d.Amount,
                            d.GlAccountNo
                        }).ToList(),
                        NetDisbursement = new
                        {
                            Amount = netAmount,
                            DrAccount = loanAcc,
                            CrAccount = endorsementDto.SourceAccountNo
                        },
                        CreatedBy = endorsementDto.EndorsedBy ?? "SYSTEM",
                        CreatedDate = DateTime.Now,
                        BlockchainTxId = blockchainTxId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: glForAudit,
                        tableName: "Gltransactions",
                        recordId: voucherNo,
                        userId: endorsementDto.EndorsedBy ?? "SYSTEM",
                        userName: endorsementDto.EndorsedBy ?? "SYSTEM",
                        companyCode: endorsementDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(glAuditExtraData),
                        blockchainTxId: blockchainTxId
                    );
                }

                _logger.LogInformation($"Endorsement audit completed for loan {endorsementDto.LoanNo}");

                await transaction.CommitAsync();

                _logger.LogInformation($"Endorsement created successfully for loan {endorsementDto.LoanNo}. " +
                    $"Approved Amount: {approvedAmount:C}, Total Deductions: {totalDeductions:C}, Net Amount: {netAmount:C}");

                return endmain;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error creating endorsement for loan {endorsementDto.LoanNo}");
                throw;
            }
        }

        //public async Task<Endmain> CreateEndorsementAsync(LoanEndorsementDTO endorsementDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Creating endorsement for loan {endorsementDto.LoanNo}");

        //        var validDeductions = endorsementDto.Deductions
        //            .Where(d => d.Amount > 0 && !string.IsNullOrEmpty(d.GlAccountNo))
        //            .ToList();

        //        var totalDeductions = validDeductions.Sum(d => d.Amount);

        //        // Get loan data with LoanType to get LoanAcc
        //        var loan = await _context.Loans
        //            .FirstOrDefaultAsync(l => l.LoanNo == endorsementDto.LoanNo && l.CompanyCode == endorsementDto.CompanyCode);

        //        if (loan == null)
        //        {
        //            throw new InvalidOperationException($"Loan {endorsementDto.LoanNo} not found");
        //        }

        //        // Get LoanType to retrieve LoanAcc
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == endorsementDto.CompanyCode);

        //        if (loan.Status != (int)Status.Approved)
        //        {
        //            throw new InvalidOperationException($"Cannot create endorsement for loan in status '{loan.Status}'. Loan must be Approved.");
        //        }

        //        if (string.IsNullOrEmpty(endorsementDto.SourceAccountNo))
        //        {
        //            throw new InvalidOperationException("Please select a Source Bank/Account for disbursement.");
        //        }

        //        // Check existing endorsement
        //        var existingEndmain = await _context.Endmain
        //            .FirstOrDefaultAsync(e => e.LoanNo == endorsementDto.LoanNo && e.CompanyCode == endorsementDto.CompanyCode);

        //        if (existingEndmain != null)
        //        {
        //            throw new InvalidOperationException($"Endorsement already exists for loan {endorsementDto.LoanNo}");
        //        }

        //        // Get the approved amount from Appraisal (before deductions)
        //        var appraisal = await _context.Appraisal
        //            .FirstOrDefaultAsync(a => a.LoanNo == endorsementDto.LoanNo);

        //        // The amount approved from appraisal (could be less than applied amount)
        //        decimal approvedAmount = appraisal?.AmtRecommended ?? loan.LoanAmt ?? 0;

        //        // Calculate net amount after deductions
        //        var netAmount = approvedAmount - totalDeductions;

        //        if (netAmount < 0)
        //        {
        //            throw new InvalidOperationException($"Total deductions ({totalDeductions:C}) cannot exceed approved amount ({approvedAmount:C})");
        //        }

        //        var appraisalReason = appraisal?.Reason ?? "APPROVED";

        //        var minuteNo = await GenerateMinuteNumberAsync(endorsementDto.CompanyCode);
        //        var voucherNo = await GenerateVoucherNumberAsync(endorsementDto.CompanyCode);
        //        var chequeNo = await GenerateChequeNumberAsync(endorsementDto.CompanyCode);
        //        var transactionNo = DateTime.Now.ToString("ddMMyyyyHHmmss");
        //        var auditTransactionNo = Guid.NewGuid().ToString().Substring(0, 15);

        //        // Get PremiumAcc from the view (deduction with DeductionCode "INSURANCE")
        //        var premiumDeduction = validDeductions.FirstOrDefault(d => d.DeductionCode == "INSURANCE" || d.DeductionName.Contains("Insurance"));
        //        var premiumAcc = premiumDeduction?.GlAccountNo ?? null;

        //        // Get LoanAcc from LoanType
        //        var loanAcc = loanType?.LoanAcc ?? "LOAN_ASSET_ACCOUNT";

        //        // ============================================================
        //        // CREATE ENDMAIN RECORD
        //        // AmtApproved = Approved amount from appraisal (before deductions)
        //        // ============================================================
        //        var endmain = new Endmain
        //        {
        //            LoanNo = endorsementDto.LoanNo,
        //            CompanyCode = endorsementDto.CompanyCode,
        //            MinuteNo = minuteNo,
        //            MeetingDate = endorsementDto.EndorsementDate,
        //            AmtApproved = approvedAmount,  // FIXED: Amount approved from appraisal (before deductions)
        //            Accepted = "1",
        //            ChairSigned = null,
        //            SecSigned = null,
        //            MembSigned = null,
        //            Reasons = appraisalReason,
        //            Remarks = $"Total deductions: {totalDeductions:C}",
        //            AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
        //            AuditTime = DateTime.Now,
        //            TransactionNo = auditTransactionNo
        //        };

        //        _context.Endmain.Add(endmain);
        //        await _context.SaveChangesAsync();

        //        // ============================================================
        //        // CREATE CHEQUE RECORD
        //        // Amount = Approved amount from appraisal (before deductions)
        //        // AmountIssued = Net amount after deductions (what member actually receives)
        //        // ============================================================
        //        var cheque = new Cheque
        //        {
        //            LoanNo = endorsementDto.LoanNo,
        //            MemberNo = loan.MemberNo,
        //            CompanyCode = endorsementDto.CompanyCode,
        //            Amount = approvedAmount,  // FIXED: Approved amount from appraisal (before deductions)
        //            AmountIssued = netAmount,  // FIXED: Net amount after deductions (what member gets)
        //            ChequeNo = chequeNo,
        //            Voucherno = voucherNo,
        //            Voucheramount = netAmount,
        //            DateIssued = endorsementDto.EndorsementDate,
        //            Status = "Pending",
        //            AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
        //            AuditTime = DateTime.Now,
        //            Remarks = endorsementDto.Remarks ?? "",
        //            Firstdate = endorsementDto.EndorsementDate,
        //            Balance = netAmount,
        //            TransactionNo = transactionNo,
        //            OrgAmt = approvedAmount,  // Original approved amount
        //            LoanAcc = loanAcc,
        //            ContraAcc = endorsementDto.SourceAccountNo,
        //            PremiumAcc = premiumAcc ?? "PREMIUM_ACCOUNT",
        //            UserName = endorsementDto.EndorsedBy ?? "SYSTEM",
        //            AuditDateTime = DateTime.Now,
        //            BalForward = 0,
        //            ProcessingFee = validDeductions.FirstOrDefault(d => d.DeductionCode == "PROC_FEE")?.Amount ?? 0,
        //            IntAmount = 0,
        //            IntrOwed = 0,
        //            CollectorId = null,
        //            CollectorName = null,
        //            ClerkStaffNo = null,
        //            ClerkName = null,
        //            Reasons = appraisalReason,
        //            Premium = 0,
        //            Offsetamount = 0,
        //            Amountinword = NumberToWords(netAmount),
        //            Refloan = true,
        //            Paymethod = "",
        //            Dregard = 0,
        //            PaidBf = 0,
        //            ApiKey = null,
        //            SerialNo = null
        //        };

        //        _context.Cheques.Add(cheque);
        //        await _context.SaveChangesAsync();

        //        // ============================================================
        //        // RECORD GL TRANSACTIONS FOR EACH DEDUCTION
        //        // Debit: Income Account (where fee goes)
        //        // Credit: Source Account (bank/cash)
        //        // ============================================================
        //        foreach (var deduction in validDeductions)
        //        {
        //            var glTransaction = new Gltransaction
        //            {
        //                TransDate = DateTime.Now,
        //                Amount = deduction.Amount,
        //                DrAccNo = deduction.GlAccountNo,  // Income account (DEBIT)
        //                CrAccNo = endorsementDto.SourceAccountNo,  // Source account (CREDIT)
        //                Temp = "ENDORSEMENT",
        //                DocumentNo = voucherNo,
        //                Source = "LOAN_ENDORSEMENT",
        //                CompanyCode = endorsementDto.CompanyCode,
        //                TransDescript = $"{deduction.DeductionName} for Loan {endorsementDto.LoanNo}",
        //                AuditTime = DateTime.Now,
        //                AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
        //                Cash = 0,
        //                DocPosted = 1,
        //                ChequeNo = chequeNo,
        //                Dregard = false,
        //                Recon = false,
        //                TransactionNo = transactionNo,
        //                Module = "LOAN",
        //                ReconId = 0,
        //                AuditDateTime = DateTime.Now
        //            };

        //            _context.Gltransactions.Add(glTransaction);
        //        }

        //        // ============================================================
        //        // RECORD NET DISBURSEMENT GL TRANSACTION
        //        // Debit: Loan Asset Account
        //        // Credit: Source Account
        //        // ============================================================
        //        var netDisbursementGL = new Gltransaction
        //        {
        //            TransDate = DateTime.Now,
        //            Amount = netAmount,
        //            DrAccNo = loanAcc,  // Loan Asset Account (DEBIT)
        //            CrAccNo = endorsementDto.SourceAccountNo,  // Source account (CREDIT)
        //            Temp = "DISBURSEMENT",
        //            DocumentNo = voucherNo,
        //            Source = "LOAN_DISBURSEMENT",
        //            CompanyCode = endorsementDto.CompanyCode,
        //            TransDescript = $"Net Loan Disbursement - Loan {endorsementDto.LoanNo}",
        //            AuditTime = DateTime.Now,
        //            AuditId = endorsementDto.EndorsedBy ?? "SYSTEM",
        //            Cash = 0,
        //            DocPosted = 1,
        //            ChequeNo = chequeNo,
        //            Dregard = false,
        //            Recon = false,
        //            TransactionNo = transactionNo,
        //            Module = "LOAN",
        //            ReconId = 0,
        //            AuditDateTime = DateTime.Now
        //        };

        //        _context.Gltransactions.Add(netDisbursementGL);
        //        await _context.SaveChangesAsync();

        //        // Update Loan status to Endorsed
        //        loan.Status = (int)Status.Endorsed;
        //        loan.Posted = "Endorsed";
        //        loan.UserName = endorsementDto.EndorsedBy ?? "SYSTEM";
        //        loan.AuditDateTime = DateTime.Now;
        //        // Store the net amount as the disbursable amount
        //        loan.Aamount = netAmount;

        //        _context.Loans.Update(loan);
        //        await _context.SaveChangesAsync();

        //        // Record blockchain transaction
        //        try
        //        {
        //            var blockchainData = new
        //            {
        //                EndmainId = endmain.Id,
        //                LoanNo = endorsementDto.LoanNo,
        //                MemberNo = loan.MemberNo,
        //                ApprovedAmount = approvedAmount,
        //                TotalDeductions = totalDeductions,
        //                NetAmount = netAmount,
        //                VoucherNo = voucherNo,
        //                ChequeNo = chequeNo,
        //                SourceAccount = endorsementDto.SourceAccountNo,
        //                LoanAcc = loanAcc,
        //                PremiumAcc = premiumAcc,
        //                Deductions = validDeductions.Select(d => new
        //                {
        //                    d.DeductionCode,
        //                    d.DeductionName,
        //                    d.Amount,
        //                    d.GlAccountNo,
        //                    d.GlAccountName
        //                }).ToList(),
        //                EndorsedBy = endorsementDto.EndorsedBy,
        //                EndorsementDate = endorsementDto.EndorsementDate,
        //                Remarks = endorsementDto.Remarks,
        //                LoanStatusAfter = (int)Status.Endorsed
        //            };

        //            var blockchainTx = new BlockchainTransaction
        //            {
        //                TransactionId = Guid.NewGuid().ToString(),
        //                TransactionType = "LOAN_ENDORSEMENT",
        //                MemberNo = loan.MemberNo,
        //                CompanyCode = loan.CompanyCode,
        //                Amount = netAmount,
        //                Timestamp = DateTime.Now,
        //                DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //                PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //                OffChainReferenceId = endmain.MinuteNo,
        //                Status = "PENDING",
        //                CreatedAt = DateTime.Now
        //            };

        //            _context.BlockchainTransactions.Add(blockchainTx);
        //            await _context.SaveChangesAsync();

        //            endmain.BlockchainTxId = blockchainTx.TransactionId;
        //            cheque.BlockchainTxId = blockchainTx.TransactionId;
        //            loan.BlockchainTxId = blockchainTx.TransactionId;
        //            await _context.SaveChangesAsync();
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogWarning(ex, "Failed to record blockchain transaction for endorsement");
        //        }

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"Endorsement created successfully for loan {endorsementDto.LoanNo}. " +
        //            $"Approved Amount: {approvedAmount:C}, Total Deductions: {totalDeductions:C}, Net Amount: {netAmount:C}");

        //        return endmain;
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error creating endorsement for loan {endorsementDto.LoanNo}");
        //        throw;
        //    }
        //}
        private async Task<string> GenerateChequeNumberAsync(string companyCode)
        {
            var prefix = "CHQ";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            try
            {
                var lastCheque = await _context.Cheques
                    .Where(c => c.CompanyCode == companyCode && c.ChequeNo != null && c.ChequeNo.StartsWith($"{prefix}{date}"))
                    .OrderByDescending(c => c.ChequeNo)
                    .Select(c => c.ChequeNo)
                    .FirstOrDefaultAsync();

                if (lastCheque != null && lastCheque.Length > 11)
                {
                    var sequenceStr = lastCheque.Substring(11);
                    if (int.TryParse(sequenceStr, out int lastSeq))
                    {
                        sequence = lastSeq + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating cheque number, using default sequence");
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        private async Task<string> GenerateVoucherNumberAsync(string companyCode)
        {
            var prefix = "VNO";
            var date = DateTime.Now.ToString("ddMMyyyy");
            var sequence = 1;

            try
            {
                var lastVoucher = await _context.Cheques
                    .Where(c => c.CompanyCode == companyCode && c.Voucherno != null && c.Voucherno.StartsWith($"{prefix}{date}"))
                    .OrderByDescending(c => c.Voucherno)
                    .Select(c => c.Voucherno)
                    .FirstOrDefaultAsync();

                if (lastVoucher != null && lastVoucher.Length > 11)
                {
                    var sequenceStr = lastVoucher.Substring(11);
                    if (int.TryParse(sequenceStr, out int lastSeq))
                    {
                        sequence = lastSeq + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating voucher number, using default sequence");
            }

            return $"{prefix}{date}{sequence:D3}";
        }

        private async Task<string> GenerateMinuteNumberAsync(string companyCode)
        {
            var prefix = "MIN";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            try
            {
                var lastMinute = await _context.Endmain
                    .Where(e => e.CompanyCode == companyCode && e.MinuteNo != null && e.MinuteNo.StartsWith($"{prefix}{date}"))
                    .OrderByDescending(e => e.MinuteNo)
                    .Select(e => e.MinuteNo)
                    .FirstOrDefaultAsync();

                if (lastMinute != null && lastMinute.Length > 11)
                {
                    var sequenceStr = lastMinute.Substring(11);
                    if (int.TryParse(sequenceStr, out int lastSeq))
                    {
                        sequence = lastSeq + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating minute number, using default sequence");
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        private string NumberToWords(decimal number)
        {
            if (number == 0)
                return "ZERO";

            var integerPart = (int)Math.Floor(number);
            var fractionPart = (int)((number - integerPart) * 100);

            var words = ConvertIntegerToWords(integerPart);
            words += " SHILLINGS";

            if (fractionPart > 0)
            {
                words += $" AND {ConvertIntegerToWords(fractionPart)} CENTS";
            }

            return words.ToUpper();
        }

        private string ConvertIntegerToWords(int number)
        {
            if (number == 0)
                return "ZERO";

            var units = new[] { "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN" };
            var tens = new[] { "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY" };

            if (number < 20)
                return units[number];

            if (number < 100)
                return tens[number / 10] + (number % 10 > 0 ? " " + units[number % 10] : "");

            if (number < 1000)
                return units[number / 100] + " HUNDRED" + (number % 100 > 0 ? " " + ConvertIntegerToWords(number % 100) : "");

            if (number < 1000000)
                return ConvertIntegerToWords(number / 1000) + " THOUSAND" + (number % 1000 > 0 ? " " + ConvertIntegerToWords(number % 1000) : "");

            return ConvertIntegerToWords(number / 1000000) + " MILLION" + (number % 1000000 > 0 ? " " + ConvertIntegerToWords(number % 1000000) : "");
        }

        public async Task<Endmain> GetEndorsementByLoanNoAsync(string loanNo, string companyCode)
        {
            return await _context.Endmain
                .Where(e => e.LoanNo == loanNo && e.CompanyCode == companyCode)
                .Select(e => new Endmain
                {
                    Id = e.Id,
                    LoanNo = e.LoanNo,
                    CompanyCode = e.CompanyCode,
                    MinuteNo = e.MinuteNo,
                    MeetingDate = e.MeetingDate,
                    AmtApproved = e.AmtApproved,
                    Accepted = e.Accepted,
                    ChairSigned = e.ChairSigned,
                    SecSigned = e.SecSigned,
                    MembSigned = e.MembSigned,
                    Reasons = e.Reasons,
                    Remarks = e.Remarks,
                    AuditId = e.AuditId,
                    AuditTime = e.AuditTime,
                    TransactionNo = e.TransactionNo,
                    BlockchainTxId = e.BlockchainTxId
                })
                .FirstOrDefaultAsync();
        }
        public async Task<Endmain> GetEndorsementByMinuteNoAsync(string minuteNo, string companyCode)
        {
            return await _context.Endmain
                .FirstOrDefaultAsync(e => e.MinuteNo == minuteNo && e.CompanyCode == companyCode);
        }

        public async Task<List<Endmain>> GetEndorsementsByLoanNoAsync(string loanNo, string companyCode)
        {
            return await _context.Endmain
                .Where(e => e.LoanNo == loanNo && e.CompanyCode == companyCode)
                .OrderByDescending(e => e.MeetingDate)
                .ToListAsync();
        }

        public async Task<List<LoanDeductionDTO>> GetAvailableDeductionsAsync(string companyCode)
        {
            return new List<LoanDeductionDTO>
    {
        new LoanDeductionDTO
        {
            DeductionCode = "PROC_FEE",
            DeductionName = "Processing Fee",
            GlAccountNo = "",
            GlAccountName = "",
            IsMandatory = false,
            Description = "Loan processing fee (enter amount)",
            IsPercentage = false,
            PercentageValue = null,
            Amount = 0
        },
        new LoanDeductionDTO
        {
            DeductionCode = "INSURANCE",
            DeductionName = "Insurance Premium",
            GlAccountNo = "",
            GlAccountName = "",
            IsMandatory = false,
            Description = "Loan insurance premium (enter amount)",
            IsPercentage = false,
            PercentageValue = null,
            Amount = 0
        },
        new LoanDeductionDTO
        {
            DeductionCode = "LEGAL",
            DeductionName = "Legal Fees",
            GlAccountNo = "",
            GlAccountName = "",
            IsMandatory = false,
            Description = "Legal fees (enter amount)",
            IsPercentage = false,
            PercentageValue = null,
            Amount = 0
        },
        new LoanDeductionDTO
        {
            DeductionCode = "OTHER",
            DeductionName = "Other Charges",
            GlAccountNo = "",
            GlAccountName = "",
            IsMandatory = false,
            Description = "Other miscellaneous charges (enter amount)",
            IsPercentage = false,
            PercentageValue = null,
            Amount = 0
        }
        };
        }

        public async Task<decimal> CalculateTotalDeductionsAsync(string loanNo, List<LoanDeductionDTO> deductions)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo);

            if (loan == null)
            {
                throw new InvalidOperationException($"Loan {loanNo} not found");
            }

            var loanAmount = loan.LoanAmt ?? 0;
            var total = 0m;

            foreach (var deduction in deductions)
            {
                if (deduction.IsPercentage && deduction.PercentageValue.HasValue)
                {
                    deduction.Amount = loanAmount * (deduction.PercentageValue.Value / 100);
                    total += deduction.Amount;
                }
                else
                {
                    total += deduction.Amount;
                }
            }

            return total;
        }

        public async Task<bool> HasEndorsementAsync(string loanNo, string companyCode)
        {
            try
            {
                return await _context.Endmain
                    .AnyAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking endorsement for loan {loanNo}");
                return false;
            }
        }

        #endregion



        #region Disbursement

        public async Task<Cheque> DisburseLoanAsync(LoanDisbursementDTO disbursementDto)
        {
            var currentUserRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

            if (currentUserRole != "Finance Officer" && currentUserRole != "Super Admin" && currentUserRole != "Admin")
            {
                throw new UnauthorizedAccessException("Only Finance Officers can disburse loans");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Disbursing loan {disbursementDto.LoanNo}");

                // Get loan details
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == disbursementDto.LoanNo && l.CompanyCode == disbursementDto.CompanyCode);

                if (loan == null)
                {
                    throw new InvalidOperationException($"Loan {disbursementDto.LoanNo} not found");
                }

                // Store old loan values for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                decimal oldLoanAmt = loan.LoanAmt ?? 0;
                decimal oldAamount = loan.Aamount ?? 0;

                _logger.LogInformation($"Loan found: Status={loan.Status}, LoanAmt={loan.LoanAmt}");

                if (loan.Status != (int)Status.Endorsed)
                {
                    throw new InvalidOperationException($"Loan cannot be disbursed in status '{loan.Status}'. Loan must be Endorsed.");
                }

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == disbursementDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;

                // Get endorsement record
                var endmain = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == disbursementDto.LoanNo && e.CompanyCode == disbursementDto.CompanyCode);

                if (endmain == null)
                {
                    throw new InvalidOperationException($"Endorsement not found for loan {disbursementDto.LoanNo}");
                }

                // Get existing cheque from endorsement
                var existingCheque = await _context.Cheques
                    .FirstOrDefaultAsync(c => c.LoanNo == disbursementDto.LoanNo && c.CompanyCode == disbursementDto.CompanyCode);

                if (existingCheque == null)
                {
                    throw new InvalidOperationException($"Cheque record not found for loan {disbursementDto.LoanNo}. Please ensure endorsement was completed.");
                }

                // Store old cheque values for audit
                string oldChequeStatus = existingCheque.Status ?? "";
                decimal? oldChequeAmountIssued = existingCheque.AmountIssued;
                decimal? oldChequeBalance = existingCheque.Balance;

                // Get LoanType for repayment method and grace period
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == disbursementDto.CompanyCode);

                // IMPORTANT: Balance = Approved Amount (from Endmain.AmtApproved)
                decimal approvedAmount = endmain.AmtApproved;  // This is the amount BEFORE deductions
                decimal netDisbursedAmount = existingCheque.AmountIssued ?? approvedAmount;  // Amount AFTER deductions (what member gets)

                _logger.LogInformation($"Approved Amount: {approvedAmount:C}, Net Disbursed: {netDisbursedAmount:C}");

                // Check if already disbursed
                var existingLoanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == disbursementDto.LoanNo && lb.Companycode == disbursementDto.CompanyCode);

                if (existingLoanbal != null)
                {
                    throw new InvalidOperationException($"Loan already disbursed. Loan balance record exists.");
                }

                // ============================================================
                // CALCULATE TOTAL INTEREST FOR THE LOAN PERIOD
                decimal annualInterestRate = loan.Interest ?? 0;
                decimal monthlyInterestRate = (annualInterestRate / 100) / 12;
                int repaymentPeriod = loan.RepayPeriod ?? 12;
                string repayMethod = loan.RepayMethod ?? loanType?.Repaymethod ?? "AMT";

                decimal totalInterest = 0;
                decimal monthlyPayment = 0;
                decimal totalRepayable = 0;

                if (repayMethod == "AMT")
                {
                    // ✅ EMI
                    if (monthlyInterestRate > 0)
                    {
                        decimal factor = (decimal)Math.Pow((double)(1 + monthlyInterestRate), repaymentPeriod);
                        monthlyPayment = approvedAmount * monthlyInterestRate * factor / (factor - 1);
                        totalInterest = (monthlyPayment * repaymentPeriod) - approvedAmount;
                    }
                    else
                    {
                        monthlyPayment = approvedAmount / repaymentPeriod;
                        totalInterest = 0;
                    }

                    totalRepayable = approvedAmount + totalInterest;
                }
                else if (repayMethod == "STL")
                {
                    // ✅ FLAT RATE (Straight Line)
                    totalInterest = approvedAmount * (annualInterestRate / 100) * (repaymentPeriod / 12m);

                    monthlyPayment = (approvedAmount + totalInterest) / repaymentPeriod;

                    totalRepayable = approvedAmount + totalInterest;
                }
                else if (repayMethod == "RBAL")
                {
                    decimal remainingBalance = approvedAmount;
                    decimal monthlyPrincipal = approvedAmount / repaymentPeriod;

                    decimal firstMonthPayment = 0;

                    for (int i = 0; i < repaymentPeriod; i++)
                    {
                        decimal interest = remainingBalance * monthlyInterestRate;
                        totalInterest += interest;

                        decimal installment = monthlyPrincipal + interest;

                        if (i == 0)
                            firstMonthPayment = installment; // For display only

                        remainingBalance -= monthlyPrincipal;
                    }

                    totalRepayable = approvedAmount + totalInterest;

                    // ✅ DO NOT average payments
                    monthlyPayment = firstMonthPayment;
                }

                _logger.LogInformation($"Repayment Method: {repayMethod}, Monthly Rate: {monthlyInterestRate:P}, Total Interest: {totalInterest:C}");

                // Generate transaction numbers
                string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");

                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                // Create Block record
                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = await GetLastBlockHashAsync(),
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                // Prepare block data for transaction
                var blockData = new
                {
                    TransactionType = "LOAN_DISBURSEMENT",
                    LoanNo = disbursementDto.LoanNo,
                    MemberNo = loan.MemberNo,
                    ApprovedAmount = approvedAmount,
                    NetDisbursedAmount = netDisbursedAmount,
                    TotalInterest = totalInterest,
                    MonthlyPayment = monthlyPayment,
                    RepaymentPeriod = repaymentPeriod,
                    RepaymentMethod = repayMethod,
                    DisbursementDate = disbursementDto.DisbursementDate,
                    DisbursementMethod = disbursementDto.DisbursementMethod,
                    SourceBankId = disbursementDto.BankId,
                    GlAccountNo = disbursementDto.GlAccountNo,
                    ChequeNo = existingCheque.ChequeNo,
                    VoucherNo = existingCheque.Voucherno,
                    TransactionNo = transactionNo
                };

                // ============================================================
                // RECORD GL TRANSACTIONS
                // ============================================================

                string loanAssetAccount = existingCheque.LoanAcc ?? loanType?.LoanAcc ?? "LOAN_ASSET_ACCOUNT";
                string sourceAccount = existingCheque.ContraAcc ?? disbursementDto.GlAccountNo ?? "BANK_ACCOUNT";

                // Get all GL transactions from endorsement
                var endorsementGLTransactions = await _context.Gltransactions
                    .Where(gl => gl.DocumentNo == existingCheque.Voucherno && gl.Source == "LOAN_ENDORSEMENT")
                    .ToListAsync();

                var totalDeductions = endorsementGLTransactions.Sum(gl => gl.Amount);

                // Declare at the top of the method (after using var transaction)
                Gltransaction netDisbursementGL = null;

                // Then later, create the GL transaction
                netDisbursementGL = new Gltransaction
                {
                    TransDate = disbursementDto.DisbursementDate,
                    Amount = netDisbursedAmount,
                    DrAccNo = loanAssetAccount,
                    CrAccNo = sourceAccount,
                    Temp = "DISBURSEMENT",
                    DocumentNo = existingCheque.Voucherno,
                    Source = "LOAN_DISBURSEMENT",
                    CompanyCode = disbursementDto.CompanyCode,
                    TransDescript = $"Loan Disbursement - Net Amount - Loan {disbursementDto.LoanNo}",
                    AuditTime = DateTime.Now,
                    AuditId = disbursementDto.DisbursedBy,
                    Cash = 0,
                    DocPosted = 1,
                    ChequeNo = existingCheque.ChequeNo,
                    Dregard = false,
                    Recon = false,
                    TransactionNo = transactionNo,
                    Module = "LOAN",
                    ReconId = 0,
                    AuditDateTime = DateTime.Now
                };

                _context.Gltransactions.Add(netDisbursementGL);
                await _context.SaveChangesAsync();

                // ============================================================
                // CREATE LOANBAL RECORD
                // Balance = Approved Amount (from Endmain)
                // IntrCharged = Total Interest for the loan period
                // ============================================================
                var loanbal = new Loanbal
                {
                    LoanNo = disbursementDto.LoanNo,
                    LoanCode = loan.LoanCode ?? "",
                    MemberNo = loan.MemberNo,
                    Balance = approvedAmount,
                    IntrOwed = totalInterest,
                    Installments = repaymentPeriod,
                    IntrOwed2 = 0,
                    FirstDate = disbursementDto.DisbursementDate,
                    RepayRate = monthlyPayment,
                    LastDate = disbursementDto.DisbursementDate.AddMonths(repaymentPeriod),
                    Duedate = disbursementDto.DisbursementDate.AddMonths(1),
                    IntrCharged = totalInterest,  // Total interest charged for the loan
                    Interest = annualInterestRate,
                    Companycode = disbursementDto.CompanyCode,
                    Penalty = 0,
                    RepayRate2 = monthlyPayment,
                    RepayMethod = repayMethod,
                    Cleared = false,
                    AutoCalc = true,
                    IntrAmount = 0,
                    RepayPeriod = repaymentPeriod,
                    Remarks = disbursementDto.Remarks,
                    AuditId = disbursementDto.DisbursedBy,
                    AuditTime = DateTime.Now,
                    IntBalance = totalInterest,
                    CategoryCode = null,
                    InterestAccrued = 0,
                    Defaulter = "N",
                    Processdate = DateTime.Now,
                    Receiptno = null,
                    Cease = "N",
                    Nextduedate = disbursementDto.DisbursementDate.AddMonths(1),
                    TransactionNo = transactionNo,
                    Year = DateTime.Now.Year.ToString(),
                    Month = DateTime.Now.Month.ToString(),
                    RepayMode = 1,
                    Gperiod = null,
                    ApiKey = null,
                    UserName = disbursementDto.DisbursedBy,
                    Run = 0,
                    SerialNo = null,
                    AuditDateTime = DateTime.Now,
                    BlockchainTxId = null
                };

                _context.Loanbal.Add(loanbal);
                await _context.SaveChangesAsync();

                // Update existing Cheque record to mark as disbursed
                existingCheque.Status = "Disbursed";
                existingCheque.DateIssued = disbursementDto.DisbursementDate;
                existingCheque.AmountIssued = netDisbursedAmount;
                existingCheque.Balance = netDisbursedAmount;
                existingCheque.AuditDateTime = DateTime.Now;
                existingCheque.UserName = disbursementDto.DisbursedBy;
                existingCheque.TransactionNo = transactionNo;
                _context.Cheques.Update(existingCheque);
                await _context.SaveChangesAsync();

                // Update Loan table
                loan.Status = (int)Status.Disbursed;
                loan.Posted = "ACTIVE";
                loan.Aamount = approvedAmount;  // Outstanding balance (approved amount)
                loan.AuditTime = disbursementDto.DisbursementDate;
                loan.UserName = disbursementDto.DisbursedBy;
                loan.AuditDateTime = DateTime.Now;
                loan.TransactionNo = transactionNo;
                _context.Loans.Update(loan);
                await _context.SaveChangesAsync();

                // Create Blockchain Transaction
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_DISBURSEMENT",
                    MemberNo = loan.MemberNo,
                    CompanyCode = loan.CompanyCode,
                    Amount = netDisbursedAmount,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockData),
                    OffChainReferenceId = existingCheque.Voucherno,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Update ALL records with BlockchainTxId
                loanbal.BlockchainTxId = blockchainTx.TransactionId;
                loan.BlockchainTxId = blockchainTx.TransactionId;
                if (netDisbursementGL != null)
                {
                    netDisbursementGL.BlockchainTxId = blockchainTx.TransactionId;
                }
                existingCheque.BlockchainTxId = blockchainTx.TransactionId;

                await _context.SaveChangesAsync();

                // Generate Loan Schedule
                await GenerateLoanScheduleAsync(disbursementDto.LoanNo, approvedAmount, annualInterestRate,
                    repaymentPeriod, disbursementDto.DisbursementDate, disbursementDto.CompanyCode, repayMethod);

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN DISBURSEMENT
                // ============================================================

                // Audit Extra Data
                var auditExtraData = new
                {
                    loanNo = disbursementDto.LoanNo,
                    applicantMemberNo = loan.MemberNo,
                    applicantName = memberName,
                    approvedAmount = approvedAmount,
                    netDisbursedAmount = netDisbursedAmount,
                    totalDeductions = totalDeductions,
                    annualInterestRate = annualInterestRate,
                    monthlyInterestRate = monthlyInterestRate,
                    repaymentPeriod = repaymentPeriod,
                    repaymentMethod = repayMethod,
                    totalInterest = totalInterest,
                    monthlyPayment = monthlyPayment,
                    totalRepayable = totalRepayable,
                    disbursementDate = disbursementDto.DisbursementDate,
                    disbursementMethod = disbursementDto.DisbursementMethod,
                    sourceBankId = disbursementDto.BankId,
                    sourceGlAccount = disbursementDto.GlAccountNo,
                    loanAssetAccount = loanAssetAccount,
                    sourceAccount = sourceAccount,
                    chequeNo = existingCheque.ChequeNo,
                    voucherNo = existingCheque.Voucherno,
                    transactionNo = transactionNo,
                    blockHash = blockHash,
                    remarks = disbursementDto.Remarks ?? "",
                    disbursedBy = disbursementDto.DisbursedBy,
                    disbursedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanAmountBefore = oldLoanAmt,
                    loanAmountAfter = loan.LoanAmt,
                    aamountBefore = oldAamount,
                    aamountAfter = loan.Aamount,
                    chequeStatusBefore = oldChequeStatus,
                    chequeStatusAfter = existingCheque.Status,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // LoanBal for Audit (NewValue)
                var loanbalForAudit = new
                {
                    loanbal.Id,
                    loanbal.LoanNo,
                    loanbal.MemberNo,
                    loanbal.Balance,
                    loanbal.IntrOwed,
                    loanbal.IntrCharged,
                    loanbal.Installments,
                    loanbal.RepayRate,
                    loanbal.FirstDate,
                    loanbal.LastDate,
                    loanbal.Duedate,
                    loanbal.Interest,
                    loanbal.RepayMethod,
                    loanbal.Remarks,
                    loanbal.TransactionNo,
                    CreatedBy = disbursementDto.DisbursedBy,
                    CreatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                // Save Audit for LoanBal (INSERT)
                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: loanbalForAudit,
                    tableName: "Loanbal",
                    recordId: loanbal.Id.ToString(),
                    userId: disbursementDto.DisbursedBy,
                    userName: disbursementDto.DisbursedBy,
                    companyCode: disbursementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE
                // ============================================================
                var loanAuditExtraData = new
                {
                    loanNo = disbursementDto.LoanNo,
                    statusChangedFrom = oldLoanStatus,
                    statusChangedTo = loan.Status,
                    reason = "Loan disbursed - funds released to member",
                    approvedAmount = approvedAmount,
                    netDisbursedAmount = netDisbursedAmount,
                    totalDeductions = totalDeductions,
                    chequeNo = existingCheque.ChequeNo,
                    voucherNo = existingCheque.Voucherno,
                    disbursementDate = disbursementDto.DisbursementDate,
                    disbursementMethod = disbursementDto.DisbursementMethod,
                    disbursedBy = disbursementDto.DisbursedBy,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.LoanAmt,
                    loan.Aamount,
                    loan.Status,
                    loan.Posted,
                    loan.UserName,
                    loan.AuditDateTime,
                    loan.TransactionNo,
                    UpdatedBy = disbursementDto.DisbursedBy,
                    UpdateReason = "Loan disbursed",
                    NetDisbursedAmount = netDisbursedAmount,
                    TotalInterest = totalInterest,
                    MonthlyPayment = monthlyPayment
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new
                    {
                        Status = oldLoanStatus,
                        Posted = oldLoanPosted,
                        LoanAmt = oldLoanAmt,
                        Aamount = oldAamount
                    },
                    newModel: loanForAudit,
                    tableName: "Loans",
                    recordId: disbursementDto.LoanNo,
                    userId: disbursementDto.DisbursedBy,
                    userName: disbursementDto.DisbursedBy,
                    companyCode: disbursementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR CHEQUE UPDATE
                // ============================================================
                var chequeAuditExtraData = new
                {
                    loanNo = disbursementDto.LoanNo,
                    chequeNo = existingCheque.ChequeNo,
                    voucherNo = existingCheque.Voucherno,
                    statusChangedFrom = oldChequeStatus,
                    statusChangedTo = existingCheque.Status,
                    amountIssuedBefore = oldChequeAmountIssued,
                    amountIssuedAfter = existingCheque.AmountIssued,
                    balanceBefore = oldChequeBalance,
                    balanceAfter = existingCheque.Balance,
                    netDisbursedAmount = netDisbursedAmount,
                    disbursementDate = disbursementDto.DisbursementDate,
                    disbursedBy = disbursementDto.DisbursedBy,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var chequeForAudit = new
                {
                    existingCheque.Id,
                    existingCheque.LoanNo,
                    existingCheque.MemberNo,
                    existingCheque.Status,
                    existingCheque.AmountIssued,
                    existingCheque.Balance,
                    existingCheque.DateIssued,
                    existingCheque.UserName,
                    existingCheque.TransactionNo,
                    UpdatedBy = disbursementDto.DisbursedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new { Status = oldChequeStatus, AmountIssued = oldChequeAmountIssued, Balance = oldChequeBalance },
                    newModel: chequeForAudit,
                    tableName: "Cheques",
                    recordId: existingCheque.Id.ToString(),
                    userId: disbursementDto.DisbursedBy,
                    userName: disbursementDto.DisbursedBy,
                    companyCode: disbursementDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(chequeAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR GL TRANSACTION
                // ============================================================
                if (netDisbursementGL != null)
                {
                    var glAuditExtraData = new
                    {
                        loanNo = disbursementDto.LoanNo,
                        glTransactionId = netDisbursementGL.Id,
                        transactionType = "LOAN_DISBURSEMENT",
                        amount = netDisbursedAmount,
                        drAccount = loanAssetAccount,
                        crAccount = sourceAccount,
                        documentNo = existingCheque.Voucherno,
                        chequeNo = existingCheque.ChequeNo,
                        transactionNo = transactionNo,
                        disbursementDate = disbursementDto.DisbursementDate,
                        disbursedBy = disbursementDto.DisbursedBy,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var glForAudit = new
                    {
                        netDisbursementGL.Id,
                        netDisbursementGL.TransDate,
                        netDisbursementGL.Amount,
                        netDisbursementGL.DrAccNo,
                        netDisbursementGL.CrAccNo,
                        netDisbursementGL.DocumentNo,
                        netDisbursementGL.Source,
                        netDisbursementGL.TransDescript,
                        netDisbursementGL.ChequeNo,
                        netDisbursementGL.TransactionNo,
                        CreatedBy = disbursementDto.DisbursedBy,
                        CreatedDate = DateTime.Now,
                        BlockchainTxId = blockchainTx.TransactionId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: glForAudit,
                        tableName: "Gltransactions",
                        recordId: netDisbursementGL.Id.ToString(),
                        userId: disbursementDto.DisbursedBy,
                        userName: disbursementDto.DisbursedBy,
                        companyCode: disbursementDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(glAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );
                }

                _logger.LogInformation($"Disbursement audit completed for loan {disbursementDto.LoanNo}");

                await transaction.CommitAsync();

                _logger.LogInformation($"Loan {loan.LoanNo} disbursed successfully. " +
                    $"Approved: {approvedAmount:C}, Net Disbursed: {netDisbursedAmount:C}, " +
                    $"Total Interest: {totalInterest:C}, Monthly Payment: {monthlyPayment:C}");

                return existingCheque;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error disbursing loan {disbursementDto.LoanNo}");
                throw;
            }
        }

        //public async Task<Cheque> DisburseLoanAsync(LoanDisbursementDTO disbursementDto)
        //{
        //    var currentUserRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

        //    if (currentUserRole != "Finance Officer" && currentUserRole != "Super Admin" && currentUserRole != "Admin")
        //    {
        //        throw new UnauthorizedAccessException("Only Finance Officers can disburse loans");
        //    }

        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Disbursing loan {disbursementDto.LoanNo}");

        //        // Get loan details
        //        var loan = await _context.Loans
        //            .FirstOrDefaultAsync(l => l.LoanNo == disbursementDto.LoanNo && l.CompanyCode == disbursementDto.CompanyCode);

        //        if (loan == null)
        //        {
        //            throw new InvalidOperationException($"Loan {disbursementDto.LoanNo} not found");
        //        }

        //        _logger.LogInformation($"Loan found: Status={loan.Status}, LoanAmt={loan.LoanAmt}");

        //        if (loan.Status != (int)Status.Endorsed)
        //        {
        //            throw new InvalidOperationException($"Loan cannot be disbursed in status '{loan.Status}'. Loan must be Endorsed.");
        //        }

        //        // Get endorsement record
        //        var endmain = await _context.Endmain
        //            .FirstOrDefaultAsync(e => e.LoanNo == disbursementDto.LoanNo && e.CompanyCode == disbursementDto.CompanyCode);

        //        if (endmain == null)
        //        {
        //            throw new InvalidOperationException($"Endorsement not found for loan {disbursementDto.LoanNo}");
        //        }

        //        // Get existing cheque from endorsement
        //        var existingCheque = await _context.Cheques
        //            .FirstOrDefaultAsync(c => c.LoanNo == disbursementDto.LoanNo && c.CompanyCode == disbursementDto.CompanyCode);

        //        if (existingCheque == null)
        //        {
        //            throw new InvalidOperationException($"Cheque record not found for loan {disbursementDto.LoanNo}. Please ensure endorsement was completed.");
        //        }

        //        // Get LoanType for repayment method and grace period
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == disbursementDto.CompanyCode);

        //        // IMPORTANT: Balance = Approved Amount (from Endmain.AmtApproved)
        //        decimal approvedAmount = endmain.AmtApproved;  // This is the amount BEFORE deductions
        //        decimal netDisbursedAmount = existingCheque.AmountIssued ?? approvedAmount;  // Amount AFTER deductions (what member gets)

        //        _logger.LogInformation($"Approved Amount: {approvedAmount:C}, Net Disbursed: {netDisbursedAmount:C}");

        //        // Check if already disbursed
        //        var existingLoanbal = await _context.Loanbal
        //            .FirstOrDefaultAsync(lb => lb.LoanNo == disbursementDto.LoanNo && lb.Companycode == disbursementDto.CompanyCode);

        //        if (existingLoanbal != null)
        //        {
        //            throw new InvalidOperationException($"Loan already disbursed. Loan balance record exists.");
        //        }

        //        // ============================================================
        //        // CALCULATE TOTAL INTEREST FOR THE LOAN PERIOD
        //        decimal annualInterestRate = loan.Interest ?? 0;
        //        decimal monthlyInterestRate = (annualInterestRate / 100) / 12;
        //        int repaymentPeriod = loan.RepayPeriod ?? 12;
        //        string repayMethod = loan.RepayMethod ?? loanType?.Repaymethod ?? "AMT";

        //        decimal totalInterest = 0;
        //        decimal monthlyPayment = 0;
        //        decimal totalRepayable = 0;

        //        if (repayMethod == "AMT")
        //        {
        //            // ✅ EMI
        //            if (monthlyInterestRate > 0)
        //            {
        //                decimal factor = (decimal)Math.Pow((double)(1 + monthlyInterestRate), repaymentPeriod);
        //                monthlyPayment = approvedAmount * monthlyInterestRate * factor / (factor - 1);
        //                totalInterest = (monthlyPayment * repaymentPeriod) - approvedAmount;
        //            }
        //            else
        //            {
        //                monthlyPayment = approvedAmount / repaymentPeriod;
        //                totalInterest = 0;
        //            }

        //            totalRepayable = approvedAmount + totalInterest;
        //        }
        //        else if (repayMethod == "STL")
        //        {
        //            // ✅ FLAT RATE (Straight Line)
        //            totalInterest = approvedAmount * (annualInterestRate / 100) * (repaymentPeriod / 12m);

        //            monthlyPayment = (approvedAmount + totalInterest) / repaymentPeriod;

        //            totalRepayable = approvedAmount + totalInterest;
        //        }
        //        else if (repayMethod == "RBAL")
        //        {
        //            decimal remainingBalance = approvedAmount;
        //            decimal monthlyPrincipal = approvedAmount / repaymentPeriod;

        //            decimal firstMonthPayment = 0;

        //            for (int i = 0; i < repaymentPeriod; i++)
        //            {
        //                decimal interest = remainingBalance * monthlyInterestRate;
        //                totalInterest += interest;

        //                decimal installment = monthlyPrincipal + interest;

        //                if (i == 0)
        //                    firstMonthPayment = installment; // For display only

        //                remainingBalance -= monthlyPrincipal;
        //            }

        //            totalRepayable = approvedAmount + totalInterest;

        //            // ✅ DO NOT average payments
        //            monthlyPayment = firstMonthPayment; 
        //        }

        //        _logger.LogInformation($"Repayment Method: {repayMethod}, Monthly Rate: {monthlyInterestRate:P}, Total Interest: {totalInterest:C}");

        //        // Generate transaction numbers
        //        string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
        //        string blockHash = Guid.NewGuid().ToString().Replace("-", "");

        //        if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
        //        else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

        //        // Create Block record
        //        var block = new Block
        //        {
        //            BlockHash = blockHash,
        //            PreviousHash = await GetLastBlockHashAsync(),
        //            Timestamp = DateTime.Now,
        //            Nonce = 0,
        //            MerkleRoot = Guid.NewGuid().ToString(),
        //            Confirmed = true,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.Blocks.Add(block);
        //        await _context.SaveChangesAsync();

        //        // Prepare block data for transaction
        //        var blockData = new
        //        {
        //            TransactionType = "LOAN_DISBURSEMENT",
        //            LoanNo = disbursementDto.LoanNo,
        //            MemberNo = loan.MemberNo,
        //            ApprovedAmount = approvedAmount,
        //            NetDisbursedAmount = netDisbursedAmount,
        //            TotalInterest = totalInterest,
        //            MonthlyPayment = monthlyPayment,
        //            RepaymentPeriod = repaymentPeriod,
        //            RepaymentMethod = repayMethod,
        //            DisbursementDate = disbursementDto.DisbursementDate,
        //            DisbursementMethod = disbursementDto.DisbursementMethod,
        //            SourceBankId = disbursementDto.BankId,
        //            GlAccountNo = disbursementDto.GlAccountNo,
        //            ChequeNo = existingCheque.ChequeNo,
        //            VoucherNo = existingCheque.Voucherno,
        //            TransactionNo = transactionNo
        //        };

        //        // ============================================================
        //        // RECORD GL TRANSACTIONS
        //        // ============================================================

        //        string loanAssetAccount = existingCheque.LoanAcc ?? loanType?.LoanAcc ?? "LOAN_ASSET_ACCOUNT";
        //        string sourceAccount = existingCheque.ContraAcc ?? disbursementDto.GlAccountNo ?? "BANK_ACCOUNT";

        //        // Get all GL transactions from endorsement
        //        var endorsementGLTransactions = await _context.Gltransactions
        //            .Where(gl => gl.DocumentNo == existingCheque.Voucherno && gl.Source == "LOAN_ENDORSEMENT")
        //            .ToListAsync();

        //        var totalDeductions = endorsementGLTransactions.Sum(gl => gl.Amount);

        //        // Declare at the top of the method (after using var transaction)
        //        Gltransaction netDisbursementGL = null;

        //        // Then later, create the GL transaction
        //        netDisbursementGL = new Gltransaction
        //        {
        //            TransDate = disbursementDto.DisbursementDate,
        //            Amount = netDisbursedAmount,
        //            DrAccNo = loanAssetAccount,
        //            CrAccNo = sourceAccount,
        //            Temp = "DISBURSEMENT",
        //            DocumentNo = existingCheque.Voucherno,
        //            Source = "LOAN_DISBURSEMENT",
        //            CompanyCode = disbursementDto.CompanyCode,
        //            TransDescript = $"Loan Disbursement - Net Amount - Loan {disbursementDto.LoanNo}",
        //            AuditTime = DateTime.Now,
        //            AuditId = disbursementDto.DisbursedBy,
        //            Cash = 0,
        //            DocPosted = 1,
        //            ChequeNo = existingCheque.ChequeNo,
        //            Dregard = false,
        //            Recon = false,
        //            TransactionNo = transactionNo,
        //            Module = "LOAN",
        //            ReconId = 0,
        //            AuditDateTime = DateTime.Now
        //        };

        //        _context.Gltransactions.Add(netDisbursementGL);
        //        await _context.SaveChangesAsync();

        //        // ============================================================
        //        // CREATE LOANBAL RECORD
        //        // Balance = Approved Amount (from Endmain)
        //        // IntrCharged = Total Interest for the loan period
        //        // ============================================================
        //        var loanbal = new Loanbal
        //        {
        //            LoanNo = disbursementDto.LoanNo,
        //            LoanCode = loan.LoanCode ?? "",
        //            MemberNo = loan.MemberNo,
        //            Balance = approvedAmount, 
        //            IntrOwed = totalInterest, 
        //            Installments = repaymentPeriod,
        //            IntrOwed2 = 0,
        //            FirstDate = disbursementDto.DisbursementDate,
        //            RepayRate = monthlyPayment,
        //            LastDate = disbursementDto.DisbursementDate.AddMonths(repaymentPeriod),
        //            Duedate = disbursementDto.DisbursementDate.AddMonths(1),
        //            IntrCharged = totalInterest,  // Total interest charged for the loan
        //            Interest = annualInterestRate,
        //            Companycode = disbursementDto.CompanyCode,
        //            Penalty = 0,
        //            RepayRate2 = monthlyPayment,
        //            RepayMethod = repayMethod,
        //            Cleared = false,
        //            AutoCalc = true,
        //            IntrAmount = 0,
        //            RepayPeriod = repaymentPeriod,
        //            Remarks = disbursementDto.Remarks,
        //            AuditId = disbursementDto.DisbursedBy,
        //            AuditTime = DateTime.Now,
        //            IntBalance = totalInterest,
        //            CategoryCode = null,
        //            InterestAccrued = 0,
        //            Defaulter = "N",
        //            Processdate = DateTime.Now,
        //            Receiptno = null,
        //            Cease = "N",
        //            Nextduedate = disbursementDto.DisbursementDate.AddMonths(1),
        //            TransactionNo = transactionNo,
        //            Year = DateTime.Now.Year.ToString(),
        //            Month = DateTime.Now.Month.ToString(),
        //            RepayMode = 1,
        //            Gperiod = null,
        //            ApiKey = null,
        //            UserName = disbursementDto.DisbursedBy,
        //            Run = 0,
        //            SerialNo = null,
        //            AuditDateTime = DateTime.Now,
        //            BlockchainTxId = null
        //        };

        //        _context.Loanbal.Add(loanbal);
        //        await _context.SaveChangesAsync();

        //        // Update existing Cheque record to mark as disbursed
        //        existingCheque.Status = "Disbursed";
        //        existingCheque.DateIssued = disbursementDto.DisbursementDate;
        //        existingCheque.AmountIssued = netDisbursedAmount;
        //        existingCheque.Balance = netDisbursedAmount;
        //        existingCheque.AuditDateTime = DateTime.Now;
        //        existingCheque.UserName = disbursementDto.DisbursedBy;
        //        existingCheque.TransactionNo = transactionNo;
        //        _context.Cheques.Update(existingCheque);
        //        await _context.SaveChangesAsync();

        //        // Update Loan table
        //        loan.Status = (int)Status.Disbursed;
        //        loan.Posted = "ACTIVE";
        //        loan.Aamount = approvedAmount;  // Outstanding balance (approved amount)
        //        loan.AuditTime = disbursementDto.DisbursementDate;
        //        loan.UserName = disbursementDto.DisbursedBy;
        //        loan.AuditDateTime = DateTime.Now;
        //        loan.TransactionNo = transactionNo;
        //        _context.Loans.Update(loan);
        //        await _context.SaveChangesAsync();

        //        // Create Blockchain Transaction
        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "LOAN_DISBURSEMENT",
        //            MemberNo = loan.MemberNo,
        //            CompanyCode = loan.CompanyCode,
        //            Amount = netDisbursedAmount,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockData),
        //            OffChainReferenceId = existingCheque.Voucherno,
        //            Status = "CONFIRMED",
        //            BlockHash = block.BlockHash,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        // Update ALL records with BlockchainTxId
        //        loanbal.BlockchainTxId = blockchainTx.TransactionId;
        //        loan.BlockchainTxId = blockchainTx.TransactionId;
        //        if (netDisbursementGL != null)
        //        {
        //            netDisbursementGL.BlockchainTxId = blockchainTx.TransactionId;
        //        }
        //        existingCheque.BlockchainTxId = blockchainTx.TransactionId;

        //        await _context.SaveChangesAsync();

        //        // Generate Loan Schedule
        //        await GenerateLoanScheduleAsync(disbursementDto.LoanNo, approvedAmount, annualInterestRate,
        //            repaymentPeriod, disbursementDto.DisbursementDate, disbursementDto.CompanyCode, repayMethod);

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"Loan {loan.LoanNo} disbursed successfully. " +
        //            $"Approved: {approvedAmount:C}, Net Disbursed: {netDisbursedAmount:C}, " +
        //            $"Total Interest: {totalInterest:C}, Monthly Payment: {monthlyPayment:C}");

        //        return existingCheque;
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error disbursing loan {disbursementDto.LoanNo}");
        //        throw;
        //    }
        //}

        private async Task GenerateLoanScheduleAsync(string loanNo, decimal principalAmount, decimal interestRate,
            int repaymentPeriod, DateTime disbursementDate, string companyCode, string repayMethod)
        {
            try
            {
                _logger.LogInformation($"Generating loan schedule for loan {loanNo} with method {repayMethod}");
                _logger.LogInformation($"Principal: {principalAmount:C}, Interest Rate: {interestRate}%, Period: {repaymentPeriod} months");

                var existingSchedule = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo)
                    .ToListAsync();

                if (existingSchedule.Any())
                {
                    _context.LoanSchedules.RemoveRange(existingSchedule);
                    await _context.SaveChangesAsync();
                }

                // FIX: Convert percentage to decimal
                decimal monthlyRate = (interestRate / 100) / 12;
                int totalPayments = repaymentPeriod;

                _logger.LogInformation($"Monthly Interest Rate: {monthlyRate:P4}");

                var scheduleEntries = new List<LoanSchedule>();

                if (repayMethod == "STL")
                {
                    decimal monthlyPrincipal = principalAmount / totalPayments;

                    // ✅ Flat interest (NOT reducing)
                    decimal totalInterest = principalAmount * (interestRate / 100) * (totalPayments / 12m);
                    decimal monthlyInterest = totalInterest / totalPayments;

                    decimal remainingBalance = principalAmount;

                    for (int i = 1; i <= totalPayments; i++)
                    {
                        decimal totalInstallment = monthlyPrincipal + monthlyInterest;

                        scheduleEntries.Add(new LoanSchedule
                        {
                            LoanNo = loanNo,
                            CompanyCode = companyCode,
                            InstallmentNo = i,
                            DueDate = disbursementDate.AddMonths(i),

                            PrincipalAmount = monthlyPrincipal,
                            InterestAmount = monthlyInterest,
                            TotalInstallment = totalInstallment,

                            BalancePrincipal = remainingBalance - monthlyPrincipal,
                            BalanceInterest = totalInterest - (monthlyInterest * i),
                            BalanceTotal = (remainingBalance - monthlyPrincipal) + (totalInterest - (monthlyInterest * i)),

                            PaidPrincipal = 0,
                            PaidInterest = 0,
                            PaidTotal = 0,

                            OutstandingPrincipal = monthlyPrincipal,
                            OutstandingInterest = monthlyInterest,
                            OutstandingTotal = totalInstallment,

                            PenaltyAmount = 0,
                            Status = "Pending",
                            IsFlexible = false,
                            MinimumPayment = totalInstallment,
                            DaysOverdue = 0
                        });

                        remainingBalance -= monthlyPrincipal;
                    }
                }
                else if (repayMethod == "AMT")
                {
                    // AMT: Equal Monthly Installments
                    decimal monthlyPayment;
                    if (monthlyRate > 0)
                    {
                        double factor = Math.Pow((double)(1 + monthlyRate), totalPayments);
                        monthlyPayment = principalAmount * monthlyRate * (decimal)factor / ((decimal)factor - 1);
                    }
                    else
                    {
                        monthlyPayment = principalAmount / totalPayments;
                    }

                    decimal remainingBalance = principalAmount;
                    decimal totalInterestAccumulated = 0;

                    for (int i = 1; i <= totalPayments; i++)
                    {
                        decimal interestAmount = remainingBalance * monthlyRate;
                        decimal principalAmountPayment = monthlyPayment - interestAmount;
                        totalInterestAccumulated += interestAmount;

                        if (i == totalPayments)
                        {
                            principalAmountPayment = remainingBalance;
                            monthlyPayment = principalAmountPayment + interestAmount;
                        }

                        scheduleEntries.Add(new LoanSchedule
                        {
                            LoanNo = loanNo,
                            CompanyCode = companyCode,
                            InstallmentNo = i,
                            DueDate = disbursementDate.AddMonths(i),
                            PrincipalAmount = principalAmountPayment,
                            InterestAmount = interestAmount,
                            TotalInstallment = monthlyPayment,
                            BalancePrincipal = remainingBalance - principalAmountPayment,
                            BalanceInterest = totalInterestAccumulated - interestAmount,
                            BalanceTotal = (remainingBalance - principalAmountPayment) + (totalInterestAccumulated - interestAmount),
                            PaidPrincipal = 0,
                            PaidInterest = 0,
                            PaidTotal = 0,
                            OutstandingPrincipal = principalAmountPayment,
                            OutstandingInterest = interestAmount,
                            OutstandingTotal = monthlyPayment,
                            PenaltyAmount = 0,
                            Status = "Pending",
                            IsFlexible = false,
                            MinimumPayment = monthlyPayment,
                            DaysOverdue = 0
                        });

                        remainingBalance -= principalAmountPayment;

                        _logger.LogInformation($"AMT Month {i}: Principal={principalAmountPayment:C}, Interest={interestAmount:C}, Total={monthlyPayment:C}, Balance={remainingBalance:C}");
                    }
                }
                else if (repayMethod == "RBAL")
                {
                    // RBAL: Interest only minimum
                    decimal remainingBalance = principalAmount;
                    decimal totalInterestAccumulated = 0;

                    for (int i = 1; i <= totalPayments; i++)
                    {
                        decimal interestAmount = remainingBalance * monthlyRate;
                        totalInterestAccumulated += interestAmount;

                        scheduleEntries.Add(new LoanSchedule
                        {
                            LoanNo = loanNo,
                            CompanyCode = companyCode,
                            InstallmentNo = i,
                            DueDate = disbursementDate.AddMonths(i),
                            PrincipalAmount = 0,
                            InterestAmount = interestAmount,
                            TotalInstallment = interestAmount,
                            BalancePrincipal = remainingBalance,
                            BalanceInterest = totalInterestAccumulated - interestAmount,
                            BalanceTotal = remainingBalance,
                            PaidPrincipal = 0,
                            PaidInterest = 0,
                            PaidTotal = 0,
                            OutstandingPrincipal = 0,
                            OutstandingInterest = interestAmount,
                            OutstandingTotal = interestAmount,
                            PenaltyAmount = 0,
                            Status = "Pending",
                            IsFlexible = true,
                            MinimumPayment = interestAmount,
                            DaysOverdue = 0
                        });
                    }
                }

                await _context.LoanSchedules.AddRangeAsync(scheduleEntries);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Generated {scheduleEntries.Count} schedule entries for loan {loanNo}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating loan schedule for loan {loanNo}");
                throw;
            }
        }

        private async Task<string> GetLastBlockHashAsync()
        {
            try
            {
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                return lastBlock?.BlockHash ?? "0".PadLeft(64, '0');
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last block hash, using default");
                return "0".PadLeft(64, '0');
            }
        }

        public async Task<Cheque> GetLoanDisbursementAsync(string loanNo)
        {
            return await _context.Cheques
                .FirstOrDefaultAsync(c => c.LoanNo == loanNo);
        }

        public async Task<Loanbal> GetLoanBalanceAsync(string loanNo)
        {
            return await _context.Loanbal
                .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo);
        }

        private async Task GenerateLoanScheduleAsync(string loanNo, decimal principalAmount, decimal interestRate, int repaymentPeriod, DateTime disbursementDate, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Generating loan schedule for loan {loanNo}");

                var existingSchedule = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo)
                    .ToListAsync();

                if (existingSchedule.Any())
                {
                    _context.LoanSchedules.RemoveRange(existingSchedule);
                    await _context.SaveChangesAsync();
                }

                decimal monthlyRate = interestRate / 12;
                int totalPayments = repaymentPeriod;

                decimal monthlyPayment;
                if (monthlyRate > 0)
                {
                    decimal factor = (decimal)Math.Pow((double)(1 + monthlyRate), totalPayments);
                    monthlyPayment = principalAmount * monthlyRate * factor / (factor - 1);
                }
                else
                {
                    monthlyPayment = principalAmount / totalPayments;
                }

                var scheduleEntries = new List<LoanSchedule>();
                decimal remainingBalance = principalAmount;

                for (int i = 1; i <= totalPayments; i++)
                {
                    decimal interestAmount = remainingBalance * monthlyRate;
                    decimal principalAmountPayment = monthlyPayment - interestAmount;

                    if (i == totalPayments)
                    {
                        principalAmountPayment = remainingBalance;
                        monthlyPayment = principalAmountPayment + interestAmount;
                    }

                    scheduleEntries.Add(new LoanSchedule
                    {
                        LoanNo = loanNo,
                        CompanyCode = companyCode,
                        InstallmentNo = i,
                        DueDate = disbursementDate.AddMonths(i),
                        PrincipalAmount = principalAmountPayment,
                        InterestAmount = interestAmount,
                        TotalInstallment = monthlyPayment,
                        PaidPrincipal = 0,
                        PaidInterest = 0,
                        PaidTotal = 0,
                        OutstandingPrincipal = remainingBalance - principalAmountPayment,
                        OutstandingInterest = 0,
                        OutstandingTotal = monthlyPayment,
                        PenaltyAmount = 0,
                        Status = "Pending",
                        IsFlexible = false,
                        DaysOverdue = 0
                    });

                    remainingBalance -= principalAmountPayment;
                }

                await _context.LoanSchedules.AddRangeAsync(scheduleEntries);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Generated {scheduleEntries.Count} schedule entries for loan {loanNo}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating loan schedule for loan {loanNo}");
                throw;
            }
        }

        #endregion



        #region Repayments

        public async Task<Repay> ProcessRepaymentAsync(LoanRepaymentDTO repaymentDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Processing repayment for loan {repaymentDto.LoanNo}, Amount: {repaymentDto.AmountPaid:C}");

                // 1. GET LOAN DATA
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == repaymentDto.LoanNo && l.CompanyCode == repaymentDto.CompanyCode);

                if (loan == null)
                    throw new InvalidOperationException($"Loan {repaymentDto.LoanNo} not found");

                // Store old loan values for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                decimal oldLoanAamount = loan.Aamount ?? 0;

                if (loan.Status != (int)Status.Disbursed && loan.Status != (int)Status.Endorsed)
                    throw new InvalidOperationException($"Cannot process repayment for loan in status '{loan.Status}'");

                // 2. GET LOAN BALANCE
                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == repaymentDto.LoanNo && lb.Companycode == repaymentDto.CompanyCode);

                if (loanbal == null)
                    throw new InvalidOperationException($"Loan balance record not found");

                // Store old loanbal values for audit
                decimal oldBalance = loanbal.Balance;
                decimal oldIntrOwed = loanbal.IntrOwed;
                decimal oldPenalty = loanbal.Penalty;
                decimal oldIntBalance = loanbal.IntBalance;

                // 3. GET ALL REMAINING SCHEDULES
                var remainingSchedules = await _context.LoanSchedules
                    .Where(s => s.LoanNo == repaymentDto.LoanNo && s.Status != "Paid")
                    .OrderBy(s => s.InstallmentNo)
                    .ToListAsync();

                if (!remainingSchedules.Any())
                {
                    if (loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m)
                    {
                        throw new InvalidOperationException("Loan is already fully paid.");
                    }
                    throw new InvalidOperationException("No active schedule found for this loan.");
                }

                // Get the current schedule (first unpaid)
                var currentSchedule = remainingSchedules.First();

                // Store old schedule values for audit
                decimal oldScheduleOutstandingPrincipal = currentSchedule.OutstandingPrincipal;
                decimal oldScheduleOutstandingInterest = currentSchedule.OutstandingInterest;
                decimal oldScheduleOutstandingTotal = currentSchedule.OutstandingTotal;
                string oldScheduleStatus = currentSchedule.Status;

                _logger.LogInformation($"Current Schedule - Installment {currentSchedule.InstallmentNo}, " +
                    $"Outstanding Principal: {currentSchedule.OutstandingPrincipal:C}, " +
                    $"Outstanding Interest: {currentSchedule.OutstandingInterest:C}");

                // 4. GET LOAN TYPE FOR PENALTY SETTINGS
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == repaymentDto.CompanyCode);

                // 5. CALCULATE TOTAL REMAINING BALANCE (for early settlement detection)
                decimal totalRemainingPrincipal = loanbal.Balance;
                decimal totalRemainingInterest = loanbal.IntrOwed;
                decimal totalRemainingPenalty = loanbal.Penalty;
                decimal totalFullBalance = totalRemainingPrincipal + totalRemainingInterest + totalRemainingPenalty;

                // 6. CHECK IF THIS IS AN EARLY FULL SETTLEMENT
                bool isEarlyFullSettlement = repaymentDto.AmountPaid >= totalFullBalance - 0.01m;

                decimal penaltyAllocated = 0;
                decimal interestAllocated = 0;
                decimal principalAllocated = 0;
                decimal overpaymentAmount = 0;

                if (isEarlyFullSettlement)
                {
                    _logger.LogInformation($"EARLY FULL SETTLEMENT detected! Amount: {repaymentDto.AmountPaid:C}, Total Due: {totalFullBalance:C}");

                    // ALLOCATE FULL BALANCE
                    penaltyAllocated = totalRemainingPenalty;
                    interestAllocated = totalRemainingInterest;
                    principalAllocated = totalRemainingPrincipal;
                    overpaymentAmount = repaymentDto.AmountPaid - totalFullBalance;

                    _logger.LogInformation($"Full Settlement Allocation: Principal={principalAllocated:C}, Interest={interestAllocated:C}, Penalty={penaltyAllocated:C}, Overpayment={overpaymentAmount:C}");
                }
                else
                {
                    // Regular installment payment - calculate penalty if overdue
                    decimal penaltyAmount = 0;
                    int daysOverdue = 0;

                    if (loanType != null && loanType.Penalty == 1 && repaymentDto.PaymentDate > currentSchedule.DueDate)
                    {
                        daysOverdue = (repaymentDto.PaymentDate - currentSchedule.DueDate).Days;
                        int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 30;

                        if (daysOverdue > gracePeriodDays)
                        {
                            int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
                            int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);
                            decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
                            penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
                            _logger.LogInformation($"Penalty calculated: {penaltyAmount:C}");
                        }
                    }

                    decimal remainingAmount = repaymentDto.AmountPaid;

                    // Apply to penalty first
                    if (remainingAmount > 0 && penaltyAmount > 0)
                    {
                        penaltyAllocated = Math.Min(remainingAmount, penaltyAmount);
                        remainingAmount -= penaltyAllocated;
                    }

                    // Apply to interest (current schedule)
                    if (remainingAmount > 0 && currentSchedule.OutstandingInterest > 0)
                    {
                        interestAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingInterest);
                        remainingAmount -= interestAllocated;
                    }

                    // Apply to principal (current schedule)
                    if (remainingAmount > 0 && currentSchedule.OutstandingPrincipal > 0)
                    {
                        principalAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingPrincipal);
                        remainingAmount -= principalAllocated;
                    }

                    overpaymentAmount = remainingAmount;
                }

                // 7. GENERATE NUMBERS
                string receiptNo = await GenerateReceiptNumberAsync(repaymentDto.CompanyCode);
                string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
                int repaymentCount = await _context.Repay.CountAsync(r => r.LoanNo == repaymentDto.LoanNo && r.Posted == true);
                int paymentNo = repaymentCount + 1;

                // 8. CREATE REPAY RECORD
                var repayment = new Repay
                {
                    LoanNo = repaymentDto.LoanNo,
                    MemberNo = repaymentDto.MemberNo,
                    CompanyCode = repaymentDto.CompanyCode,
                    ReceiptNo = receiptNo,
                    PaymentNo = paymentNo,
                    DateReceived = repaymentDto.PaymentDate,
                    Amount = repaymentDto.AmountPaid,
                    Principal = principalAllocated,
                    Interest = interestAllocated,
                    Penalty = penaltyAllocated,
                    IntrCharged = interestAllocated,
                    IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated),
                    IntrAccrued = currentSchedule.InterestAmount,
                    LoanBalance = Math.Max(0, loanbal.Balance - principalAllocated),
                    RepayRate = currentSchedule.TotalInstallment,
                    Locked = false,
                    Posted = true,
                    Accrued = true,
                    Remarks = repaymentDto.Remarks + (overpaymentAmount > 0 ? $" (Overpayment: KES {overpaymentAmount:N2})" : "") + (isEarlyFullSettlement ? " - EARLY FULL SETTLEMENT" : ""),
                    AuditId = repaymentDto.ReceivedBy,
                    AuditTime = DateTime.Now,
                    Transby = repaymentDto.ReceivedBy,
                    IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated),
                    Loancode = loan.LoanCode,
                    Interestaccrued = currentSchedule.InterestAmount,
                    Transno = transactionNo,
                    TransDate = repaymentDto.PaymentDate,
                    TransactionNo = transactionNo,
                    ApiKey = repaymentDto.ReferenceNo,
                    UserName = repaymentDto.ReceivedBy,
                    Run = 0,
                    AuditDateTime = DateTime.Now,
                    BlockchainTxId = null
                };

                _context.Repay.Add(repayment);
                await _context.SaveChangesAsync();

                // 9. UPDATE ALL REMAINING SCHEDULES
                if (isEarlyFullSettlement)
                {
                    // Mark ALL remaining schedules as PAID
                    foreach (var schedule in remainingSchedules)
                    {
                        schedule.PaidPrincipal = schedule.PrincipalAmount;
                        schedule.PaidInterest = schedule.InterestAmount;
                        schedule.PaidTotal = schedule.TotalInstallment;
                        schedule.OutstandingPrincipal = 0;
                        schedule.OutstandingInterest = 0;
                        schedule.OutstandingTotal = 0;
                        schedule.Status = "Paid";
                        schedule.PaidDate = repaymentDto.PaymentDate;
                        schedule.PenaltyAmount = schedule.PenaltyAmount + (schedule == currentSchedule ? penaltyAllocated : 0);

                        _logger.LogInformation($"Schedule {schedule.InstallmentNo} marked as PAID (early settlement)");
                    }
                }
                else
                {
                    // Update only the current schedule (regular payment)
                    bool isCurrentInstallmentFullyPaid = (principalAllocated >= currentSchedule.OutstandingPrincipal - 0.01m) &&
                                                          (interestAllocated >= currentSchedule.OutstandingInterest - 0.01m);

                    if (isCurrentInstallmentFullyPaid)
                    {
                        currentSchedule.PaidPrincipal = currentSchedule.PrincipalAmount;
                        currentSchedule.PaidInterest = currentSchedule.InterestAmount;
                        currentSchedule.PaidTotal = currentSchedule.TotalInstallment;
                        currentSchedule.OutstandingPrincipal = 0;
                        currentSchedule.OutstandingInterest = 0;
                        currentSchedule.OutstandingTotal = 0;
                        currentSchedule.Status = "Paid";
                        currentSchedule.PaidDate = repaymentDto.PaymentDate;
                        currentSchedule.PenaltyAmount = currentSchedule.PenaltyAmount + penaltyAllocated;

                        _logger.LogInformation($"Installment {currentSchedule.InstallmentNo} marked as PAID");
                    }
                    else
                    {
                        // Partial payment - update the current schedule
                        currentSchedule.PaidPrincipal += principalAllocated;
                        currentSchedule.PaidInterest += interestAllocated;
                        currentSchedule.PaidTotal = currentSchedule.PaidPrincipal + currentSchedule.PaidInterest;
                        currentSchedule.OutstandingPrincipal = currentSchedule.PrincipalAmount - currentSchedule.PaidPrincipal;
                        currentSchedule.OutstandingInterest = currentSchedule.InterestAmount - currentSchedule.PaidInterest;
                        currentSchedule.OutstandingTotal = currentSchedule.OutstandingPrincipal + currentSchedule.OutstandingInterest;
                        currentSchedule.Status = "Partial";
                        currentSchedule.PenaltyAmount = currentSchedule.PenaltyAmount + penaltyAllocated;

                        _logger.LogInformation($"Installment {currentSchedule.InstallmentNo} marked as PARTIAL");
                    }
                }

                // 10. UPDATE LOANBAL RECORD
                loanbal.Balance = Math.Max(0, loanbal.Balance - principalAllocated);
                loanbal.IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated);
                loanbal.Penalty = Math.Max(0, loanbal.Penalty - penaltyAllocated);
                loanbal.IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated);
                loanbal.LastDate = repaymentDto.PaymentDate;
                loanbal.Processdate = DateTime.Now;

                // Update next due date
                if (!isEarlyFullSettlement)
                {
                    var nextSchedule = await _context.LoanSchedules
                        .Where(s => s.LoanNo == repaymentDto.LoanNo && s.Status != "Paid")
                        .OrderBy(s => s.InstallmentNo)
                        .FirstOrDefaultAsync();

                    if (nextSchedule != null)
                    {
                        loanbal.Nextduedate = nextSchedule.DueDate;
                        loanbal.Duedate = nextSchedule.DueDate;
                        loanbal.RepayRate = nextSchedule.TotalInstallment;
                    }
                    else if (loanbal.Balance > 0.01m || loanbal.IntrOwed > 0.01m)
                    {
                        loanbal.Nextduedate = repaymentDto.PaymentDate.AddMonths(1);
                        loanbal.Duedate = repaymentDto.PaymentDate.AddMonths(1);
                    }
                }

                // 11. CHECK IF LOAN IS FULLY PAID
                bool isFullyPaid = loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m;

                if (isFullyPaid)
                {
                    loanbal.Cleared = true;
                    loan.Status = (int)Status.Closed;
                    loan.Posted = "Closed";
                    loan.Aamount = 0;

                    await ReleaseCollateralGuaranteesForLoanAsync(repaymentDto.LoanNo, repaymentDto.ReceivedBy);

                    _logger.LogInformation($"Loan {loan.LoanNo} fully paid and closed");
                }
                else if (loan.Status == (int)Status.Disbursed)
                {
                    loan.Status = (int)Status.Endorsed;
                    loan.Posted = "Active";
                }

                loan.Aamount = loanbal.Balance;

                if (string.IsNullOrEmpty(loan.TransactionNo))
                {
                    loan.TransactionNo = transactionNo;
                }

                await _context.SaveChangesAsync();

                // 12. CREATE GL TRANSACTION
                Gltransaction glTransaction = null;

                glTransaction = new Gltransaction
                {
                    TransDate = repaymentDto.PaymentDate,
                    Amount = repaymentDto.AmountPaid,
                    DrAccNo = repaymentDto.GlAccountNo,
                    CrAccNo = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT",
                    Temp = "REPAYMENT",
                    DocumentNo = receiptNo,
                    Source = "LOAN_REPAYMENT",
                    CompanyCode = repaymentDto.CompanyCode,
                    TransDescript = $"Loan repayment #{paymentNo} - {loan.LoanNo} - {(isEarlyFullSettlement ? "EARLY FULL SETTLEMENT" : $"Installment {currentSchedule.InstallmentNo}")}",
                    AuditTime = DateTime.Now,
                    AuditId = repaymentDto.ReceivedBy,
                    Cash = 0,
                    DocPosted = 1,
                    ChequeNo = repaymentDto.ReferenceNo,
                    Dregard = false,
                    Recon = false,
                    TransactionNo = transactionNo,
                    Module = "LOAN",
                    ReconId = 0,
                    AuditDateTime = DateTime.Now
                };

                _context.Gltransactions.Add(glTransaction);
                await _context.SaveChangesAsync();

                // 13. CREATE BLOCK AND BLOCKCHAIN TRANSACTION
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = await GetLastBlockHashAsync(),
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                var blockchainData = new
                {
                    TransactionType = "LOAN_REPAYMENT",
                    LoanNo = repaymentDto.LoanNo,
                    MemberNo = repaymentDto.MemberNo,
                    ReceiptNo = receiptNo,
                    PaymentNo = paymentNo,
                    Amount = repaymentDto.AmountPaid,
                    InstallmentNo = currentSchedule.InstallmentNo,
                    PenaltyAllocated = penaltyAllocated,
                    InterestAllocated = interestAllocated,
                    PrincipalAllocated = principalAllocated,
                    Overpayment = overpaymentAmount,
                    BalanceAfter = loanbal.Balance,
                    InterestAfter = loanbal.IntrOwed,
                    PaymentDate = repaymentDto.PaymentDate,
                    IsEarlyFullSettlement = isEarlyFullSettlement,
                    BlockHash = blockHash
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_REPAYMENT",
                    MemberNo = repaymentDto.MemberNo,
                    CompanyCode = repaymentDto.CompanyCode,
                    Amount = repaymentDto.AmountPaid,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = receiptNo,
                    Status = "CONFIRMED",
                    BlockHash = blockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Update records with BlockchainTxId
                repayment.BlockchainTxId = blockchainTx.TransactionId;
                loan.BlockchainTxId = blockchainTx.TransactionId;
                loanbal.BlockchainTxId = blockchainTx.TransactionId;
                glTransaction.BlockchainTxId = blockchainTx.TransactionId;
                foreach (var schedule in remainingSchedules.Where(s => s.Status == "Paid"))
                {
                    schedule.BlockchainTxId = blockchainTx.TransactionId;
                }
                await _context.SaveChangesAsync();

                // ============================================================
                // SAVE AUDIT TRAIL FOR REPAYMENT
                // ============================================================

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == repaymentDto.MemberNo && m.CompanyCode == repaymentDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : repaymentDto.MemberNo;

                // Audit Extra Data
                var auditExtraData = new
                {
                    loanNo = repaymentDto.LoanNo,
                    memberNo = repaymentDto.MemberNo,
                    memberName = memberName,
                    receiptNo = receiptNo,
                    paymentNo = paymentNo,
                    amountPaid = repaymentDto.AmountPaid,
                    paymentDate = repaymentDto.PaymentDate,
                    referenceNo = repaymentDto.ReferenceNo ?? "",
                    glAccountNo = repaymentDto.GlAccountNo,
                    remarks = repaymentDto.Remarks ?? "",
                    principalAllocated = principalAllocated,
                    interestAllocated = interestAllocated,
                    penaltyAllocated = penaltyAllocated,
                    overpaymentAmount = overpaymentAmount,
                    isEarlyFullSettlement = isEarlyFullSettlement,
                    totalFullBalanceBefore = totalFullBalance,
                    installmentNo = currentSchedule.InstallmentNo,
                    dueDate = currentSchedule.DueDate,
                    daysOverdue = repaymentDto.PaymentDate > currentSchedule.DueDate ? (repaymentDto.PaymentDate - currentSchedule.DueDate).Days : 0,
                    receivedBy = repaymentDto.ReceivedBy,
                    processedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanAmountBefore = oldLoanAamount,
                    loanAmountAfter = loan.Aamount,
                    balanceBefore = oldBalance,
                    balanceAfter = loanbal.Balance,
                    interestOwedBefore = oldIntrOwed,
                    interestOwedAfter = loanbal.IntrOwed,
                    penaltyBefore = oldPenalty,
                    penaltyAfter = loanbal.Penalty,
                    scheduleStatusBefore = oldScheduleStatus,
                    scheduleStatusAfter = currentSchedule.Status,
                    isLoanFullyPaid = isFullyPaid,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Repayment Record for Audit (NewValue)
                var repaymentForAudit = new
                {
                    repayment.Id,
                    repayment.LoanNo,
                    repayment.MemberNo,
                    repayment.ReceiptNo,
                    repayment.PaymentNo,
                    repayment.DateReceived,
                    repayment.Amount,
                    repayment.Principal,
                    repayment.Interest,
                    repayment.Penalty,
                    repayment.LoanBalance,
                    repayment.IntrOwed,
                    repayment.Remarks,
                    repayment.Transby,
                    repayment.TransactionNo,
                    repayment.ApiKey,
                    CreatedBy = repaymentDto.ReceivedBy,
                    CreatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                // Save Audit for Repayment (INSERT)
                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: repaymentForAudit,
                    tableName: "Repay",
                    recordId: receiptNo,
                    userId: repaymentDto.ReceivedBy,
                    userName: repaymentDto.ReceivedBy,
                    companyCode: repaymentDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOANBAL UPDATE
                // ============================================================
                var loanbalAuditExtraData = new
                {
                    loanNo = repaymentDto.LoanNo,
                    balanceBefore = oldBalance,
                    balanceAfter = loanbal.Balance,
                    principalReduction = principalAllocated,
                    interestOwedBefore = oldIntrOwed,
                    interestOwedAfter = loanbal.IntrOwed,
                    interestReduction = interestAllocated,
                    penaltyBefore = oldPenalty,
                    penaltyAfter = loanbal.Penalty,
                    penaltyReduction = penaltyAllocated,
                    nextDueDate = loanbal.Nextduedate,
                    isCleared = loanbal.Cleared,
                    isFullyPaid = isFullyPaid,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanbalForAudit = new
                {
                    loanbal.LoanNo,
                    loanbal.Balance,
                    loanbal.IntrOwed,
                    loanbal.Penalty,
                    loanbal.LastDate,
                    loanbal.Nextduedate,
                    loanbal.Cleared,
                    loanbal.Processdate,
                    UpdatedBy = repaymentDto.ReceivedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new { Balance = oldBalance, IntrOwed = oldIntrOwed, Penalty = oldPenalty },
                    newModel: loanbalForAudit,
                    tableName: "Loanbal",
                    recordId: loanbal.Id.ToString(),
                    userId: repaymentDto.ReceivedBy,
                    userName: repaymentDto.ReceivedBy,
                    companyCode: repaymentDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanbalAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE (if changed)
                // ============================================================
                if (oldLoanStatus != loan.Status)
                {
                    var loanAuditExtraData = new
                    {
                        loanNo = repaymentDto.LoanNo,
                        statusChangedFrom = oldLoanStatus,
                        statusChangedTo = loan.Status,
                        reason = isFullyPaid ? "Loan fully paid and closed" : "Loan status updated after repayment",
                        paymentNo = paymentNo,
                        receiptNo = receiptNo,
                        amountPaid = repaymentDto.AmountPaid,
                        balanceAfter = loanbal.Balance,
                        interestAfter = loanbal.IntrOwed,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var loanForAudit = new
                    {
                        loan.LoanNo,
                        loan.Status,
                        loan.Posted,
                        loan.Aamount,
                        loan.UserName,
                        loan.AuditDateTime,
                        UpdatedBy = repaymentDto.ReceivedBy,
                        UpdateReason = isFullyPaid ? "Loan fully paid" : "Status updated after repayment",
                        BlockchainTxId = blockchainTx.TransactionId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Update,
                        oldModel: new { Status = oldLoanStatus, Posted = oldLoanPosted, Aamount = oldLoanAamount },
                        newModel: loanForAudit,
                        tableName: "Loans",
                        recordId: repaymentDto.LoanNo,
                        userId: repaymentDto.ReceivedBy,
                        userName: repaymentDto.ReceivedBy,
                        companyCode: repaymentDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );
                }

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN SCHEDULE UPDATE
                // ============================================================
                var scheduleAuditExtraData = new
                {
                    loanNo = repaymentDto.LoanNo,
                    installmentNo = currentSchedule.InstallmentNo,
                    dueDate = currentSchedule.DueDate,
                    principalAmount = currentSchedule.PrincipalAmount,
                    interestAmount = currentSchedule.InterestAmount,
                    totalInstallment = currentSchedule.TotalInstallment,
                    statusBefore = oldScheduleStatus,
                    statusAfter = currentSchedule.Status,
                    outstandingPrincipalBefore = oldScheduleOutstandingPrincipal,
                    outstandingPrincipalAfter = currentSchedule.OutstandingPrincipal,
                    outstandingInterestBefore = oldScheduleOutstandingInterest,
                    outstandingInterestAfter = currentSchedule.OutstandingInterest,
                    outstandingTotalBefore = oldScheduleOutstandingTotal,
                    outstandingTotalAfter = currentSchedule.OutstandingTotal,
                    paidPrincipal = currentSchedule.PaidPrincipal,
                    paidInterest = currentSchedule.PaidInterest,
                    penaltyAmount = currentSchedule.PenaltyAmount,
                    paidDate = currentSchedule.PaidDate,
                    isEarlyFullSettlement = isEarlyFullSettlement,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var scheduleForAudit = new
                {
                    currentSchedule.Id,
                    currentSchedule.LoanNo,
                    currentSchedule.InstallmentNo,
                    currentSchedule.Status,
                    currentSchedule.OutstandingPrincipal,
                    currentSchedule.OutstandingInterest,
                    currentSchedule.OutstandingTotal,
                    currentSchedule.PaidPrincipal,
                    currentSchedule.PaidInterest,
                    currentSchedule.PaidTotal,
                    currentSchedule.PaidDate,
                    currentSchedule.PenaltyAmount,
                    UpdatedBy = repaymentDto.ReceivedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new
                    {
                        Status = oldScheduleStatus,
                        OutstandingPrincipal = oldScheduleOutstandingPrincipal,
                        OutstandingInterest = oldScheduleOutstandingInterest,
                        OutstandingTotal = oldScheduleOutstandingTotal
                    },
                    newModel: scheduleForAudit,
                    tableName: "LoanSchedules",
                    recordId: currentSchedule.Id.ToString(),
                    userId: repaymentDto.ReceivedBy,
                    userName: repaymentDto.ReceivedBy,
                    companyCode: repaymentDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(scheduleAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR GL TRANSACTION
                // ============================================================
                if (glTransaction != null)
                {
                    var glAuditExtraData = new
                    {
                        loanNo = repaymentDto.LoanNo,
                        receiptNo = receiptNo,
                        paymentNo = paymentNo,
                        amount = repaymentDto.AmountPaid,
                        drAccount = repaymentDto.GlAccountNo,
                        crAccount = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT",
                        transactionType = "LOAN_REPAYMENT",
                        isEarlyFullSettlement = isEarlyFullSettlement,
                        installmentNo = currentSchedule.InstallmentNo,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var glForAudit = new
                    {
                        glTransaction.Id,
                        glTransaction.TransDate,
                        glTransaction.Amount,
                        glTransaction.DrAccNo,
                        glTransaction.CrAccNo,
                        glTransaction.DocumentNo,
                        glTransaction.Source,
                        glTransaction.TransDescript,
                        glTransaction.ChequeNo,
                        glTransaction.TransactionNo,
                        CreatedBy = repaymentDto.ReceivedBy,
                        CreatedDate = DateTime.Now,
                        BlockchainTxId = blockchainTx.TransactionId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: glForAudit,
                        tableName: "Gltransactions",
                        recordId: glTransaction.Id.ToString(),
                        userId: repaymentDto.ReceivedBy,
                        userName: repaymentDto.ReceivedBy,
                        companyCode: repaymentDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(glAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );
                }

                _logger.LogInformation($"Repayment audit completed for loan {repaymentDto.LoanNo}, Receipt: {receiptNo}");

                await transaction.CommitAsync();

                _logger.LogInformation($"Repayment #{paymentNo} - {receiptNo} processed successfully. " +
                    $"Principal: {principalAllocated:C}, Interest: {interestAllocated:C}, Penalty: {penaltyAllocated:C}, " +
                    $"New Balance: {loanbal.Balance:C}, New Interest Owed: {loanbal.IntrOwed:C}, " +
                    $"IsFullSettlement: {isEarlyFullSettlement}");

                return repayment;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error processing repayment for loan {repaymentDto.LoanNo}");
                throw;
            }
        }

        //public async Task<Repay> ProcessRepaymentAsync(LoanRepaymentDTO repaymentDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Processing repayment for loan {repaymentDto.LoanNo}, Amount: {repaymentDto.AmountPaid:C}");

        //        // 1. GET LOAN DATA
        //        var loan = await _context.Loans
        //            .FirstOrDefaultAsync(l => l.LoanNo == repaymentDto.LoanNo && l.CompanyCode == repaymentDto.CompanyCode);

        //        if (loan == null)
        //            throw new InvalidOperationException($"Loan {repaymentDto.LoanNo} not found");

        //        if (loan.Status != (int)Status.Disbursed && loan.Status != (int)Status.Endorsed)
        //            throw new InvalidOperationException($"Cannot process repayment for loan in status '{loan.Status}'");

        //        // 2. GET LOAN BALANCE
        //        var loanbal = await _context.Loanbal
        //            .FirstOrDefaultAsync(lb => lb.LoanNo == repaymentDto.LoanNo && lb.Companycode == repaymentDto.CompanyCode);

        //        if (loanbal == null)
        //            throw new InvalidOperationException($"Loan balance record not found");

        //        // 3. GET ALL REMAINING SCHEDULES
        //        var remainingSchedules = await _context.LoanSchedules
        //            .Where(s => s.LoanNo == repaymentDto.LoanNo && s.Status != "Paid")
        //            .OrderBy(s => s.InstallmentNo)
        //            .ToListAsync();

        //        if (!remainingSchedules.Any())
        //        {
        //            if (loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m)
        //            {
        //                throw new InvalidOperationException("Loan is already fully paid.");
        //            }
        //            throw new InvalidOperationException("No active schedule found for this loan.");
        //        }

        //        // Get the current schedule (first unpaid)
        //        var currentSchedule = remainingSchedules.First();

        //        _logger.LogInformation($"Current Schedule - Installment {currentSchedule.InstallmentNo}, " +
        //            $"Outstanding Principal: {currentSchedule.OutstandingPrincipal:C}, " +
        //            $"Outstanding Interest: {currentSchedule.OutstandingInterest:C}");

        //        // 4. GET LOAN TYPE FOR PENALTY SETTINGS
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == repaymentDto.CompanyCode);

        //        // 5. CALCULATE TOTAL REMAINING BALANCE (for early settlement detection)
        //        decimal totalRemainingPrincipal = loanbal.Balance;
        //        decimal totalRemainingInterest = loanbal.IntrOwed;
        //        decimal totalRemainingPenalty = loanbal.Penalty;
        //        decimal totalFullBalance = totalRemainingPrincipal + totalRemainingInterest + totalRemainingPenalty;

        //        // 6. CHECK IF THIS IS AN EARLY FULL SETTLEMENT
        //        bool isEarlyFullSettlement = repaymentDto.AmountPaid >= totalFullBalance - 0.01m;

        //        decimal penaltyAllocated = 0;
        //        decimal interestAllocated = 0;
        //        decimal principalAllocated = 0;
        //        decimal overpaymentAmount = 0;

        //        if (isEarlyFullSettlement)
        //        {
        //            _logger.LogInformation($"EARLY FULL SETTLEMENT detected! Amount: {repaymentDto.AmountPaid:C}, Total Due: {totalFullBalance:C}");

        //            // ALLOCATE FULL BALANCE
        //            penaltyAllocated = totalRemainingPenalty;
        //            interestAllocated = totalRemainingInterest;
        //            principalAllocated = totalRemainingPrincipal;
        //            overpaymentAmount = repaymentDto.AmountPaid - totalFullBalance;

        //            _logger.LogInformation($"Full Settlement Allocation: Principal={principalAllocated:C}, Interest={interestAllocated:C}, Penalty={penaltyAllocated:C}, Overpayment={overpaymentAmount:C}");
        //        }
        //        else
        //        {
        //            // Regular installment payment - calculate penalty if overdue
        //            decimal penaltyAmount = 0;
        //            int daysOverdue = 0;

        //            if (loanType != null && loanType.Penalty == 1 && repaymentDto.PaymentDate > currentSchedule.DueDate)
        //            {
        //                daysOverdue = (repaymentDto.PaymentDate - currentSchedule.DueDate).Days;
        //                int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 30;

        //                if (daysOverdue > gracePeriodDays)
        //                {
        //                    int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
        //                    int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);
        //                    decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
        //                    penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
        //                    _logger.LogInformation($"Penalty calculated: {penaltyAmount:C}");
        //                }
        //            }

        //            decimal remainingAmount = repaymentDto.AmountPaid;

        //            // Apply to penalty first
        //            if (remainingAmount > 0 && penaltyAmount > 0)
        //            {
        //                penaltyAllocated = Math.Min(remainingAmount, penaltyAmount);
        //                remainingAmount -= penaltyAllocated;
        //            }

        //            // Apply to interest (current schedule)
        //            if (remainingAmount > 0 && currentSchedule.OutstandingInterest > 0)
        //            {
        //                interestAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingInterest);
        //                remainingAmount -= interestAllocated;
        //            }

        //            // Apply to principal (current schedule)
        //            if (remainingAmount > 0 && currentSchedule.OutstandingPrincipal > 0)
        //            {
        //                principalAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingPrincipal);
        //                remainingAmount -= principalAllocated;
        //            }

        //            overpaymentAmount = remainingAmount;
        //        }

        //        // 7. GENERATE NUMBERS
        //        string receiptNo = await GenerateReceiptNumberAsync(repaymentDto.CompanyCode);
        //        string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
        //        int repaymentCount = await _context.Repay.CountAsync(r => r.LoanNo == repaymentDto.LoanNo && r.Posted == true);
        //        int paymentNo = repaymentCount + 1;

        //        // 8. CREATE REPAY RECORD
        //        var repayment = new Repay
        //        {
        //            LoanNo = repaymentDto.LoanNo,
        //            MemberNo = repaymentDto.MemberNo,
        //            CompanyCode = repaymentDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            PaymentNo = paymentNo,
        //            DateReceived = repaymentDto.PaymentDate,
        //            Amount = repaymentDto.AmountPaid,
        //            Principal = principalAllocated,
        //            Interest = interestAllocated,
        //            Penalty = penaltyAllocated,
        //            IntrCharged = interestAllocated,
        //            IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated),
        //            IntrAccrued = currentSchedule.InterestAmount,
        //            LoanBalance = Math.Max(0, loanbal.Balance - principalAllocated),
        //            RepayRate = currentSchedule.TotalInstallment,
        //            Locked = false,
        //            Posted = true,
        //            Accrued = true,
        //            Remarks = repaymentDto.Remarks + (overpaymentAmount > 0 ? $" (Overpayment: KES {overpaymentAmount:N2})" : "") + (isEarlyFullSettlement ? " - EARLY FULL SETTLEMENT" : ""),
        //            AuditId = repaymentDto.ReceivedBy,
        //            AuditTime = DateTime.Now,
        //            Transby = repaymentDto.ReceivedBy,
        //            IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated),
        //            Loancode = loan.LoanCode,
        //            Interestaccrued = currentSchedule.InterestAmount,
        //            Transno = transactionNo,
        //            TransDate = repaymentDto.PaymentDate,
        //            TransactionNo = transactionNo,
        //            ApiKey = repaymentDto.ReferenceNo,
        //            UserName = repaymentDto.ReceivedBy,
        //            Run = 0,
        //            AuditDateTime = DateTime.Now,
        //            BlockchainTxId = null
        //        };

        //        _context.Repay.Add(repayment);
        //        await _context.SaveChangesAsync();

        //        // 9. UPDATE ALL REMAINING SCHEDULES
        //        if (isEarlyFullSettlement)
        //        {
        //            // Mark ALL remaining schedules as PAID
        //            foreach (var schedule in remainingSchedules)
        //            {
        //                schedule.PaidPrincipal = schedule.PrincipalAmount;
        //                schedule.PaidInterest = schedule.InterestAmount;
        //                schedule.PaidTotal = schedule.TotalInstallment;
        //                schedule.OutstandingPrincipal = 0;
        //                schedule.OutstandingInterest = 0;
        //                schedule.OutstandingTotal = 0;
        //                schedule.Status = "Paid";
        //                schedule.PaidDate = repaymentDto.PaymentDate;
        //                schedule.PenaltyAmount = schedule.PenaltyAmount + (schedule == currentSchedule ? penaltyAllocated : 0);

        //                _logger.LogInformation($"Schedule {schedule.InstallmentNo} marked as PAID (early settlement)");
        //            }
        //        }
        //        else
        //        {
        //            // Update only the current schedule (regular payment)
        //            bool isCurrentInstallmentFullyPaid = (principalAllocated >= currentSchedule.OutstandingPrincipal - 0.01m) &&
        //                                                  (interestAllocated >= currentSchedule.OutstandingInterest - 0.01m);

        //            if (isCurrentInstallmentFullyPaid)
        //            {
        //                currentSchedule.PaidPrincipal = currentSchedule.PrincipalAmount;
        //                currentSchedule.PaidInterest = currentSchedule.InterestAmount;
        //                currentSchedule.PaidTotal = currentSchedule.TotalInstallment;
        //                currentSchedule.OutstandingPrincipal = 0;
        //                currentSchedule.OutstandingInterest = 0;
        //                currentSchedule.OutstandingTotal = 0;
        //                currentSchedule.Status = "Paid";
        //                currentSchedule.PaidDate = repaymentDto.PaymentDate;
        //                currentSchedule.PenaltyAmount = currentSchedule.PenaltyAmount + penaltyAllocated;

        //                _logger.LogInformation($"Installment {currentSchedule.InstallmentNo} marked as PAID");
        //            }
        //            else
        //            {
        //                // Partial payment - update the current schedule
        //                currentSchedule.PaidPrincipal += principalAllocated;
        //                currentSchedule.PaidInterest += interestAllocated;
        //                currentSchedule.PaidTotal = currentSchedule.PaidPrincipal + currentSchedule.PaidInterest;
        //                currentSchedule.OutstandingPrincipal = currentSchedule.PrincipalAmount - currentSchedule.PaidPrincipal;
        //                currentSchedule.OutstandingInterest = currentSchedule.InterestAmount - currentSchedule.PaidInterest;
        //                currentSchedule.OutstandingTotal = currentSchedule.OutstandingPrincipal + currentSchedule.OutstandingInterest;
        //                currentSchedule.Status = "Partial";
        //                currentSchedule.PenaltyAmount = currentSchedule.PenaltyAmount + penaltyAllocated;

        //                _logger.LogInformation($"Installment {currentSchedule.InstallmentNo} marked as PARTIAL");
        //            }
        //        }

        //        // 10. UPDATE LOANBAL RECORD
        //        loanbal.Balance = Math.Max(0, loanbal.Balance - principalAllocated);
        //        loanbal.IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated);
        //        loanbal.Penalty = Math.Max(0, loanbal.Penalty - penaltyAllocated);
        //        loanbal.IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated);
        //        loanbal.LastDate = repaymentDto.PaymentDate;
        //        loanbal.Processdate = DateTime.Now;

        //        // Update next due date
        //        if (!isEarlyFullSettlement)
        //        {
        //            var nextSchedule = await _context.LoanSchedules
        //                .Where(s => s.LoanNo == repaymentDto.LoanNo && s.Status != "Paid")
        //                .OrderBy(s => s.InstallmentNo)
        //                .FirstOrDefaultAsync();

        //            if (nextSchedule != null)
        //            {
        //                loanbal.Nextduedate = nextSchedule.DueDate;
        //                loanbal.Duedate = nextSchedule.DueDate;
        //                loanbal.RepayRate = nextSchedule.TotalInstallment;
        //            }
        //            else if (loanbal.Balance > 0.01m || loanbal.IntrOwed > 0.01m)
        //            {
        //                loanbal.Nextduedate = repaymentDto.PaymentDate.AddMonths(1);
        //                loanbal.Duedate = repaymentDto.PaymentDate.AddMonths(1);
        //            }
        //        }

        //        // 11. CHECK IF LOAN IS FULLY PAID
        //        bool isFullyPaid = loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m;

        //        if (isFullyPaid)
        //        {
        //            loanbal.Cleared = true;
        //            loan.Status = (int)Status.Closed;
        //            loan.Posted = "Closed";
        //            loan.Aamount = 0;

        //            await ReleaseCollateralGuaranteesForLoanAsync(repaymentDto.LoanNo, repaymentDto.ReceivedBy);

        //            _logger.LogInformation($"Loan {loan.LoanNo} fully paid and closed");
        //        }
        //        else if (loan.Status == (int)Status.Disbursed)
        //        {
        //            loan.Status = (int)Status.Endorsed;
        //            loan.Posted = "Active";
        //        }

        //        loan.Aamount = loanbal.Balance;

        //        if (string.IsNullOrEmpty(loan.TransactionNo))
        //        {
        //            loan.TransactionNo = transactionNo;
        //        }

        //        await _context.SaveChangesAsync();

        //        // 12. CREATE GL TRANSACTION
        //        Gltransaction glTransaction = null;

        //        glTransaction = new Gltransaction
        //        {
        //            TransDate = repaymentDto.PaymentDate,
        //            Amount = repaymentDto.AmountPaid,
        //            DrAccNo = repaymentDto.GlAccountNo,
        //            CrAccNo = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT",
        //            Temp = "REPAYMENT",
        //            DocumentNo = receiptNo,
        //            Source = "LOAN_REPAYMENT",
        //            CompanyCode = repaymentDto.CompanyCode,
        //            TransDescript = $"Loan repayment #{paymentNo} - {loan.LoanNo} - {(isEarlyFullSettlement ? "EARLY FULL SETTLEMENT" : $"Installment {currentSchedule.InstallmentNo}")}",
        //            AuditTime = DateTime.Now,
        //            AuditId = repaymentDto.ReceivedBy,
        //            Cash = 0,
        //            DocPosted = 1,
        //            ChequeNo = repaymentDto.ReferenceNo,
        //            Dregard = false,
        //            Recon = false,
        //            TransactionNo = transactionNo,
        //            Module = "LOAN",
        //            ReconId = 0,
        //            AuditDateTime = DateTime.Now
        //        };

        //        _context.Gltransactions.Add(glTransaction);
        //        await _context.SaveChangesAsync();

        //        // 13. CREATE BLOCK AND BLOCKCHAIN TRANSACTION
        //        string blockHash = Guid.NewGuid().ToString().Replace("-", "");
        //        if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
        //        else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

        //        var block = new Block
        //        {
        //            BlockHash = blockHash,
        //            PreviousHash = await GetLastBlockHashAsync(),
        //            Timestamp = DateTime.Now,
        //            Nonce = 0,
        //            MerkleRoot = Guid.NewGuid().ToString(),
        //            Confirmed = true,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.Blocks.Add(block);
        //        await _context.SaveChangesAsync();

        //        var blockchainData = new
        //        {
        //            TransactionType = "LOAN_REPAYMENT",
        //            LoanNo = repaymentDto.LoanNo,
        //            MemberNo = repaymentDto.MemberNo,
        //            ReceiptNo = receiptNo,
        //            PaymentNo = paymentNo,
        //            Amount = repaymentDto.AmountPaid,
        //            InstallmentNo = currentSchedule.InstallmentNo,
        //            PenaltyAllocated = penaltyAllocated,
        //            InterestAllocated = interestAllocated,
        //            PrincipalAllocated = principalAllocated,
        //            Overpayment = overpaymentAmount,
        //            BalanceAfter = loanbal.Balance,
        //            InterestAfter = loanbal.IntrOwed,
        //            PaymentDate = repaymentDto.PaymentDate,
        //            IsEarlyFullSettlement = isEarlyFullSettlement,
        //            BlockHash = blockHash
        //        };

        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "LOAN_REPAYMENT",
        //            MemberNo = repaymentDto.MemberNo,
        //            CompanyCode = repaymentDto.CompanyCode,
        //            Amount = repaymentDto.AmountPaid,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = receiptNo,
        //            Status = "CONFIRMED",
        //            BlockHash = blockHash,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        // Update records with BlockchainTxId
        //        repayment.BlockchainTxId = blockchainTx.TransactionId;
        //        loan.BlockchainTxId = blockchainTx.TransactionId;
        //        loanbal.BlockchainTxId = blockchainTx.TransactionId;
        //        glTransaction.BlockchainTxId = blockchainTx.TransactionId; 
        //        foreach (var schedule in remainingSchedules.Where(s => s.Status == "Paid"))
        //        {
        //            schedule.BlockchainTxId = blockchainTx.TransactionId;
        //        }
        //        await _context.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"Repayment #{paymentNo} - {receiptNo} processed successfully. " +
        //            $"Principal: {principalAllocated:C}, Interest: {interestAllocated:C}, Penalty: {penaltyAllocated:C}, " +
        //            $"New Balance: {loanbal.Balance:C}, New Interest Owed: {loanbal.IntrOwed:C}, " +
        //            $"IsFullSettlement: {isEarlyFullSettlement}");

        //        return repayment;
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error processing repayment for loan {repaymentDto.LoanNo}");
        //        throw;
        //    }
        //}

        private async Task UpdateLoanSchedulesAfterRepaymentAsync(string loanNo, decimal amountPaid, DateTime paymentDate, string repaymentMethod)
        {
            var schedules = await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                .OrderBy(s => s.InstallmentNo)
                .ToListAsync();

            if (!schedules.Any())
            {
                _logger.LogWarning($"No schedules found for loan {loanNo}");
                return;
            }

            decimal remainingAmount = amountPaid;

            foreach (var schedule in schedules)
            {
                if (remainingAmount <= 0) break;

                decimal scheduleOutstanding = schedule.OutstandingPrincipal + schedule.OutstandingInterest;

                if (remainingAmount >= scheduleOutstanding)
                {
                    // Fully pay this schedule
                    schedule.PaidPrincipal = schedule.PrincipalAmount;
                    schedule.PaidInterest = schedule.InterestAmount;
                    schedule.PaidTotal = schedule.TotalInstallment;
                    schedule.OutstandingPrincipal = 0;
                    schedule.OutstandingInterest = 0;
                    schedule.OutstandingTotal = 0;
                    schedule.Status = "Paid";
                    schedule.PaidDate = paymentDate;
                    remainingAmount -= scheduleOutstanding;
                    _logger.LogInformation($"Schedule {schedule.InstallmentNo} fully paid");
                }
                else
                {
                    // Partial payment
                    if (remainingAmount <= schedule.OutstandingInterest)
                    {
                        schedule.PaidInterest += remainingAmount;
                        schedule.OutstandingInterest = schedule.InterestAmount - schedule.PaidInterest;
                        schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
                        remainingAmount = 0;
                    }
                    else
                    {
                        schedule.PaidInterest = schedule.InterestAmount;
                        schedule.OutstandingInterest = 0;
                        remainingAmount -= schedule.OutstandingInterest;

                        schedule.PaidPrincipal += remainingAmount;
                        schedule.OutstandingPrincipal = schedule.PrincipalAmount - schedule.PaidPrincipal;
                        schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
                        remainingAmount = 0;
                    }

                    schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
                    schedule.Status = "Partial";
                    _logger.LogInformation($"Schedule {schedule.InstallmentNo} partially paid");
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task<string> GenerateReceiptNumberAsync(string companyCode)
        {
            var prefix = "RCPT";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastReceipt = await _context.Repay
                .Where(r => r.CompanyCode == companyCode && r.ReceiptNo != null && r.ReceiptNo.StartsWith($"{prefix}{date}"))
                .OrderByDescending(r => r.ReceiptNo)
                .Select(r => r.ReceiptNo)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(lastReceipt) && lastReceipt.Length > 11)
            {
                var seqStr = lastReceipt.Substring(11);
                if (int.TryParse(seqStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        public async Task<List<Repay>> GetLoanRepaymentsAsync(string loanNo)
        {
            return await _context.Repay
                .Where(r => r.LoanNo == loanNo)
                .OrderByDescending(r => r.AuditTime)
                .ToListAsync();
        }

        public async Task<Repay> ReverseRepaymentAsync(int repaymentId, string reason, string reversedBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var repayment = await _context.Repay
                    .FirstOrDefaultAsync(r => r.Id == repaymentId);

                if (repayment == null)
                {
                    throw new InvalidOperationException("Repayment record not found");
                }

                if (repayment.Posted == false)
                {
                    throw new InvalidOperationException("Repayment has already been reversed");
                }

                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == repayment.LoanNo && l.CompanyCode == repayment.CompanyCode);

                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == repayment.LoanNo && lb.Companycode == repayment.CompanyCode);

                // Reverse the allocations
                decimal principalToAddBack = repayment.Principal ?? 0;
                decimal interestToAddBack = repayment.Interest ?? 0;
                decimal penaltyToAddBack = repayment.Penalty ?? 0;

                if (loanbal != null)
                {
                    loanbal.Balance += principalToAddBack;
                    loanbal.IntrOwed += interestToAddBack;
                    loanbal.Penalty += penaltyToAddBack;
                    loanbal.IntBalance += interestToAddBack;
                    loanbal.LastDate = repayment.AuditDateTime ?? DateTime.Now;
                    loanbal.Processdate = DateTime.Now;
                }

                if (loan != null && loan.Status == (int)Status.Closed)
                {
                    loan.Status = (int)Status.Disbursed;
                    loan.Posted = "Active";
                }

                // Reverse schedule allocations
                var schedules = await _context.LoanSchedules
                    .Where(s => s.LoanNo == repayment.LoanNo)
                    .OrderByDescending(s => s.InstallmentNo)
                    .ToListAsync();

                decimal remainingPrincipal = principalToAddBack;
                decimal remainingInterest = interestToAddBack;

                foreach (var schedule in schedules)
                {
                    if (remainingPrincipal <= 0 && remainingInterest <= 0) break;

                    if (schedule.Status == "Paid" || schedule.Status == "Partial")
                    {
                        if (remainingPrincipal > 0 && schedule.PaidPrincipal > 0)
                        {
                            decimal principalToReverse = Math.Min(remainingPrincipal, schedule.PaidPrincipal);
                            schedule.PaidPrincipal -= principalToReverse;
                            schedule.OutstandingPrincipal += principalToReverse;
                            remainingPrincipal -= principalToReverse;
                        }

                        if (remainingInterest > 0 && schedule.PaidInterest > 0)
                        {
                            decimal interestToReverse = Math.Min(remainingInterest, schedule.PaidInterest);
                            schedule.PaidInterest -= interestToReverse;
                            schedule.OutstandingInterest += interestToReverse;
                            remainingInterest -= interestToReverse;
                        }

                        schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
                        schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;

                        if (schedule.PaidPrincipal <= 0 && schedule.PaidInterest <= 0)
                        {
                            schedule.Status = "Pending";
                            schedule.PaidDate = null;
                        }
                        else if (schedule.PaidPrincipal < schedule.PrincipalAmount || schedule.PaidInterest < schedule.InterestAmount)
                        {
                            schedule.Status = "Partial";
                        }
                    }
                }

                // Mark repayment as reversed
                repayment.Posted = false;
                repayment.Remarks = $"Reversed: {reason}";
                repayment.AuditDateTime = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain reversal
                var reversalData = new
                {
                    RepaymentId = repaymentId,
                    LoanNo = repayment.LoanNo,
                    ReceiptNo = repayment.ReceiptNo,
                    AmountReversed = repayment.Amount,
                    PrincipalReversed = principalToAddBack,
                    InterestReversed = interestToAddBack,
                    PenaltyReversed = penaltyToAddBack,
                    Reason = reason,
                    ReversedBy = reversedBy,
                    ReversalDate = DateTime.Now
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_REPAYMENT_REVERSED",
                    MemberNo = repayment.MemberNo,
                    CompanyCode = repayment.CompanyCode,
                    Amount = repayment.Amount ?? 0,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(reversalData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(reversalData),
                    OffChainReferenceId = repayment.ReceiptNo,
                    Status = "CONFIRMED",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                repayment.BlockchainTxId = blockchainTx.TransactionId;
                if (loanbal != null) loanbal.BlockchainTxId = blockchainTx.TransactionId;
                if (loan != null) loan.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Repayment {repayment.ReceiptNo} reversed successfully");

                return repayment;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error reversing repayment ID {repaymentId}");
                throw;
            }
        }
        private async Task UpdateLoanSchedulesAfterRepaymentAsync(string loanNo, decimal amountPaid, DateTime paymentDate)
        {
            var schedules = await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                .OrderBy(s => s.InstallmentNo)
                .ToListAsync();

            if (!schedules.Any()) return;

            decimal remainingAmount = amountPaid;

            foreach (var schedule in schedules)
            {
                if (remainingAmount <= 0) break;

                decimal scheduleOutstanding = schedule.OutstandingPrincipal + schedule.OutstandingInterest;

                if (remainingAmount >= scheduleOutstanding)
                {
                    // Fully pay this schedule
                    schedule.PaidPrincipal = schedule.PrincipalAmount;
                    schedule.PaidInterest = schedule.InterestAmount;
                    schedule.PaidTotal = schedule.TotalInstallment;
                    schedule.OutstandingPrincipal = 0;
                    schedule.OutstandingInterest = 0;
                    schedule.OutstandingTotal = 0;
                    schedule.Status = "Paid";
                    schedule.PaidDate = paymentDate;
                    remainingAmount -= scheduleOutstanding;

                    _logger.LogInformation($"Schedule {schedule.InstallmentNo} fully paid");
                }
                else
                {
                    // Partial payment
                    if (remainingAmount <= schedule.OutstandingInterest)
                    {
                        schedule.PaidInterest += remainingAmount;
                        schedule.OutstandingInterest = schedule.InterestAmount - schedule.PaidInterest;
                    }
                    else
                    {
                        schedule.PaidInterest = schedule.InterestAmount;
                        schedule.OutstandingInterest = 0;
                        remainingAmount -= schedule.OutstandingInterest;

                        schedule.PaidPrincipal += remainingAmount;
                        schedule.OutstandingPrincipal = schedule.PrincipalAmount - schedule.PaidPrincipal;
                    }

                    schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
                    schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
                    schedule.Status = "Partial";
                    remainingAmount = 0;

                    _logger.LogInformation($"Schedule {schedule.InstallmentNo} partially paid");
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task<LoanSchedule> GetCurrentScheduleAsync(string loanNo)
        {
            return await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                .OrderBy(s => s.InstallmentNo)
                .FirstOrDefaultAsync();
        }

        private async Task UpdateRbalScheduleAsync(string loanNo, decimal principalPaid, DateTime paymentDate)
        {
            var schedules = await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                .OrderBy(s => s.InstallmentNo)
                .ToListAsync();

            if (!schedules.Any()) return;

            decimal remainingPrincipal = principalPaid;

            foreach (var schedule in schedules)
            {
                if (remainingPrincipal <= 0) break;

                decimal principalToAllocate = Math.Min(remainingPrincipal, schedule.OutstandingPrincipal);

                schedule.PaidPrincipal += principalToAllocate;
                schedule.OutstandingPrincipal = schedule.PrincipalAmount - schedule.PaidPrincipal;
                schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
                schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;

                remainingPrincipal -= principalToAllocate;

                if (schedule.OutstandingPrincipal <= 0 && schedule.OutstandingInterest <= 0)
                {
                    schedule.Status = "Paid";
                    schedule.PaidDate = paymentDate;
                }
                else
                {
                    schedule.Status = "Partial";
                }
            }

            await _context.SaveChangesAsync();
        }
        private async Task ReleaseCollateralGuaranteesForLoanAsync(string loanNo, string releasedBy)
        {
            try
            {
                // Get all active collateral guarantees for this loan
                var activeGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .ToListAsync();

                if (!activeGuarantees.Any())
                {
                    _logger.LogInformation($"No active collateral guarantees found for loan {loanNo}");
                    return;
                }

                _logger.LogInformation($"Releasing {activeGuarantees.Count} collateral guarantee(s) for loan {loanNo}");

                foreach (var guarantee in activeGuarantees)
                {
                    var originalBalance = guarantee.Balance;

                    // Release by setting balance to 0
                    guarantee.Balance = 0;
                    guarantee.AuditId = releasedBy;

                    // Record blockchain transaction for release
                    var blockchainData = new
                    {
                        Action = "COLLATERAL_GUARANTEE_AUTO_RELEASE",
                        CollateralGuaranteeId = guarantee.Id,
                        LoanNo = guarantee.LoanNo,
                        MemberNo = guarantee.MemberNo,
                        ColCode = guarantee.ColCode,
                        DocNo = guarantee.DocNo,
                        OriginalBalance = originalBalance,
                        ReleasedBy = releasedBy,
                        Reason = "Loan fully repaid",
                        ReleasedAt = DateTime.Now
                    };

                    var blockchainTx = new BlockchainTransaction
                    {
                        TransactionId = Guid.NewGuid().ToString(),
                        TransactionType = "COLLATERAL_GUARANTEE_AUTO_RELEASE",
                        MemberNo = guarantee.MemberNo,
                        CompanyCode = guarantee.CompanyCode,
                        Amount = originalBalance,
                        Timestamp = DateTime.Now,
                        DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                        PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                        OffChainReferenceId = guarantee.LoanNo,
                        Status = "CONFIRMED",
                        CreatedAt = DateTime.Now
                    };

                    _context.BlockchainTransactions.Add(blockchainTx);
                    guarantee.BlockchainTxId = blockchainTx.TransactionId;

                    _logger.LogInformation($"Released collateral guarantee {guarantee.Id}: {guarantee.ColCode} - Doc: {guarantee.DocNo}, Amount: {originalBalance:C}");
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error releasing collateral guarantees for loan {loanNo}");
                throw;
            }
        }

        #endregion


        #region Schedule Generation
        public async Task<List<LoanSchedule>> GenerateLoanScheduleAsync(string loanNo)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo);

            if (loan == null)
            {
                throw new InvalidOperationException($"Loan {loanNo} not found");
            }

            var loanbal = await _context.Loanbal
                .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo);

            if (loanbal == null)
            {
                throw new InvalidOperationException($"Loan balance record not found for loan {loanNo}");
            }

            // Get the approved amount from Endmain (endorsement)
            var endmain = await _context.Endmain
                .FirstOrDefaultAsync(e => e.LoanNo == loanNo);

            // Principal amount is the APPROVED amount from appraisal
            decimal principalAmount = endmain?.AmtApproved ?? loan.LoanAmt ?? 0;

            if (principalAmount <= 0)
            {
                throw new InvalidOperationException("Cannot generate schedule. No approved amount found for this loan.");
            }

            _logger.LogInformation($"Generating schedule for loan {loanNo} with approved principal: {principalAmount:C}");
            _logger.LogInformation($"Repayment Method: {loan.RepayMethod}, Interest Rate: {loan.Interest}%, Period: {loan.RepayPeriod} months");

            var existingSchedule = await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo)
                .ToListAsync();

            if (existingSchedule.Any())
            {
                _context.LoanSchedules.RemoveRange(existingSchedule);
                await _context.SaveChangesAsync();
            }

            var schedules = new List<LoanSchedule>();
            var annualInterestRate = loan.Interest ?? 0;
            var monthlyInterestRate = annualInterestRate / 100 / 12; // Convert percentage to decimal (e.g., 12% = 0.12, then /12 = 0.01)
            var months = loan.RepayPeriod ?? 12;
            var dueDate = loanbal.FirstDate.AddMonths(1);
            var repaymentMethod = loan.RepayMethod ?? "AMT";

            decimal totalInterestForLoan = 0;

            if (repaymentMethod == "STL")
            {
                // STL: Fixed Principal + Interest on reducing balance
                decimal monthlyPrincipal = principalAmount / months;
                decimal remainingBalance = principalAmount;

                for (int i = 1; i <= months; i++)
                {
                    decimal interestAmount = remainingBalance * monthlyInterestRate;
                    decimal totalInstallment = monthlyPrincipal + interestAmount;
                    totalInterestForLoan += interestAmount;

                    var schedule = new LoanSchedule
                    {
                        LoanNo = loanNo,
                        CompanyCode = loan.CompanyCode,
                        InstallmentNo = i,
                        DueDate = dueDate.AddMonths(i - 1),
                        PrincipalAmount = monthlyPrincipal,
                        InterestAmount = interestAmount,
                        TotalInstallment = totalInstallment,
                        BalancePrincipal = remainingBalance - monthlyPrincipal,
                        BalanceInterest = totalInterestForLoan - (interestAmount * i),
                        BalanceTotal = (remainingBalance - monthlyPrincipal) + (totalInterestForLoan - (interestAmount * i)),
                        PaidPrincipal = 0,
                        PaidInterest = 0,
                        PaidTotal = 0,
                        OutstandingPrincipal = monthlyPrincipal,
                        OutstandingInterest = interestAmount,
                        OutstandingTotal = totalInstallment,
                        PenaltyAmount = 0,
                        Status = "Pending",
                        DaysOverdue = 0,
                        IsFlexible = false
                    };

                    schedules.Add(schedule);
                    remainingBalance -= monthlyPrincipal;
                }
            }
            else if (repaymentMethod == "AMT")
            {
                // AMT: Equal Monthly Installments (EMI)
                decimal monthlyPayment;
                if (monthlyInterestRate > 0)
                {
                    decimal factor = (decimal)Math.Pow((double)(1 + monthlyInterestRate), months);
                    monthlyPayment = principalAmount * monthlyInterestRate * factor / (factor - 1);
                }
                else
                {
                    monthlyPayment = principalAmount / months;
                }

                decimal remainingBalance = principalAmount;

                for (int i = 1; i <= months; i++)
                {
                    decimal interestAmount = remainingBalance * monthlyInterestRate;
                    decimal principalAmountPayment = monthlyPayment - interestAmount;
                    totalInterestForLoan += interestAmount;

                    if (i == months)
                    {
                        principalAmountPayment = remainingBalance;
                        monthlyPayment = principalAmountPayment + interestAmount;
                    }

                    var schedule = new LoanSchedule
                    {
                        LoanNo = loanNo,
                        CompanyCode = loan.CompanyCode,
                        InstallmentNo = i,
                        DueDate = dueDate.AddMonths(i - 1),
                        PrincipalAmount = principalAmountPayment,
                        InterestAmount = interestAmount,
                        TotalInstallment = monthlyPayment,
                        BalancePrincipal = remainingBalance - principalAmountPayment,
                        BalanceInterest = totalInterestForLoan - (interestAmount * i),
                        BalanceTotal = (remainingBalance - principalAmountPayment) + (totalInterestForLoan - (interestAmount * i)),
                        PaidPrincipal = 0,
                        PaidInterest = 0,
                        PaidTotal = 0,
                        OutstandingPrincipal = principalAmountPayment,
                        OutstandingInterest = interestAmount,
                        OutstandingTotal = monthlyPayment,
                        PenaltyAmount = 0,
                        Status = "Pending",
                        DaysOverdue = 0,
                        IsFlexible = false
                    };

                    schedules.Add(schedule);
                    remainingBalance -= principalAmountPayment;
                }
            }
            else if (repaymentMethod == "RBAL")
            {
                // RBAL: Interest only minimum, principal flexible
                decimal remainingBalance = principalAmount;

                for (int i = 1; i <= months; i++)
                {
                    decimal interestAmount = remainingBalance * monthlyInterestRate;
                    totalInterestForLoan += interestAmount;

                    var schedule = new LoanSchedule
                    {
                        LoanNo = loanNo,
                        CompanyCode = loan.CompanyCode,
                        InstallmentNo = i,
                        DueDate = dueDate.AddMonths(i - 1),
                        PrincipalAmount = 0,
                        InterestAmount = interestAmount,
                        TotalInstallment = interestAmount,
                        BalancePrincipal = remainingBalance,
                        BalanceInterest = totalInterestForLoan - (interestAmount * i),
                        BalanceTotal = remainingBalance,
                        PaidPrincipal = 0,
                        PaidInterest = 0,
                        PaidTotal = 0,
                        OutstandingPrincipal = 0,
                        OutstandingInterest = interestAmount,
                        OutstandingTotal = interestAmount,
                        PenaltyAmount = 0,
                        Status = "Pending",
                        DaysOverdue = 0,
                        IsFlexible = true,
                        MinimumPayment = interestAmount
                    };

                    schedules.Add(schedule);
                }
            }

            // Update loanbal with correct total interest
            loanbal.IntrOwed = totalInterestForLoan;
            loanbal.IntBalance = totalInterestForLoan;
            await _context.SaveChangesAsync();

            await _context.LoanSchedules.AddRangeAsync(schedules);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Generated {schedules.Count} schedule entries for loan {loanNo}. Total Interest: {totalInterestForLoan:C}");

            return schedules;
        }

        public async Task<List<LoanScheduleDTO>> GetLoanScheduleAsync(string loanNo)
        {
            try
            {
                var schedules = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo)
                    .OrderBy(s => s.InstallmentNo)
                    .ToListAsync();

                if (!schedules.Any())
                {
                    _logger.LogWarning($"No schedule found for loan {loanNo}");
                    return new List<LoanScheduleDTO>();
                }

                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo);

                var repayments = await _context.Repay
                    .Where(r => r.LoanNo == loanNo && r.Posted == true)
                    .OrderBy(r => r.DateReceived)
                    .ToListAsync();

                var scheduleDTOs = new List<LoanScheduleDTO>();
                int paymentIndex = 0;
                bool isRBAL = loan?.RepayMethod == "RBAL";

                // Calculate cumulative payments to distribute correctly
                decimal cumulativePrincipalPaid = 0;
                decimal cumulativeInterestPaid = 0;

                foreach (var repayment in repayments)
                {
                    cumulativePrincipalPaid += repayment.Principal ?? 0;
                    cumulativeInterestPaid += repayment.Interest ?? 0;
                }

                decimal remainingPrincipalToAllocate = cumulativePrincipalPaid;
                decimal remainingInterestToAllocate = cumulativeInterestPaid;

                foreach (var schedule in schedules)
                {
                    decimal paidPrincipal = 0;
                    decimal paidInterest = 0;
                    decimal paidPenalty = 0;
                    string status = schedule.Status;

                    if (!isRBAL)
                    {
                        // For AMT/STL, allocate payments to schedules in order
                        if (remainingPrincipalToAllocate > 0 && schedule.OutstandingPrincipal > 0)
                        {
                            paidPrincipal = Math.Min(remainingPrincipalToAllocate, schedule.PrincipalAmount);
                            remainingPrincipalToAllocate -= paidPrincipal;
                        }

                        if (remainingInterestToAllocate > 0 && schedule.OutstandingInterest > 0)
                        {
                            paidInterest = Math.Min(remainingInterestToAllocate, schedule.InterestAmount);
                            remainingInterestToAllocate -= paidInterest;
                        }

                        // Determine status
                        if (paidPrincipal >= schedule.PrincipalAmount - 0.01m && paidInterest >= schedule.InterestAmount - 0.01m)
                        {
                            status = "Paid";
                        }
                        else if (paidPrincipal > 0 || paidInterest > 0)
                        {
                            status = "Partial";
                        }
                        else if (schedule.DueDate < DateTime.Now)
                        {
                            status = "Overdue";
                        }
                        else
                        {
                            status = "Pending";
                        }
                    }
                    else
                    {
                        // For RBAL, interest is paid per schedule
                        if (remainingInterestToAllocate > 0)
                        {
                            paidInterest = Math.Min(remainingInterestToAllocate, schedule.InterestAmount);
                            remainingInterestToAllocate -= paidInterest;
                        }

                        if (paidInterest >= schedule.InterestAmount - 0.01m)
                        {
                            status = "Paid";
                        }
                        else if (paidInterest > 0)
                        {
                            status = "Partial";
                        }
                        else if (schedule.DueDate < DateTime.Now)
                        {
                            status = "Overdue";
                        }
                        else
                        {
                            status = "Pending";
                        }
                    }

                    decimal outstandingPrincipal = schedule.PrincipalAmount - paidPrincipal;
                    decimal outstandingInterest = schedule.InterestAmount - paidInterest;
                    decimal outstandingTotal = outstandingPrincipal + outstandingInterest + schedule.PenaltyAmount;

                    scheduleDTOs.Add(new LoanScheduleDTO
                    {
                        InstallmentNo = schedule.InstallmentNo,
                        DueDate = schedule.DueDate,
                        PrincipalAmount = schedule.PrincipalAmount,
                        InterestAmount = schedule.InterestAmount,
                        TotalInstallment = schedule.TotalInstallment,
                        PaidAmount = paidPrincipal + paidInterest,
                        OutstandingAmount = outstandingTotal,
                        PenaltyAmount = schedule.PenaltyAmount,
                        Status = status,
                        PaidDate = status == "Paid" ? repayments.LastOrDefault()?.DateReceived : null,
                        OutstandingPrincipal = outstandingPrincipal.ToString("N2"),
                        OutstandingInterest = outstandingInterest.ToString("N2"),
                        OutstandingTotal = outstandingTotal.ToString("N2"),
                        IsFlexible = schedule.IsFlexible,
                        MinimumPayment = schedule.MinimumPayment
                    });
                }

                return scheduleDTOs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting loan schedule for {loanNo}");
                throw;
            }
        }

        public async Task UpdateOverdueStatusesAsync(string companyCode)
        {
            var today = DateTime.Now.Date;

            var overdueSchedules = await _context.LoanSchedules
                .Include(s => s.Loan)
                .Where(s => s.Loan != null && s.Loan.CompanyCode == companyCode &&
                           s.Status != "Paid" &&
                           s.DueDate.Date < today)
                .ToListAsync();

            foreach (var schedule in overdueSchedules)
            {
                schedule.Status = "Overdue";
                schedule.DaysOverdue = (today - schedule.DueDate.Date).Days;

                if (schedule.IsFlexible)
                {
                    decimal minimumDue = schedule.MinimumPayment ?? schedule.InterestAmount;
                    schedule.PenaltyAmount = CalculatePenalty(minimumDue, schedule.DaysOverdue);
                }
                else
                {
                    schedule.PenaltyAmount = CalculatePenalty(schedule.OutstandingTotal, schedule.DaysOverdue);
                }
            }

            var overdueLoans = overdueSchedules
                .GroupBy(s => s.LoanNo)
                .Select(g => new { LoanNo = g.Key, Penalty = g.Sum(s => s.PenaltyAmount) });

            foreach (var loanPenalty in overdueLoans)
            {
                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loanPenalty.LoanNo);

                if (loanbal != null)
                {
                    loanbal.Penalty = loanPenalty.Penalty;
                }
            }

            await _context.SaveChangesAsync();
        }

        private decimal CalculatePenalty(decimal amount, int daysOverdue)
        {
            if (daysOverdue <= 0) return 0;

            decimal penaltyRate = 0.01m; // 1% per month
            decimal penalty = amount * penaltyRate * (daysOverdue / 30m);

            return Math.Round(penalty, 2);
        }

        #endregion



        #region Loan Offset with Shares

        public async Task<List<AvailableSharesDTO>> GetAvailableSharesForOffsetAsync(string memberNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Getting available shares for offset for member: {memberNo}");

                // Get all share types where UsedToOffset = true
                var shareTypes = await _context.Sharetypes
                    .Where(s => s.CompanyCode == companyCode && s.UsedToOffset == true)
                    .ToListAsync();

                if (!shareTypes.Any())
                {
                    _logger.LogWarning($"No share types with UsedToOffset=true found for company {companyCode}");
                    return new List<AvailableSharesDTO>();
                }

                var availableShares = new List<AvailableSharesDTO>();

                foreach (var shareType in shareTypes)
                {
                    // ✅ GET DEPOSITSAMOUNT FROM CONTRIBSHARE
                    var totalDeposits = await _context.ContribShares
                        .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                        .SumAsync(cs => cs.DepositsAmount ?? 0);

                    _logger.LogInformation($"Member {memberNo} - ShareType: {shareType.SharesCode}, Total Deposits: {totalDeposits:C}");

                    if (totalDeposits <= 0) continue;

                    // For offset, member can use ALL deposits (locked guarantee doesn't matter for their own loan)
                    var availableAmount = totalDeposits;

                    if (availableAmount > 0)
                    {
                        availableShares.Add(new AvailableSharesDTO
                        {
                            SharesCode = shareType.SharesCode,
                            SharesType = shareType.SharesType ?? shareType.SharesCode,
                            AvailableAmount = availableAmount,
                            TotalShares = totalDeposits,
                            LockedForGuarantee = 0,
                            IsMainShares = shareType.IsMainShares,
                            UsedToOffset = shareType.UsedToOffset,
                            Withdrawable = shareType.Withdrawable,
                            MinAmount = shareType.MinAmount
                        });

                        _logger.LogInformation($"Available for offset: {shareType.SharesType} - KES {availableAmount:N0}");
                    }
                }

                return availableShares;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting available shares for offset for member {memberNo}");
                throw;
            }
        }

        public async Task<decimal> GetSharesLockedForGuaranteeAsync(string memberNo, string sharesCode, string companyCode)
        {
            try
            {
                // Get all approved guarantor commitments where shares are locked
                var lockedAmount = await _context.Loanguar
                    .Where(g => g.MemberNo == memberNo &&
                               g.CompanyCode == companyCode &&
                               g.Transfered == false &&
                               (g.Balance > 0 || (g.Amount > 0 && g.Balance == null)))
                    .SumAsync(g => g.Amount ?? 0);

                return lockedAmount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting locked shares for guarantee for member {memberNo}");
                return 0;
            }
        }

        public async Task<LoanOffsetResponseDTO> OffsetLoanWithSharesAsync(LoanOffsetDTO offsetDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Processing loan offset for loan {offsetDto.LoanNo}, Amount: {offsetDto.AmountToOffset:C}");

                // 1. GET LOAN DATA
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == offsetDto.LoanNo && l.CompanyCode == offsetDto.CompanyCode);

                if (loan == null)
                    throw new InvalidOperationException($"Loan {offsetDto.LoanNo} not found");

                // Store old loan values for audit
                int oldLoanStatus = (int)loan.Status;
                string oldLoanPosted = loan.Posted ?? "";
                decimal oldLoanAamount = loan.Aamount ?? 0;

                if (loan.Status != (int)Status.Disbursed && loan.Status != (int)Status.Endorsed)
                    throw new InvalidOperationException($"Cannot offset loan in status '{loan.Status}'");

                // 2. GET LOAN BALANCE
                var loanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == offsetDto.LoanNo && lb.Companycode == offsetDto.CompanyCode);

                if (loanbal == null)
                    throw new InvalidOperationException($"Loan balance record not found");

                // Store old loanbal values for audit
                decimal oldBalance = loanbal.Balance;
                decimal oldIntrOwed = loanbal.IntrOwed;
                decimal oldPenalty = loanbal.Penalty;
                decimal oldIntBalance = loanbal.IntBalance;

                // 3. GET CURRENT SCHEDULE
                var currentSchedule = await _context.LoanSchedules
                    .Where(s => s.LoanNo == offsetDto.LoanNo && s.Status != "Paid")
                    .OrderBy(s => s.InstallmentNo)
                    .FirstOrDefaultAsync();

                if (currentSchedule == null)
                {
                    if (loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m)
                        throw new InvalidOperationException("Loan is already fully paid.");
                    throw new InvalidOperationException("No active schedule found for this loan.");
                }

                // Store old schedule values for audit
                decimal oldScheduleOutstandingPrincipal = currentSchedule.OutstandingPrincipal;
                decimal oldScheduleOutstandingInterest = currentSchedule.OutstandingInterest;
                decimal oldScheduleOutstandingTotal = currentSchedule.OutstandingTotal;
                string oldScheduleStatus = currentSchedule.Status;

                // 4. GET LOAN TYPE
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == offsetDto.CompanyCode);

                // 5. VALIDATE SHARE TYPE
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(s => s.SharesCode == offsetDto.SharesCode && s.CompanyCode == offsetDto.CompanyCode);

                if (shareType == null)
                    throw new InvalidOperationException($"Share type {offsetDto.SharesCode} not found");

                if (!shareType.UsedToOffset)
                    throw new InvalidOperationException($"Share type {shareType.SharesType} cannot be used for loan offset");

                // 6. GET DEPOSITS FROM CONTRIBSHARE
                var contribShare = await _context.ContribShares
                    .FirstOrDefaultAsync(cs => cs.MemberNo == offsetDto.MemberNo && cs.CompanyCode == offsetDto.CompanyCode);

                if (contribShare == null || (contribShare.DepositsAmount ?? 0) <= 0)
                    throw new InvalidOperationException($"Member has no deposits available for offset.");

                // Store old contribshare values for audit
                decimal oldDepositsAmount = contribShare.DepositsAmount ?? 0;

                decimal currentDeposits = contribShare.DepositsAmount ?? 0;

                _logger.LogInformation($"Member {offsetDto.MemberNo} - Current Deposits: {currentDeposits:C}");

                if (currentDeposits < offsetDto.AmountToOffset)
                    throw new InvalidOperationException($"Insufficient deposits. Available: {currentDeposits:C}, Requested: {offsetDto.AmountToOffset:C}");

                // 7. CALCULATE PENALTY
                decimal penaltyAmount = 0;
                int daysOverdue = 0;

                if (loanType != null && loanType.Penalty == 1 && DateTime.Now > currentSchedule.DueDate)
                {
                    daysOverdue = (DateTime.Now - currentSchedule.DueDate).Days;
                    int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;

                    if (daysOverdue > gracePeriodDays)
                    {
                        int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
                        int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);
                        decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
                        penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
                        _logger.LogInformation($"Penalty calculated: {penaltyAmount:C}");
                    }
                }

                // 8. ALLOCATE OFFSET AMOUNT - Apply to current AND future installments
                decimal remainingAmount = offsetDto.AmountToOffset;
                decimal penaltyAllocated = 0;
                decimal interestAllocated = 0;
                decimal principalAllocated = 0;
                decimal overpaymentAmount = 0;

                // Get ALL remaining schedules (not just current)
                var allRemainingSchedules = await _context.LoanSchedules
                    .Where(s => s.LoanNo == offsetDto.LoanNo && s.Status != "Paid")
                    .OrderBy(s => s.InstallmentNo)
                    .ToListAsync();

                foreach (var schedule in allRemainingSchedules)
                {
                    if (remainingAmount <= 0) break;

                    // Calculate penalty for this schedule if overdue
                    decimal schedulePenalty = 0;
                    if (loanType != null && loanType.Penalty == 1 && DateTime.Now > schedule.DueDate)
                    {
                        int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;
                        if ((DateTime.Now - schedule.DueDate).Days > gracePeriodDays)
                        {
                            int overdueMonths = (int)Math.Ceiling(((DateTime.Now - schedule.DueDate).Days - gracePeriodDays) / 30.0);
                            decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
                            schedulePenalty = schedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
                        }
                    }

                    decimal scheduleTotalDue = schedule.OutstandingPrincipal + schedule.OutstandingInterest + schedulePenalty;

                    if (remainingAmount >= scheduleTotalDue)
                    {
                        // Fully pay this schedule
                        penaltyAllocated += schedulePenalty;
                        interestAllocated += schedule.OutstandingInterest;
                        principalAllocated += schedule.OutstandingPrincipal;
                        remainingAmount -= scheduleTotalDue;

                        // Mark schedule as paid
                        schedule.PaidPrincipal = schedule.PrincipalAmount;
                        schedule.PaidInterest = schedule.InterestAmount;
                        schedule.PaidTotal = schedule.TotalInstallment;
                        schedule.OutstandingPrincipal = 0;
                        schedule.OutstandingInterest = 0;
                        schedule.OutstandingTotal = 0;
                        schedule.Status = "Paid";
                        schedule.PaidDate = DateTime.Now;
                        schedule.PenaltyAmount = (schedule.PenaltyAmount) + schedulePenalty;

                        _logger.LogInformation($"Schedule {schedule.InstallmentNo} fully paid via offset");
                    }
                    else
                    {
                        // Partially pay this schedule
                        // Apply to penalty first
                        if (remainingAmount > 0 && schedulePenalty > 0)
                        {
                            decimal penaltyPart = Math.Min(remainingAmount, schedulePenalty);
                            penaltyAllocated += penaltyPart;
                            remainingAmount -= penaltyPart;
                        }

                        // Apply to interest
                        if (remainingAmount > 0 && schedule.OutstandingInterest > 0)
                        {
                            decimal interestPart = Math.Min(remainingAmount, schedule.OutstandingInterest);
                            interestAllocated += interestPart;
                            schedule.PaidInterest += interestPart;
                            schedule.OutstandingInterest = schedule.InterestAmount - schedule.PaidInterest;
                            remainingAmount -= interestPart;
                        }

                        // Apply to principal
                        if (remainingAmount > 0 && schedule.OutstandingPrincipal > 0)
                        {
                            decimal principalPart = Math.Min(remainingAmount, schedule.OutstandingPrincipal);
                            principalAllocated += principalPart;
                            schedule.PaidPrincipal += principalPart;
                            schedule.OutstandingPrincipal = schedule.PrincipalAmount - schedule.PaidPrincipal;
                            remainingAmount -= principalPart;
                        }

                        schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
                        schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
                        schedule.Status = "Partial";
                        schedule.PenaltyAmount = (schedule.PenaltyAmount) + schedulePenalty;

                        _logger.LogInformation($"Schedule {schedule.InstallmentNo} partially paid via offset");
                        break; // No more money left
                    }
                }

                overpaymentAmount = remainingAmount;

                bool isCurrentInstallmentFullyPaid = (principalAllocated >= currentSchedule.OutstandingPrincipal - 0.01m) &&
                                                      (interestAllocated >= currentSchedule.OutstandingInterest - 0.01m);

                // 9. GENERATE NUMBERS
                string receiptNo = GenerateOffsetReceiptNumber(offsetDto.CompanyCode);
                string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
                int offsetCount = await _context.Repay.CountAsync(r => r.LoanNo == offsetDto.LoanNo && r.Posted == true);
                int paymentNo = offsetCount + 1;

                // 10. CREATE REPAY RECORD
                var offsetRepayment = new Repay
                {
                    LoanNo = offsetDto.LoanNo,
                    MemberNo = offsetDto.MemberNo,
                    CompanyCode = offsetDto.CompanyCode,
                    ReceiptNo = receiptNo,
                    PaymentNo = paymentNo,
                    DateReceived = DateTime.Now,
                    Amount = offsetDto.AmountToOffset,
                    Principal = principalAllocated,
                    Interest = interestAllocated,
                    Penalty = penaltyAllocated,
                    IntrCharged = interestAllocated,
                    IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated),
                    IntrAccrued = currentSchedule.InterestAmount,
                    LoanBalance = Math.Max(0, loanbal.Balance - principalAllocated),
                    RepayRate = currentSchedule.TotalInstallment,
                    Locked = false,
                    Posted = true,
                    Accrued = true,
                    Remarks = $"Loan offset using shares: {shareType.SharesType} - {offsetDto.Remarks}" +
                              (overpaymentAmount > 0 ? $" (Overpayment: KES {overpaymentAmount:N2})" : ""),
                    AuditId = offsetDto.ProcessedBy,
                    AuditTime = DateTime.Now,
                    Transby = offsetDto.ProcessedBy,
                    IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated),
                    Loancode = loan.LoanCode,
                    Interestaccrued = currentSchedule.InterestAmount,
                    Transno = transactionNo,
                    TransDate = DateTime.Now,
                    TransactionNo = transactionNo,
                    ApiKey = offsetDto.SharesCode,
                    UserName = offsetDto.ProcessedBy,
                    Run = 0,
                    AuditDateTime = DateTime.Now,
                    BlockchainTxId = null
                };

                _context.Repay.Add(offsetRepayment);
                await _context.SaveChangesAsync();

                // 11. UPDATE DEPOSITSAMOUNT
                contribShare.DepositsAmount = currentDeposits - offsetDto.AmountToOffset;
                contribShare.AuditDateTime = DateTime.Now;
                _logger.LogInformation($"Deposits reduced from {currentDeposits:C} to {contribShare.DepositsAmount:C}");

                // 12. UPDATE CURRENT SCHEDULE
                if (isCurrentInstallmentFullyPaid)
                {
                    currentSchedule.PaidPrincipal = currentSchedule.PrincipalAmount;
                    currentSchedule.PaidInterest = currentSchedule.InterestAmount;
                    currentSchedule.PaidTotal = currentSchedule.TotalInstallment;
                    currentSchedule.OutstandingPrincipal = 0;
                    currentSchedule.OutstandingInterest = 0;
                    currentSchedule.OutstandingTotal = 0;
                    currentSchedule.Status = "Paid";
                    currentSchedule.PaidDate = DateTime.Now;
                    currentSchedule.PenaltyAmount = (currentSchedule.PenaltyAmount) + penaltyAllocated;
                }
                else
                {
                    currentSchedule.PaidPrincipal += principalAllocated;
                    currentSchedule.PaidInterest += interestAllocated;
                    currentSchedule.PaidTotal = currentSchedule.PaidPrincipal + currentSchedule.PaidInterest;
                    currentSchedule.OutstandingPrincipal = currentSchedule.PrincipalAmount - currentSchedule.PaidPrincipal;
                    currentSchedule.OutstandingInterest = currentSchedule.InterestAmount - currentSchedule.PaidInterest;
                    currentSchedule.OutstandingTotal = currentSchedule.OutstandingPrincipal + currentSchedule.OutstandingInterest;
                    currentSchedule.Status = "Partial";
                    currentSchedule.PenaltyAmount = (currentSchedule.PenaltyAmount) + penaltyAllocated;
                }

                // 13. UPDATE LOANBAL
                loanbal.Balance = Math.Max(0, loanbal.Balance - principalAllocated);
                loanbal.IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated);
                loanbal.Penalty = Math.Max(0, loanbal.Penalty - penaltyAllocated);
                loanbal.IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated);
                loanbal.LastDate = DateTime.Now;
                loanbal.Processdate = DateTime.Now;

                if (isCurrentInstallmentFullyPaid)
                {
                    var nextSchedule = await _context.LoanSchedules
                        .Where(s => s.LoanNo == offsetDto.LoanNo && s.InstallmentNo == currentSchedule.InstallmentNo + 1)
                        .FirstOrDefaultAsync();
                    if (nextSchedule != null)
                    {
                        loanbal.Nextduedate = nextSchedule.DueDate;
                        loanbal.Duedate = nextSchedule.DueDate;
                        loanbal.RepayRate = nextSchedule.TotalInstallment;
                    }
                }

                // 14. CHECK IF LOAN IS FULLY PAID
                bool isFullyPaid = loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m;

                if (isFullyPaid)
                {
                    loanbal.Cleared = true;
                    loan.Status = (int)Status.Closed;
                    loan.Posted = "Closed";
                    loan.Aamount = 0;
                    await ReleaseCollateralGuaranteesForLoanAsync(offsetDto.LoanNo, offsetDto.ProcessedBy);
                    _logger.LogInformation($"Loan {loan.LoanNo} fully paid via offset and closed");
                }
                else if (loan.Status == (int)Status.Disbursed)
                {
                    loan.Status = (int)Status.Endorsed;
                    loan.Posted = "Active";
                }

                loan.Aamount = loanbal.Balance;
                loan.UserName = offsetDto.ProcessedBy;
                loan.AuditDateTime = DateTime.Now;
                await _context.SaveChangesAsync();

                // 15. CREATE GL TRANSACTION
                Gltransaction glTransaction = null;  // ✅ DECLARE OUTSIDE THE TRY BLOCK
                try
                {
                    var shareAccount = shareType.SharesAcc;
                    var loanReceivableAccount = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT";

                    if (!string.IsNullOrEmpty(shareAccount))
                    {
                        glTransaction = new Gltransaction  // ✅ REMOVE 'var' - use existing variable
                        {
                            TransDate = DateTime.Now,
                            Amount = offsetDto.AmountToOffset,
                            DrAccNo = shareAccount,
                            CrAccNo = loanReceivableAccount,
                            Temp = "OFFSET",
                            DocumentNo = receiptNo,
                            Source = "LOAN_OFFSET",
                            CompanyCode = offsetDto.CompanyCode,
                            TransDescript = $"Loan offset using shares - {shareType.SharesType} - Loan {offsetDto.LoanNo}",
                            AuditTime = DateTime.Now,
                            AuditId = offsetDto.ProcessedBy,
                            Cash = 0,
                            DocPosted = 1,
                            TransactionNo = transactionNo,
                            Module = "LOAN",
                            ReconId = 0,
                            AuditDateTime = DateTime.Now
                        };
                        _context.Gltransactions.Add(glTransaction);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error creating GL transaction for loan offset {receiptNo}");
                }

                // 16. CREATE BLOCK AND BLOCKCHAIN TRANSACTION
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = await GetLastBlockHashAsync(),
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                var blockchainData = new
                {
                    TransactionType = "LOAN_OFFSET",
                    LoanNo = offsetDto.LoanNo,
                    MemberNo = offsetDto.MemberNo,
                    ReceiptNo = receiptNo,
                    PaymentNo = paymentNo,
                    Amount = offsetDto.AmountToOffset,
                    InstallmentNo = currentSchedule.InstallmentNo,
                    PenaltyAllocated = penaltyAllocated,
                    InterestAllocated = interestAllocated,
                    PrincipalAllocated = principalAllocated,
                    Overpayment = overpaymentAmount,
                    BalanceAfter = loanbal.Balance,
                    DepositsBefore = currentDeposits,
                    DepositsAfter = contribShare.DepositsAmount,
                    BlockHash = blockHash
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_OFFSET",
                    MemberNo = offsetDto.MemberNo,
                    CompanyCode = offsetDto.CompanyCode,
                    Amount = offsetDto.AmountToOffset,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = receiptNo,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Update all records with BlockchainTxId
                offsetRepayment.BlockchainTxId = blockchainTx.TransactionId;
                loan.BlockchainTxId = blockchainTx.TransactionId;
                loanbal.BlockchainTxId = blockchainTx.TransactionId;
                glTransaction.BlockchainTxId = blockchainTx.TransactionId;
                currentSchedule.BlockchainTxId = blockchainTx.TransactionId;
                contribShare.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN OFFSET
                // ============================================================

                // Get member details for audit
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == offsetDto.MemberNo && m.CompanyCode == offsetDto.CompanyCode);

                string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : offsetDto.MemberNo;

                // Audit Extra Data
                var auditExtraData = new
                {
                    loanNo = offsetDto.LoanNo,
                    memberNo = offsetDto.MemberNo,
                    memberName = memberName,
                    shareTypeCode = offsetDto.SharesCode,
                    shareTypeName = shareType.SharesType,
                    amountOffset = offsetDto.AmountToOffset,
                    depositsBefore = currentDeposits,
                    depositsAfter = contribShare.DepositsAmount,
                    receiptNo = receiptNo,
                    paymentNo = paymentNo,
                    penaltyAllocated = penaltyAllocated,
                    interestAllocated = interestAllocated,
                    principalAllocated = principalAllocated,
                    overpaymentAmount = overpaymentAmount,
                    daysOverdue = daysOverdue,
                    penaltyCalculated = penaltyAmount,
                    remarks = offsetDto.Remarks ?? "",
                    processedBy = offsetDto.ProcessedBy,
                    processedDate = DateTime.Now,
                    loanStatusBefore = oldLoanStatus,
                    loanStatusAfter = loan.Status,
                    loanAmountBefore = oldLoanAamount,
                    loanAmountAfter = loan.Aamount,
                    balanceBefore = oldBalance,
                    balanceAfter = loanbal.Balance,
                    interestOwedBefore = oldIntrOwed,
                    interestOwedAfter = loanbal.IntrOwed,
                    penaltyBefore = oldPenalty,
                    penaltyAfter = loanbal.Penalty,
                    scheduleStatusBefore = oldScheduleStatus,
                    scheduleStatusAfter = currentSchedule.Status,
                    isLoanFullyPaid = isFullyPaid,
                    shareAccount = shareType.SharesAcc,
                    loanReceivableAccount = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT",
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Repayment Record for Audit (NewValue)
                var offsetRepaymentForAudit = new
                {
                    offsetRepayment.Id,
                    offsetRepayment.LoanNo,
                    offsetRepayment.MemberNo,
                    offsetRepayment.ReceiptNo,
                    offsetRepayment.PaymentNo,
                    offsetRepayment.DateReceived,
                    offsetRepayment.Amount,
                    offsetRepayment.Principal,
                    offsetRepayment.Interest,
                    offsetRepayment.Penalty,
                    offsetRepayment.LoanBalance,
                    offsetRepayment.IntrOwed,
                    offsetRepayment.Remarks,
                    offsetRepayment.Transby,
                    offsetRepayment.TransactionNo,
                    offsetRepayment.ApiKey,
                    CreatedBy = offsetDto.ProcessedBy,
                    CreatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                // Save Audit for Offset Repayment (INSERT)
                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: offsetRepaymentForAudit,
                    tableName: "Repay",
                    recordId: receiptNo,
                    userId: offsetDto.ProcessedBy,
                    userName: offsetDto.ProcessedBy,
                    companyCode: offsetDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR CONTRIBSHARE UPDATE
                // ============================================================
                var contribshareAuditExtraData = new
                {
                    loanNo = offsetDto.LoanNo,
                    memberNo = offsetDto.MemberNo,
                    memberName = memberName,
                    shareTypeCode = offsetDto.SharesCode,
                    shareTypeName = shareType.SharesType,
                    depositsBefore = oldDepositsAmount,
                    depositsAfter = contribShare.DepositsAmount,
                    amountReduced = offsetDto.AmountToOffset,
                    reason = $"Loan offset - Loan {offsetDto.LoanNo}",
                    processedBy = offsetDto.ProcessedBy,
                    processedDate = DateTime.Now,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var contribshareForAudit = new
                {
                    contribShare.Id,
                    contribShare.MemberNo,
                    contribShare.Sharescode,
                    contribShare.DepositsAmount,
                    contribShare.CompanyCode,
                    UpdatedBy = offsetDto.ProcessedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new { DepositsAmount = oldDepositsAmount },
                    newModel: contribshareForAudit,
                    tableName: "ContribShares",
                    recordId: contribShare.Id.ToString(),
                    userId: offsetDto.ProcessedBy,
                    userName: offsetDto.ProcessedBy,
                    companyCode: offsetDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(contribshareAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOANBAL UPDATE
                // ============================================================
                var loanbalAuditExtraData = new
                {
                    loanNo = offsetDto.LoanNo,
                    balanceBefore = oldBalance,
                    balanceAfter = loanbal.Balance,
                    principalReduction = principalAllocated,
                    interestOwedBefore = oldIntrOwed,
                    interestOwedAfter = loanbal.IntrOwed,
                    interestReduction = interestAllocated,
                    penaltyBefore = oldPenalty,
                    penaltyAfter = loanbal.Penalty,
                    penaltyReduction = penaltyAllocated,
                    nextDueDate = loanbal.Nextduedate,
                    isCleared = loanbal.Cleared,
                    isFullyPaid = isFullyPaid,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanbalForAudit = new
                {
                    loanbal.LoanNo,
                    loanbal.Balance,
                    loanbal.IntrOwed,
                    loanbal.Penalty,
                    loanbal.LastDate,
                    loanbal.Nextduedate,
                    loanbal.Cleared,
                    loanbal.Processdate,
                    UpdatedBy = offsetDto.ProcessedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new { Balance = oldBalance, IntrOwed = oldIntrOwed, Penalty = oldPenalty },
                    newModel: loanbalForAudit,
                    tableName: "Loanbal",
                    recordId: loanbal.Id.ToString(),
                    userId: offsetDto.ProcessedBy,
                    userName: offsetDto.ProcessedBy,
                    companyCode: offsetDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanbalAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN STATUS CHANGE (if changed)
                // ============================================================
                if (oldLoanStatus != loan.Status)
                {
                    var loanAuditExtraData = new
                    {
                        loanNo = offsetDto.LoanNo,
                        statusChangedFrom = oldLoanStatus,
                        statusChangedTo = loan.Status,
                        reason = isFullyPaid ? "Loan fully paid via share offset and closed" : "Loan status updated after share offset",
                        receiptNo = receiptNo,
                        paymentNo = paymentNo,
                        amountOffset = offsetDto.AmountToOffset,
                        balanceAfter = loanbal.Balance,
                        interestAfter = loanbal.IntrOwed,
                        shareTypeUsed = shareType.SharesType,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var loanForAudit = new
                    {
                        loan.LoanNo,
                        loan.Status,
                        loan.Posted,
                        loan.Aamount,
                        loan.UserName,
                        loan.AuditDateTime,
                        UpdatedBy = offsetDto.ProcessedBy,
                        UpdateReason = isFullyPaid ? "Loan fully paid via share offset" : "Status updated after share offset",
                        BlockchainTxId = blockchainTx.TransactionId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Update,
                        oldModel: new { Status = oldLoanStatus, Posted = oldLoanPosted, Aamount = oldLoanAamount },
                        newModel: loanForAudit,
                        tableName: "Loans",
                        recordId: offsetDto.LoanNo,
                        userId: offsetDto.ProcessedBy,
                        userName: offsetDto.ProcessedBy,
                        companyCode: offsetDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );
                }

                // ============================================================
                // SAVE AUDIT TRAIL FOR LOAN SCHEDULE UPDATE
                // ============================================================
                var scheduleAuditExtraData = new
                {
                    loanNo = offsetDto.LoanNo,
                    installmentNo = currentSchedule.InstallmentNo,
                    dueDate = currentSchedule.DueDate,
                    principalAmount = currentSchedule.PrincipalAmount,
                    interestAmount = currentSchedule.InterestAmount,
                    totalInstallment = currentSchedule.TotalInstallment,
                    statusBefore = oldScheduleStatus,
                    statusAfter = currentSchedule.Status,
                    outstandingPrincipalBefore = oldScheduleOutstandingPrincipal,
                    outstandingPrincipalAfter = currentSchedule.OutstandingPrincipal,
                    outstandingInterestBefore = oldScheduleOutstandingInterest,
                    outstandingInterestAfter = currentSchedule.OutstandingInterest,
                    outstandingTotalBefore = oldScheduleOutstandingTotal,
                    outstandingTotalAfter = currentSchedule.OutstandingTotal,
                    paidPrincipal = currentSchedule.PaidPrincipal,
                    paidInterest = currentSchedule.PaidInterest,
                    penaltyAmount = currentSchedule.PenaltyAmount,
                    paidDate = currentSchedule.PaidDate,
                    isFullPayment = isCurrentInstallmentFullyPaid,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var scheduleForAudit = new
                {
                    currentSchedule.Id,
                    currentSchedule.LoanNo,
                    currentSchedule.InstallmentNo,
                    currentSchedule.Status,
                    currentSchedule.OutstandingPrincipal,
                    currentSchedule.OutstandingInterest,
                    currentSchedule.OutstandingTotal,
                    currentSchedule.PaidPrincipal,
                    currentSchedule.PaidInterest,
                    currentSchedule.PaidTotal,
                    currentSchedule.PaidDate,
                    currentSchedule.PenaltyAmount,
                    UpdatedBy = offsetDto.ProcessedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: new
                    {
                        Status = oldScheduleStatus,
                        OutstandingPrincipal = oldScheduleOutstandingPrincipal,
                        OutstandingInterest = oldScheduleOutstandingInterest,
                        OutstandingTotal = oldScheduleOutstandingTotal
                    },
                    newModel: scheduleForAudit,
                    tableName: "LoanSchedules",
                    recordId: currentSchedule.Id.ToString(),
                    userId: offsetDto.ProcessedBy,
                    userName: offsetDto.ProcessedBy,
                    companyCode: offsetDto.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(scheduleAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // ============================================================
                // SAVE AUDIT TRAIL FOR GL TRANSACTION (if created)
                // ============================================================
                if (glTransaction != null)
                {
                    var glAuditExtraData = new
                    {
                        loanNo = offsetDto.LoanNo,
                        receiptNo = receiptNo,
                        paymentNo = paymentNo,
                        amount = offsetDto.AmountToOffset,
                        drAccount = shareType.SharesAcc,
                        crAccount = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT",
                        transactionType = "LOAN_OFFSET",
                        shareTypeUsed = shareType.SharesType,
                        blockchainTxId = blockchainTx.TransactionId
                    };

                    var glForAudit = new
                    {
                        glTransaction.Id,
                        glTransaction.TransDate,
                        glTransaction.Amount,
                        glTransaction.DrAccNo,
                        glTransaction.CrAccNo,
                        glTransaction.DocumentNo,
                        glTransaction.Source,
                        glTransaction.TransDescript,
                        glTransaction.TransactionNo,
                        CreatedBy = offsetDto.ProcessedBy,
                        CreatedDate = DateTime.Now,
                        BlockchainTxId = blockchainTx.TransactionId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: glForAudit,
                        tableName: "Gltransactions",
                        recordId: glTransaction.Id.ToString(),
                        userId: offsetDto.ProcessedBy,
                        userName: offsetDto.ProcessedBy,
                        companyCode: offsetDto.CompanyCode,
                        module: "LoanManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(glAuditExtraData),
                        blockchainTxId: blockchainTx.TransactionId
                    );
                }

                _logger.LogInformation($"Loan offset audit completed for loan {offsetDto.LoanNo}, Receipt: {receiptNo}");

                await transaction.CommitAsync();

                return new LoanOffsetResponseDTO
                {
                    Success = true,
                    Message = $"Successfully offset KES {offsetDto.AmountToOffset:N0} using {shareType.SharesType}",
                    ReceiptNo = receiptNo,
                    PenaltyAllocated = penaltyAllocated,
                    InterestAllocated = interestAllocated,
                    PrincipalAllocated = principalAllocated,
                    BalanceAfter = loanbal.Balance,
                    BlockchainTxId = blockchainTx.TransactionId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error processing loan offset for loan {offsetDto.LoanNo}");
                throw;
            }
        }

        //public async Task<LoanOffsetResponseDTO> OffsetLoanWithSharesAsync(LoanOffsetDTO offsetDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        _logger.LogInformation($"Processing loan offset for loan {offsetDto.LoanNo}, Amount: {offsetDto.AmountToOffset:C}");

        //        // 1. GET LOAN DATA
        //        var loan = await _context.Loans
        //            .FirstOrDefaultAsync(l => l.LoanNo == offsetDto.LoanNo && l.CompanyCode == offsetDto.CompanyCode);

        //        if (loan == null)
        //            throw new InvalidOperationException($"Loan {offsetDto.LoanNo} not found");

        //        if (loan.Status != (int)Status.Disbursed && loan.Status != (int)Status.Endorsed)
        //            throw new InvalidOperationException($"Cannot offset loan in status '{loan.Status}'");

        //        // 2. GET LOAN BALANCE
        //        var loanbal = await _context.Loanbal
        //            .FirstOrDefaultAsync(lb => lb.LoanNo == offsetDto.LoanNo && lb.Companycode == offsetDto.CompanyCode);

        //        if (loanbal == null)
        //            throw new InvalidOperationException($"Loan balance record not found");

        //        // 3. GET CURRENT SCHEDULE
        //        var currentSchedule = await _context.LoanSchedules
        //            .Where(s => s.LoanNo == offsetDto.LoanNo && s.Status != "Paid")
        //            .OrderBy(s => s.InstallmentNo)
        //            .FirstOrDefaultAsync();

        //        if (currentSchedule == null)
        //        {
        //            if (loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m)
        //                throw new InvalidOperationException("Loan is already fully paid.");
        //            throw new InvalidOperationException("No active schedule found for this loan.");
        //        }

        //        // 4. GET LOAN TYPE
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == offsetDto.CompanyCode);

        //        // 5. VALIDATE SHARE TYPE
        //        var shareType = await _context.Sharetypes
        //            .FirstOrDefaultAsync(s => s.SharesCode == offsetDto.SharesCode && s.CompanyCode == offsetDto.CompanyCode);

        //        if (shareType == null)
        //            throw new InvalidOperationException($"Share type {offsetDto.SharesCode} not found");

        //        if (!shareType.UsedToOffset)
        //            throw new InvalidOperationException($"Share type {shareType.SharesType} cannot be used for loan offset");

        //        // 6. GET DEPOSITS FROM CONTRIBSHARE
        //        var contribShare = await _context.ContribShares
        //            .FirstOrDefaultAsync(cs => cs.MemberNo == offsetDto.MemberNo && cs.CompanyCode == offsetDto.CompanyCode);

        //        if (contribShare == null || (contribShare.DepositsAmount ?? 0) <= 0)
        //            throw new InvalidOperationException($"Member has no deposits available for offset.");

        //        decimal currentDeposits = contribShare.DepositsAmount ?? 0;

        //        _logger.LogInformation($"Member {offsetDto.MemberNo} - Current Deposits: {currentDeposits:C}");

        //        if (currentDeposits < offsetDto.AmountToOffset)
        //            throw new InvalidOperationException($"Insufficient deposits. Available: {currentDeposits:C}, Requested: {offsetDto.AmountToOffset:C}");

        //        // 7. CALCULATE PENALTY
        //        decimal penaltyAmount = 0;
        //        int daysOverdue = 0;

        //        if (loanType != null && loanType.Penalty == 1 && DateTime.Now > currentSchedule.DueDate)
        //        {
        //            daysOverdue = (DateTime.Now - currentSchedule.DueDate).Days;
        //            int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;

        //            if (daysOverdue > gracePeriodDays)
        //            {
        //                int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
        //                int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);
        //                decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
        //                penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
        //                _logger.LogInformation($"Penalty calculated: {penaltyAmount:C}");
        //            }
        //        }

        //        // 8. ALLOCATE OFFSET AMOUNT - Apply to current AND future installments
        //        decimal remainingAmount = offsetDto.AmountToOffset;
        //        decimal penaltyAllocated = 0;
        //        decimal interestAllocated = 0;
        //        decimal principalAllocated = 0;
        //        decimal overpaymentAmount = 0;

        //        // Get ALL remaining schedules (not just current)
        //        var allRemainingSchedules = await _context.LoanSchedules
        //            .Where(s => s.LoanNo == offsetDto.LoanNo && s.Status != "Paid")
        //            .OrderBy(s => s.InstallmentNo)
        //            .ToListAsync();

        //        foreach (var schedule in allRemainingSchedules)
        //        {
        //            if (remainingAmount <= 0) break;

        //            // Calculate penalty for this schedule if overdue
        //            decimal schedulePenalty = 0;
        //            if (loanType != null && loanType.Penalty == 1 && DateTime.Now > schedule.DueDate)
        //            {
        //                int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;
        //                if ((DateTime.Now - schedule.DueDate).Days > gracePeriodDays)
        //                {
        //                    int overdueMonths = (int)Math.Ceiling(((DateTime.Now - schedule.DueDate).Days - gracePeriodDays) / 30.0);
        //                    decimal monthlyPenaltyRate = (loanType.Penalty) / 100;
        //                    schedulePenalty = schedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
        //                }
        //            }

        //            decimal scheduleTotalDue = schedule.OutstandingPrincipal + schedule.OutstandingInterest + schedulePenalty;

        //            if (remainingAmount >= scheduleTotalDue)
        //            {
        //                // Fully pay this schedule
        //                penaltyAllocated += schedulePenalty;
        //                interestAllocated += schedule.OutstandingInterest;
        //                principalAllocated += schedule.OutstandingPrincipal;
        //                remainingAmount -= scheduleTotalDue;

        //                // Mark schedule as paid
        //                schedule.PaidPrincipal = schedule.PrincipalAmount;
        //                schedule.PaidInterest = schedule.InterestAmount;
        //                schedule.PaidTotal = schedule.TotalInstallment;
        //                schedule.OutstandingPrincipal = 0;
        //                schedule.OutstandingInterest = 0;
        //                schedule.OutstandingTotal = 0;
        //                schedule.Status = "Paid";
        //                schedule.PaidDate = DateTime.Now;
        //                schedule.PenaltyAmount = (schedule.PenaltyAmount) + schedulePenalty;

        //                _logger.LogInformation($"Schedule {schedule.InstallmentNo} fully paid via offset");
        //            }
        //            else
        //            {
        //                // Partially pay this schedule
        //                // Apply to penalty first
        //                if (remainingAmount > 0 && schedulePenalty > 0)
        //                {
        //                    decimal penaltyPart = Math.Min(remainingAmount, schedulePenalty);
        //                    penaltyAllocated += penaltyPart;
        //                    remainingAmount -= penaltyPart;
        //                }

        //                // Apply to interest
        //                if (remainingAmount > 0 && schedule.OutstandingInterest > 0)
        //                {
        //                    decimal interestPart = Math.Min(remainingAmount, schedule.OutstandingInterest);
        //                    interestAllocated += interestPart;
        //                    schedule.PaidInterest += interestPart;
        //                    schedule.OutstandingInterest = schedule.InterestAmount - schedule.PaidInterest;
        //                    remainingAmount -= interestPart;
        //                }

        //                // Apply to principal
        //                if (remainingAmount > 0 && schedule.OutstandingPrincipal > 0)
        //                {
        //                    decimal principalPart = Math.Min(remainingAmount, schedule.OutstandingPrincipal);
        //                    principalAllocated += principalPart;
        //                    schedule.PaidPrincipal += principalPart;
        //                    schedule.OutstandingPrincipal = schedule.PrincipalAmount - schedule.PaidPrincipal;
        //                    remainingAmount -= principalPart;
        //                }

        //                schedule.PaidTotal = schedule.PaidPrincipal + schedule.PaidInterest;
        //                schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
        //                schedule.Status = "Partial";
        //                schedule.PenaltyAmount = (schedule.PenaltyAmount) + schedulePenalty;

        //                _logger.LogInformation($"Schedule {schedule.InstallmentNo} partially paid via offset");
        //                break; // No more money left
        //            }
        //        }

        //        overpaymentAmount = remainingAmount;

        //        bool isCurrentInstallmentFullyPaid = (principalAllocated >= currentSchedule.OutstandingPrincipal - 0.01m) &&
        //                                              (interestAllocated >= currentSchedule.OutstandingInterest - 0.01m);

        //        // 9. GENERATE NUMBERS
        //        string receiptNo = GenerateOffsetReceiptNumber(offsetDto.CompanyCode);
        //        string transactionNo = DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString().Substring(0, 8);
        //        int offsetCount = await _context.Repay.CountAsync(r => r.LoanNo == offsetDto.LoanNo && r.Posted == true);
        //        int paymentNo = offsetCount + 1;

        //        // 10. CREATE REPAY RECORD
        //        var offsetRepayment = new Repay
        //        {
        //            LoanNo = offsetDto.LoanNo,
        //            MemberNo = offsetDto.MemberNo,
        //            CompanyCode = offsetDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            PaymentNo = paymentNo,
        //            DateReceived = DateTime.Now,
        //            Amount = offsetDto.AmountToOffset,
        //            Principal = principalAllocated,
        //            Interest = interestAllocated,
        //            Penalty = penaltyAllocated,
        //            IntrCharged = interestAllocated,
        //            IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated),
        //            IntrAccrued = currentSchedule.InterestAmount,
        //            LoanBalance = Math.Max(0, loanbal.Balance - principalAllocated),
        //            RepayRate = currentSchedule.TotalInstallment,
        //            Locked = false,
        //            Posted = true,
        //            Accrued = true,
        //            Remarks = $"Loan offset using shares: {shareType.SharesType} - {offsetDto.Remarks}" +
        //                      (overpaymentAmount > 0 ? $" (Overpayment: KES {overpaymentAmount:N2})" : ""),
        //            AuditId = offsetDto.ProcessedBy,
        //            AuditTime = DateTime.Now,
        //            Transby = offsetDto.ProcessedBy,
        //            IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated),
        //            Loancode = loan.LoanCode,
        //            Interestaccrued = currentSchedule.InterestAmount,
        //            Transno = transactionNo,
        //            TransDate = DateTime.Now,
        //            TransactionNo = transactionNo,
        //            ApiKey = offsetDto.SharesCode,
        //            UserName = offsetDto.ProcessedBy,
        //            Run = 0,
        //            AuditDateTime = DateTime.Now,
        //            BlockchainTxId = null
        //        };

        //        _context.Repay.Add(offsetRepayment);
        //        await _context.SaveChangesAsync();

        //        // 11. UPDATE DEPOSITSAMOUNT
        //        contribShare.DepositsAmount = currentDeposits - offsetDto.AmountToOffset;
        //        contribShare.AuditDateTime = DateTime.Now;
        //        _logger.LogInformation($"Deposits reduced from {currentDeposits:C} to {contribShare.DepositsAmount:C}");

        //        // 12. UPDATE CURRENT SCHEDULE
        //        if (isCurrentInstallmentFullyPaid)
        //        {
        //            currentSchedule.PaidPrincipal = currentSchedule.PrincipalAmount;
        //            currentSchedule.PaidInterest = currentSchedule.InterestAmount;
        //            currentSchedule.PaidTotal = currentSchedule.TotalInstallment;
        //            currentSchedule.OutstandingPrincipal = 0;
        //            currentSchedule.OutstandingInterest = 0;
        //            currentSchedule.OutstandingTotal = 0;
        //            currentSchedule.Status = "Paid";
        //            currentSchedule.PaidDate = DateTime.Now;
        //            currentSchedule.PenaltyAmount = (currentSchedule.PenaltyAmount) + penaltyAllocated;
        //        }
        //        else
        //        {
        //            currentSchedule.PaidPrincipal += principalAllocated;
        //            currentSchedule.PaidInterest += interestAllocated;
        //            currentSchedule.PaidTotal = currentSchedule.PaidPrincipal + currentSchedule.PaidInterest;
        //            currentSchedule.OutstandingPrincipal = currentSchedule.PrincipalAmount - currentSchedule.PaidPrincipal;
        //            currentSchedule.OutstandingInterest = currentSchedule.InterestAmount - currentSchedule.PaidInterest;
        //            currentSchedule.OutstandingTotal = currentSchedule.OutstandingPrincipal + currentSchedule.OutstandingInterest;
        //            currentSchedule.Status = "Partial";
        //            currentSchedule.PenaltyAmount = (currentSchedule.PenaltyAmount) + penaltyAllocated;
        //        }

        //        // 13. UPDATE LOANBAL
        //        loanbal.Balance = Math.Max(0, loanbal.Balance - principalAllocated);
        //        loanbal.IntrOwed = Math.Max(0, loanbal.IntrOwed - interestAllocated);
        //        loanbal.Penalty = Math.Max(0, loanbal.Penalty - penaltyAllocated);
        //        loanbal.IntBalance = Math.Max(0, loanbal.IntBalance - interestAllocated);
        //        loanbal.LastDate = DateTime.Now;
        //        loanbal.Processdate = DateTime.Now;

        //        if (isCurrentInstallmentFullyPaid)
        //        {
        //            var nextSchedule = await _context.LoanSchedules
        //                .Where(s => s.LoanNo == offsetDto.LoanNo && s.InstallmentNo == currentSchedule.InstallmentNo + 1)
        //                .FirstOrDefaultAsync();
        //            if (nextSchedule != null)
        //            {
        //                loanbal.Nextduedate = nextSchedule.DueDate;
        //                loanbal.Duedate = nextSchedule.DueDate;
        //                loanbal.RepayRate = nextSchedule.TotalInstallment;
        //            }
        //        }

        //        // 14. CHECK IF LOAN IS FULLY PAID
        //        bool isFullyPaid = loanbal.Balance <= 0.01m && loanbal.IntrOwed <= 0.01m;

        //        if (isFullyPaid)
        //        {
        //            loanbal.Cleared = true;
        //            loan.Status = (int)Status.Closed;
        //            loan.Posted = "Closed";
        //            loan.Aamount = 0;
        //            await ReleaseCollateralGuaranteesForLoanAsync(offsetDto.LoanNo, offsetDto.ProcessedBy);
        //            _logger.LogInformation($"Loan {loan.LoanNo} fully paid via offset and closed");
        //        }
        //        else if (loan.Status == (int)Status.Disbursed)
        //        {
        //            loan.Status = (int)Status.Endorsed;
        //            loan.Posted = "Active";
        //        }

        //        loan.Aamount = loanbal.Balance;
        //        loan.UserName = offsetDto.ProcessedBy;
        //        loan.AuditDateTime = DateTime.Now;
        //        await _context.SaveChangesAsync();

        //        // 15. CREATE GL TRANSACTION
        //        Gltransaction glTransaction = null;  // ✅ DECLARE OUTSIDE THE TRY BLOCK
        //        try
        //        {
        //            var shareAccount = shareType.SharesAcc;
        //            var loanReceivableAccount = loanType?.LoanAcc ?? "LOAN_RECEIVABLE_ACCOUNT";

        //            if (!string.IsNullOrEmpty(shareAccount))
        //            {
        //                glTransaction = new Gltransaction  // ✅ REMOVE 'var' - use existing variable
        //                {
        //                    TransDate = DateTime.Now,
        //                    Amount = offsetDto.AmountToOffset,
        //                    DrAccNo = shareAccount,
        //                    CrAccNo = loanReceivableAccount,
        //                    Temp = "OFFSET",
        //                    DocumentNo = receiptNo,
        //                    Source = "LOAN_OFFSET",
        //                    CompanyCode = offsetDto.CompanyCode,
        //                    TransDescript = $"Loan offset using shares - {shareType.SharesType} - Loan {offsetDto.LoanNo}",
        //                    AuditTime = DateTime.Now,
        //                    AuditId = offsetDto.ProcessedBy,
        //                    Cash = 0,
        //                    DocPosted = 1,
        //                    TransactionNo = transactionNo,
        //                    Module = "LOAN",
        //                    ReconId = 0,
        //                    AuditDateTime = DateTime.Now
        //                };
        //                _context.Gltransactions.Add(glTransaction);
        //                await _context.SaveChangesAsync();
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError(ex, $"Error creating GL transaction for loan offset {receiptNo}");
        //        }

        //        // 16. CREATE BLOCK AND BLOCKCHAIN TRANSACTION
        //        string blockHash = Guid.NewGuid().ToString().Replace("-", "");
        //        if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
        //        else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

        //        var block = new Block
        //        {
        //            BlockHash = blockHash,
        //            PreviousHash = await GetLastBlockHashAsync(),
        //            Timestamp = DateTime.Now,
        //            Nonce = 0,
        //            MerkleRoot = Guid.NewGuid().ToString(),
        //            Confirmed = true,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.Blocks.Add(block);
        //        await _context.SaveChangesAsync();

        //        var blockchainData = new
        //        {
        //            TransactionType = "LOAN_OFFSET",
        //            LoanNo = offsetDto.LoanNo,
        //            MemberNo = offsetDto.MemberNo,
        //            ReceiptNo = receiptNo,
        //            PaymentNo = paymentNo,
        //            Amount = offsetDto.AmountToOffset,
        //            InstallmentNo = currentSchedule.InstallmentNo,
        //            PenaltyAllocated = penaltyAllocated,
        //            InterestAllocated = interestAllocated,
        //            PrincipalAllocated = principalAllocated,
        //            Overpayment = overpaymentAmount,
        //            BalanceAfter = loanbal.Balance,
        //            DepositsBefore = currentDeposits,
        //            DepositsAfter = contribShare.DepositsAmount,
        //            BlockHash = blockHash
        //        };

        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "LOAN_OFFSET",
        //            MemberNo = offsetDto.MemberNo,
        //            CompanyCode = offsetDto.CompanyCode,
        //            Amount = offsetDto.AmountToOffset,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = receiptNo,
        //            Status = "CONFIRMED",
        //            BlockHash = block.BlockHash,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        // Update all records with BlockchainTxId
        //        offsetRepayment.BlockchainTxId = blockchainTx.TransactionId;
        //        loan.BlockchainTxId = blockchainTx.TransactionId;
        //        loanbal.BlockchainTxId = blockchainTx.TransactionId;
        //        glTransaction.BlockchainTxId = blockchainTx.TransactionId;
        //        currentSchedule.BlockchainTxId = blockchainTx.TransactionId;
        //        contribShare.BlockchainTxId = blockchainTx.TransactionId;
        //        await _context.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        return new LoanOffsetResponseDTO
        //        {
        //            Success = true,
        //            Message = $"Successfully offset KES {offsetDto.AmountToOffset:N0} using {shareType.SharesType}",
        //            ReceiptNo = receiptNo,
        //            PenaltyAllocated = penaltyAllocated,
        //            InterestAllocated = interestAllocated,
        //            PrincipalAllocated = principalAllocated,
        //            BalanceAfter = loanbal.Balance,
        //            BlockchainTxId = blockchainTx.TransactionId
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error processing loan offset for loan {offsetDto.LoanNo}");
        //        throw;
        //    }
        //}

        private string GenerateOffsetReceiptNumber(string companyCode)
        {
            var prefix = "OFF";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastOffset = _context.Repay
                .Where(r => r.CompanyCode == companyCode && r.ReceiptNo != null && r.ReceiptNo.StartsWith($"{prefix}{date}"))
                .OrderByDescending(r => r.ReceiptNo)
                .FirstOrDefault();

            if (lastOffset != null && lastOffset.ReceiptNo != null && lastOffset.ReceiptNo.Length > 11)
            {
                if (int.TryParse(lastOffset.ReceiptNo.Substring(11), out int lastSeq))
                    sequence = lastSeq + 1;
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        private async Task<string> GenerateOffsetReceiptNumberAsync(string companyCode)
        {
            var prefix = "OFF";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastOffset = await _context.Repay
                .Where(r => r.CompanyCode == companyCode &&
                           r.ReceiptNo.StartsWith($"{prefix}{date}") &&
                           r.TransactionNo == "SHARE_OFFSET")
                .OrderByDescending(r => r.ReceiptNo)
                .FirstOrDefaultAsync();

            if (lastOffset != null && lastOffset.ReceiptNo.Length > 11)
            {
                var lastSequence = int.Parse(lastOffset.ReceiptNo.Substring(11));
                sequence = lastSequence + 1;
            }

            return $"{prefix}{date}{sequence:D4}";
        }

        #endregion


        #region State Management

        public async Task<bool> UpdateLoanStatusAsync(string loanNo, string newStatus, string performedBy, string? remarks = null)
        {
            // IMPORTANT: Use GetLoanByNoForDisplayAsync to get the original status without recalculation
            var loan = await GetLoanByNoForDisplayAsync(loanNo, (await _context.Loans.FirstAsync(l => l.LoanNo == loanNo)).CompanyCode);
            var oldStatus = loan.Status;

            _logger.LogInformation($"UpdateLoanStatusAsync - Loan {loanNo}: {oldStatus} -> {newStatus}");

            // Convert string status to int for validation
            int newStatusValue = newStatus switch
            {
                "Draft" => (int)Status.Draft,
                "Submitted" => (int)Status.Submitted,
                "UnderAppraisal" => (int)Status.UnderAppraisal,
                "Approved" => (int)Status.Approved,
                "Endorsed" => (int)Status.Endorsed,
                "Disbursed" => (int)Status.Disbursed,
                "Closed" => (int)Status.Closed,
                "Rejected" => (int)Status.Rejected,
                "WrittenOff" => (int)Status.WrittenOff,
                "Defaulted" => (int)Status.Defaulted,
                _ => throw new InvalidOperationException($"Invalid status: {newStatus}")
            };

            // Validate the transition is allowed
            if (!await CanTransitionAsync(loanNo, newStatusValue))
            {
                throw new InvalidOperationException($"Cannot transition from {oldStatus} to {newStatus}");
            }

            loan.Status = newStatusValue;
            loan.UserName = performedBy;
            loan.AuditDateTime = DateTime.Now;

            await _context.SaveChangesAsync();

            // Record audit trail (comment out if CreateAuditTrailAsync doesn't exist)
            // await CreateAuditTrailAsync(loanNo, oldStatus.ToString(), newStatus.ToString(), "STATUS_CHANGE",
            //     remarks ?? $"Status changed from {oldStatus} to {newStatus}", performedBy, loan.CompanyCode);

            // Record blockchain transaction for status change
            try
            {
                var blockchainData = new
                {
                    LoanNo = loanNo,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedBy = performedBy,
                    ChangedAt = DateTime.Now,
                    Remarks = remarks
                };

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "LOAN_STATUS_CHANGE",
                    MemberNo = loan.MemberNo,
                    CompanyCode = loan.CompanyCode,
                    Amount = 0,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = loanNo,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                loan.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to record blockchain transaction for status change");
            }

            return true;
        }


        public async Task<bool> CanTransitionAsync(string loanNo, int targetStatus)
        {
            // Use GetLoanByNoForDisplayAsync to get original status
            var loan = await GetLoanByNoForDisplayAsync(loanNo, (await _context.Loans.FirstAsync(l => l.LoanNo == loanNo)).CompanyCode);
            var currentStatus = loan.Status ?? 0;

            // Define valid transitions using Status enum values
            var validTransitions = new Dictionary<int, List<int>>
            {
                { (int)Status.Draft, new List<int> { (int)Status.Submitted, (int)Status.Rejected } },
                { (int)Status.Submitted, new List<int> { (int)Status.UnderAppraisal, (int)Status.Rejected } },
                { (int)Status.UnderAppraisal, new List<int> { (int)Status.Approved, (int)Status.Rejected } },
                { (int)Status.Approved, new List<int> { (int)Status.Endorsed, (int)Status.Rejected } },
                { (int)Status.Endorsed, new List<int> { (int)Status.Disbursed, (int)Status.Rejected } },
                { (int)Status.Rejected, new List<int>() },
                { (int)Status.Disbursed, new List<int> { (int)Status.Endorsed, (int)Status.Closed, (int)Status.WrittenOff } },
                { (int)Status.Endorsed, new List<int> { (int)Status.Closed, (int)Status.WrittenOff } },
                { (int)Status.Closed, new List<int>() },
                { (int)Status.WrittenOff, new List<int>() },
                { (int)Status.Defaulted, new List<int>() }
            };

            return validTransitions.ContainsKey(currentStatus) &&
                   validTransitions[currentStatus].Contains(targetStatus);
        }

        #endregion

        #region Validation

        public async Task<(bool IsValid, string Message)> ValidateLoanApplicationAsync(LoanApplicationDTO application)
        {
            // Check if loan type exists
            var loanType = await _context.Loantypes
                .FirstOrDefaultAsync(l => l.LoanCode == application.LoanCode && l.CompanyCode == application.CompanyCode);

            if (loanType == null)
            {
                return (false, "Loan type not found");
            }

            // Check maximum loan amount
            if (application.PrincipalAmount > (loanType.MaxAmount ?? decimal.MaxValue))
            {
                return (false, $"Loan amount exceeds maximum allowed of {loanType.MaxAmount:C}");
            }

            // Check if member has existing active loans that exceed limit
            var activeLoans = await _context.Loans
                .CountAsync(l => l.MemberNo == application.MemberNo &&
                                l.CompanyCode == application.CompanyCode &&
                                (l.Status == (int)Status.Disbursed || l.Status == (int)Status.Endorsed));

            if (activeLoans >= (loanType.MaxLoans ?? int.MaxValue))
            {
                return (false, $"Member has reached maximum number of active loans ({loanType.MaxLoans})");
            }

            return (true, "Validation passed");
        }

        public async Task<(bool IsEligible, string Message)> CheckMemberEligibilityAsync(string memberNo, string loanCode, string companyCode)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                return (false, "Member not found");
            }

            // Check if member is active
            if (member.Withdrawn == true || member.Archived == true || member.Dormant == 1)
            {
                return (false, "Member is not active");
            }

            // Check minimum contribution period (if applicable)
            // This would check how long the member has been contributing

            // Check existing loan defaults
            var hasDefaulted = await HasPreviousDefaultAsync(memberNo, companyCode);
            if (hasDefaulted)
            {
                return (false, "Member has previous loan defaults");
            }

            return (true, "Member is eligible");
        }

        public async Task<decimal> CalculateMaximumLoanAmountAsync(string memberNo, string loanCode, string companyCode)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            var loanType = await _context.Loantypes
                .FirstOrDefaultAsync(l => l.LoanCode == loanCode && l.CompanyCode == companyCode);

            if (member == null || loanType == null)
            {
                return 0;
            }

            // Get member's shares value
            var shareValue = await _shareService.GetTotalSharesValueAsync(memberNo);

            // Calculate based on shares (typical SACCO rule: 3x shares or up to max amount)
            var maxByShares = shareValue * 3;

            // Apply loan type maximum
            var maxAmount = Math.Min(maxByShares, loanType.MaxAmount ?? decimal.MaxValue);

            // Consider existing loan balances
            var existingLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo &&
                           l.CompanyCode == companyCode &&
                           (l.Status == (int)Status.Disbursed || l.Status == (int)Status.Endorsed))
                .SumAsync(l => l.LoanAmt ?? 0);

            maxAmount -= existingLoans;

            return Math.Max(0, maxAmount);
        }

        #endregion

        #region Audit

        //public async Task<List<LoanAuditTrail>> GetLoanAuditTrailAsync(string loanNo)
        //{
        //    var auditTrails = await _context.LoanAuditTrails
        //        .Where(a => a.LoanNo == loanNo)
        //        .OrderByDescending(a => a.PerformedDate)
        //        .ToListAsync();

        //    return auditTrails;
        //}

        #endregion

        #region Private Helper Methods

        private async Task<string> GenerateLoanNumberAsync(string loanCode, string memberNo, string companyCode)
        {
            // Format: Milliseconds(3 digits) + LoanCode + MemberNo
            // Example: 123LO001M2603301135

            string milliseconds = DateTime.Now.ToString("fff");

            return $"{milliseconds}{loanCode}{memberNo}";
        }

        private async Task<int> CalculateCreditScore(string memberNo, string companyCode)
        {
            // Simplified credit scoring
            int score = 600; // Base score

            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null) return score;

            // Length of membership
            if (member.EffectDate.HasValue)
            {
                var years = (DateTime.Now - member.EffectDate.Value).TotalDays / 365;
                score += (int)(years * 10); // 10 points per year
            }

            // Share capital
            if (member.ShareCap.HasValue)
            {
                if (member.ShareCap > 100000) score += 50;
                else if (member.ShareCap > 50000) score += 30;
                else if (member.ShareCap > 10000) score += 20;
            }

            // Previous loan history
            var previousLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode)
                .ToListAsync();

            if (previousLoans.Any())
            {
                // Check if any defaults
                var hasDefault = previousLoans.Any(l => l.Status == (int)Status.WrittenOff);
                if (hasDefault) score -= 100;

                // Check repayment history
                var closedLoans = previousLoans.Count(l => l.Status == (int)Status.Closed);
                score += closedLoans * 20;
            }

            return Math.Clamp(score, 300, 850);
        }

        private async Task<bool> HasPreviousDefaultAsync(string memberNo, string companyCode)
        {
            return await _context.Loans
                .AnyAsync(l => l.MemberNo == memberNo &&
                              l.CompanyCode == companyCode &&
                              (l.Status == (int)Status.WrittenOff || l.Status == (int)Status.Defaulted));
        }

        private async Task<int> CalculateLoanHistoryRatingAsync(string memberNo, string companyCode)
        {
            var loans = await _context.Loans
                .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode)
                .ToListAsync();

            if (!loans.Any()) return 3; // No history - average

            var totalLoans = loans.Count;
            var closedLoans = loans.Count(l => l.Status == (int)Status.Closed);

            var repayments = await _context.Repay
                .Where(r => r.MemberNo == memberNo && r.CompanyCode == companyCode && r.Posted == true)
                .ToListAsync();

            var onTimePayments = repayments.Count;

            var rating = 3; // Base

            if (closedLoans == totalLoans && totalLoans > 0) rating += 1;
            if (onTimePayments > 10) rating += 1;

            return Math.Clamp(rating, 1, 5);
        }

        private string GetUserRole(string username)
        {
            // This would fetch user role from your identity system
            return "LoanOfficer";
        }

        private async Task RecalculateLoanBalancesAsync(string loanNo)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo);

            if (loan == null) return;

            var loanbal = await _context.Loanbal
                .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo);

            if (loanbal == null) return;

            var schedules = await _context.LoanSchedules
                .Where(s => s.LoanNo == loanNo)
                .OrderBy(s => s.InstallmentNo)
                .ToListAsync();

            // Reset loan balances
            loanbal.Balance = loan.LoanAmt ?? 0;
            loanbal.IntrOwed = 0;
            loanbal.Penalty = 0;

            // Reset schedule balances
            foreach (var schedule in schedules)
            {
                schedule.PaidPrincipal = 0;
                schedule.PaidInterest = 0;
                schedule.PaidTotal = 0;
                schedule.OutstandingPrincipal = schedule.PrincipalAmount;
                schedule.OutstandingInterest = schedule.InterestAmount;
                schedule.OutstandingTotal = schedule.TotalInstallment;
                schedule.Status = schedule.DueDate < DateTime.Now ? "Overdue" : "Pending";
            }

            await _context.SaveChangesAsync();

            // Reapply all completed repayments in order
            var repayments = await _context.Repay
                .Where(r => r.LoanNo == loanNo && r.Posted == true)
                .OrderBy(r => r.DateReceived)
                .ToListAsync();

            foreach (var repayment in repayments)
            {
                // Apply repayment to loanbal
                loanbal.Balance -= repayment.Principal ?? 0;
                loanbal.IntrOwed = Math.Max(0, loanbal.IntrOwed - (repayment.Interest ?? 0));
                loanbal.Penalty = Math.Max(0, loanbal.Penalty - (repayment.Penalty ?? 0));

                // Apply to schedules
                decimal remainingForSchedule = (repayment.Principal ?? 0) + (repayment.Interest ?? 0);

                foreach (var schedule in schedules.Where(s => s.Status != "Paid"))
                {
                    if (remainingForSchedule <= 0) break;

                    decimal scheduleOutstanding = schedule.OutstandingPrincipal + schedule.OutstandingInterest;

                    if (remainingForSchedule >= scheduleOutstanding)
                    {
                        schedule.PaidPrincipal = schedule.PrincipalAmount;
                        schedule.PaidInterest = schedule.InterestAmount;
                        schedule.PaidTotal = schedule.TotalInstallment;
                        schedule.OutstandingPrincipal = 0;
                        schedule.OutstandingInterest = 0;
                        schedule.OutstandingTotal = 0;
                        schedule.Status = "Paid";
                        schedule.PaidDate = repayment.DateReceived;
                        remainingForSchedule -= scheduleOutstanding;
                    }
                    else
                    {
                        if (remainingForSchedule <= schedule.OutstandingInterest)
                        {
                            schedule.PaidInterest = (schedule.PaidInterest) + remainingForSchedule;
                            schedule.OutstandingInterest = schedule.InterestAmount - (schedule.PaidInterest);
                        }
                        else
                        {
                            schedule.PaidInterest = schedule.InterestAmount;
                            schedule.OutstandingInterest = 0;
                            remainingForSchedule -= schedule.OutstandingInterest;

                            schedule.PaidPrincipal = (schedule.PaidPrincipal) + remainingForSchedule;
                            schedule.OutstandingPrincipal = schedule.PrincipalAmount - (schedule.PaidPrincipal);
                        }

                        schedule.PaidTotal = (schedule.PaidTotal) + remainingForSchedule;
                        schedule.OutstandingTotal = schedule.OutstandingPrincipal + schedule.OutstandingInterest;
                        schedule.Status = "Partial";
                        remainingForSchedule = 0;
                    }
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<AuditTrail>> GetLoanAuditTrailAsync(string loanNo)
        {
            try
            {
                return await _context.AuditTrails
                    .Where(a => a.RecordId == loanNo && a.TableName == "Loans")
                    .OrderByDescending(a => a.AuditTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting audit trail for loan {loanNo}");
                return new List<AuditTrail>();
            }
        }

        public async Task<Loanbal?> GetLoanBalanceAsync(string loanNo, string companyCode)
        {
            try
            {
                return await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting loan balance for loan {loanNo} and company {companyCode}");
                return null;
            }
        }

        public async Task<bool> RejectGuarantorAsync(int guarantorId, string remarks, string rejectedBy)
        {
            var guarantor = await _context.Loanguar
                .FirstOrDefaultAsync(g => g.Id == guarantorId);

            if (guarantor == null)
            {
                throw new InvalidOperationException("Guarantor not found");
            }

            // Store old values for audit
            bool oldTransfered = guarantor.Transfered;
            string oldDescription = guarantor.Description ?? "";
            string oldAuditId = guarantor.AuditId ?? "";
            DateTime? oldAuditTime = guarantor.AuditTime;
            decimal? oldAmount = guarantor.Amount;
            string oldMemberNo = guarantor.MemberNo;
            string oldLoanNo = guarantor.LoanNo;

            if (guarantor.Transfered == true)
            {
                throw new InvalidOperationException($"Cannot reject guarantor that is already transferred");
            }

            guarantor.Transfered = true;
            guarantor.Description = remarks;
            guarantor.AuditTime = DateTime.Now;
            guarantor.AuditId = rejectedBy;
            await _context.SaveChangesAsync();

            // Get member details for audit
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == guarantor.MemberNo && m.CompanyCode == guarantor.CompanyCode);

            string memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : guarantor.MemberNo;

            // Get loan details for audit
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == guarantor.LoanNo && l.CompanyCode == guarantor.CompanyCode);

            string loanStatus = loan != null ? loan.Status.ToString() : "Unknown";

            var blockchainData = new
            {
                Id = guarantor.Id,
                LoanNo = guarantor.LoanNo,
                GuarantorMemberNo = guarantor.MemberNo,
                GuarantorName = memberName,
                GuaranteeAmount = guarantor.Amount,
                RejectionReason = remarks,
                RejectedBy = rejectedBy,
                RejectionDate = DateTime.Now,
                LoanStatus = loanStatus
            };

            var blockchainTx = new BlockchainTransaction
            {
                TransactionId = Guid.NewGuid().ToString(),
                TransactionType = "LOAN_GUARANTOR_REJECTED",
                MemberNo = guarantor.MemberNo,
                CompanyCode = guarantor.CompanyCode,
                Amount = guarantor.Amount ?? 0,
                Timestamp = DateTime.Now,
                DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                OffChainReferenceId = $"{guarantor.LoanNo}-{guarantor.MemberNo}-rejected",
                Status = "PENDING",
                CreatedAt = DateTime.Now
            };

            _context.BlockchainTransactions.Add(blockchainTx);
            await _context.SaveChangesAsync();

            guarantor.BlockchainTxId = blockchainTx.TransactionId;
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Blockchain transaction recorded for guarantor rejection: {blockchainTx.TransactionId}");

            // ============================================================
            // SAVE AUDIT TRAIL FOR GUARANTOR REJECTION
            // ============================================================

            var auditExtraData = new
            {
                guarantorId = guarantor.Id,
                loanNo = guarantor.LoanNo,
                guarantorMemberNo = guarantor.MemberNo,
                guarantorName = memberName,
                guaranteeAmount = oldAmount,
                rejectionReason = remarks,
                rejectedBy = rejectedBy,
                rejectionDate = DateTime.Now,
                loanStatus = loanStatus,
                transferedBefore = oldTransfered,
                transferedAfter = guarantor.Transfered,
                descriptionBefore = oldDescription,
                descriptionAfter = guarantor.Description,
                auditIdBefore = oldAuditId,
                auditIdAfter = guarantor.AuditId,
                auditTimeBefore = oldAuditTime,
                auditTimeAfter = guarantor.AuditTime,
                blockchainTxId = blockchainTx.TransactionId
            };

            var guarantorForAudit = new
            {
                guarantor.Id,
                guarantor.LoanNo,
                guarantor.MemberNo,
                guarantor.Amount,
                guarantor.Transfered,
                guarantor.Description,
                guarantor.AuditId,
                guarantor.AuditTime,
                RejectedBy = rejectedBy,
                RejectionDate = DateTime.Now,
                RejectionReason = remarks,
                BlockchainTxId = blockchainTx.TransactionId
            };

            await _auditService.SaveLogAsync(
                actionType: AuditActionType.Update,
                oldModel: new
                {
                    Transfered = oldTransfered,
                    Description = oldDescription,
                    AuditId = oldAuditId,
                    AuditTime = oldAuditTime
                },
                newModel: guarantorForAudit,
                tableName: "Loanguar",
                recordId: guarantor.Id.ToString(),
                userId: rejectedBy,
                userName: rejectedBy,
                companyCode: guarantor.CompanyCode,
                module: "LoanManagement",
                extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                blockchainTxId: blockchainTx.TransactionId
            );

            // ============================================================
            // ALSO UPDATE LOAN STATUS IF NEEDED (check if all guarantors rejected?)
            // ============================================================

            // Check if this loan has any remaining active guarantors
            var remainingActiveGuarantors = await _context.Loanguar
                .CountAsync(g => g.LoanNo == guarantor.LoanNo && g.Transfered == false && g.CompanyCode == guarantor.CompanyCode);

            _logger.LogInformation($"Remaining active guarantors for loan {guarantor.LoanNo}: {remainingActiveGuarantors}");

            // If loan exists and has no active guarantors, log this for awareness
            if (loan != null && remainingActiveGuarantors == 0)
            {
                var loanAuditExtraData = new
                {
                    loanNo = guarantor.LoanNo,
                    message = "All guarantors have been rejected for this loan",
                    remainingGuarantors = remainingActiveGuarantors,
                    lastRejectedGuarantorId = guarantor.Id,
                    lastRejectedBy = rejectedBy,
                    lastRejectionDate = DateTime.Now,
                    blockchainTxId = blockchainTx.TransactionId
                };

                var loanForAudit = new
                {
                    loan.LoanNo,
                    loan.Status,
                    loan.Posted,
                    Note = "All guarantors have been rejected. Loan may need new guarantors or may be declined.",
                    UpdatedBy = rejectedBy,
                    UpdatedDate = DateTime.Now,
                    BlockchainTxId = blockchainTx.TransactionId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Update,
                    oldModel: null,
                    newModel: loanForAudit,
                    tableName: "Loans",
                    recordId: guarantor.LoanNo,
                    userId: rejectedBy,
                    userName: rejectedBy,
                    companyCode: guarantor.CompanyCode,
                    module: "LoanManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(loanAuditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                _logger.LogWarning($"Loan {guarantor.LoanNo} has no active guarantors after rejection of guarantor {guarantor.Id}");
            }

            _logger.LogInformation($"Guarantor rejection audit completed for guarantor ID: {guarantor.Id}, Loan: {guarantor.LoanNo}");

            return true;
        }

        private async Task CreateAuditTrailAsync(
           string loanNo,
           string? previousStatus,
           string? newStatus,
           string action,
           string description,
           string performedBy,
           string companyCode)
        {
            var audit = new AuditTrail
            {
                CompanyCode = companyCode,
                UserId = performedBy,
                UserName = performedBy,
                ActionType = action,
                ActionDescription = description,
                TableName = "Loans",
                RecordId = loanNo,
                OldValue = previousStatus,
                NewValue = newStatus,
                AuditTime = DateTime.Now,
                Module = "LOAN_MANAGEMENT",
                CorrelationId = Guid.NewGuid().ToString(),
                ExtraData = System.Text.Json.JsonSerializer.Serialize(new
                {
                    LoanNo = loanNo,
                    PreviousStatus = previousStatus,
                    NewStatus = newStatus,
                    Action = action
                })
            };

            _context.AuditTrails.Add(audit);
            await _context.SaveChangesAsync();
        }

        public Task<LoanSchedule> GetCurrentInstallmentAsync(string loanNo)
        {
            throw new NotImplementedException();
        }

        public Task RecalculateRbalScheduleAsync(string loanNo, decimal newOutstandingBalance)
        {
            throw new NotImplementedException();
        }

        Task ILoanService.CreateAuditTrailAsync(string loanNo, string? previousStatus, string? newStatus, string action, string description, string performedBy, string companyCode)
        {
            return CreateAuditTrailAsync(loanNo, previousStatus, newStatus, action, description, performedBy, companyCode);
        }

        //public Task<decimal> GetMemberAvailableDepositsForGuaranteeAsync(string guarantorMemberNo, string companyCode)
        //{
        //    throw new NotImplementedException();
        //}

        #endregion
    }
}