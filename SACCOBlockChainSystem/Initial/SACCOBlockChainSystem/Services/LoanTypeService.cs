using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SACCOBlockChainSystem.Services
{
    public class LoanTypeService : ILoanTypeService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LoanTypeService> _logger;
        private readonly IBlockchainService _blockchainService;
        private readonly ICompanyContextService _companyContextService;

        public LoanTypeService(
            ApplicationDbContext context,
            ILogger<LoanTypeService> logger,
            IBlockchainService blockchainService,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _logger = logger;
            _blockchainService = blockchainService;
            _companyContextService = companyContextService;
        }

        public async Task<LoanTypeResponseDTO> CreateLoanTypeAsync(LoanTypeCreateDTO loanTypeDto)
        {
            _logger.LogInformation($"Creating loan type: {loanTypeDto.LoanCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validate DTO
                await ValidateLoanTypeAsync(loanTypeDto);

                // Check if loan type already exists
                var existingLoanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loanTypeDto.LoanCode &&
                                              lt.CompanyCode == loanTypeDto.CompanyCode);
                
                if (existingLoanType != null)
                {
                    throw new ValidationException($"Loan type with code '{loanTypeDto.LoanCode}' already exists");
                }

                // Validate accounts exist
                await ValidateAccountsExist(loanTypeDto);

                // Create new loan type with Pending status
                var loanType = new Loantype
                {
                    LoanCode = loanTypeDto.LoanCode,
                    LoanType1 = loanTypeDto.LoanType,
                    ValueChain = loanTypeDto.ValueChain,
                    LoanProduct = loanTypeDto.LoanProduct,
                    LoanAcc = loanTypeDto.LoanAcc,
                    InterestAcc = loanTypeDto.InterestAcc,
                    PenaltyAcc = loanTypeDto.PenaltyAcc,
                    RepayPeriod = loanTypeDto.RepayPeriod,
                    Interest = loanTypeDto.Interest,
                    MaxAmount = loanTypeDto.MaxAmount,
                    Guarantor = loanTypeDto.Guarantor,
                    UseintRange = loanTypeDto.UseIntRange,
                    EarningRation = loanTypeDto.EarningRatio,
                    Penalty = loanTypeDto.Penalty ? 1: 0,
                    Processingfee = loanTypeDto.ProcessingFee,
                    GracePeriod = loanTypeDto.GracePeriod,
                    Repaymethod = loanTypeDto.RepayMethod,
                    Bridging = loanTypeDto.Bridging ? 1 : 0,
                    SelfGuarantee = loanTypeDto.SelfGuarantee,
                    MobileLoan = loanTypeDto.MobileLoan,
                    Ppacc = loanTypeDto.Ppacc ?? string.Empty, // Use empty string if null
                    ContraAccount = loanTypeDto.ContraAccount ?? string.Empty, // Use empty string if null
                    Priority = loanTypeDto.Priority,
                    MaxLoans = loanTypeDto.MaxLoans,
                    CompanyCode = loanTypeDto.CompanyCode,
                    AuditId = loanTypeDto.CreatedBy,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    // Set default values for required fields from model
                    AccruedAcc = "000000",
                    Mdtei = 0,
                    Intrecovery = "000000",
                    IsMain = true,
                    ReceivableAcc = "000000",
                    MinimumPaidForBridging = 0,
                    MinimumPaidForTopup = 0,
                    ApprovalStatus = "Pending"
                };

                _context.Loantypes.Add(loanType);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                await CreateBlockchainTransaction("LOAN_TYPE_CREATE", loanType, loanTypeDto.CreatedBy);

                await transaction.CommitAsync();
                _logger.LogInformation($"Loan type {loanType.LoanCode} created successfully with Pending status");

                // Return response DTO
                return await GetLoanTypeResponseDto(loanType);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error creating loan type {loanTypeDto.LoanCode}");
                throw;
            }
        }

        public async Task<LoanTypeResponseDTO> ApproveLoanTypeAsync(string loanCode, string companyCode, string approvedBy)
        {
            _logger.LogInformation($"Approving loan type: {loanCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loanCode &&
                                              lt.CompanyCode == companyCode);

                if (loanType == null)
                {
                    throw new KeyNotFoundException($"Loan type '{loanCode}' not found");
                }

                if (loanType.ApprovalStatus == "Active")
                {
                    throw new ValidationException($"Loan type '{loanCode}' is already approved");
                }

                // Update status to Active
                loanType.ApprovalStatus = "Active";
                loanType.AuditId = approvedBy;
                loanType.AuditTime = DateTime.Now;
                loanType.AuditDateTime = DateTime.Now;

                await _context.SaveChangesAsync();

                // Create blockchain transaction for approval
                var blockchainData = new
                {
                    LoanTypeCode = loanType.LoanCode,
                    LoanTypeName = loanType.LoanType1,
                    CompanyCode = loanType.CompanyCode,
                    ApprovedBy = approvedBy,
                    ApprovedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    PreviousStatus = "Pending",
                    NewStatus = "Active"
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    "LOAN_TYPE_APPROVE",
                    approvedBy ?? "SYSTEM",
                    loanType.CompanyCode,
                    0,
                    loanType.LoanCode,
                    blockchainData
                );

                await transaction.CommitAsync();
                _logger.LogInformation($"Loan type {loanCode} approved successfully");

                return await GetLoanTypeResponseDto(loanType);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error approving loan type {loanCode}");
                throw;
            }
        }

        public async Task<LoanTypeResponseDTO> UpdateLoanTypeAsync(string loanCode, LoanTypeUpdateDTO loanTypeDto)
        {
            _logger.LogInformation($"Updating loan type: {loanCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get existing loan type
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loanCode &&
                                              lt.CompanyCode == loanTypeDto.CompanyCode);

                if (loanType == null)
                {
                    throw new KeyNotFoundException($"Loan type '{loanCode}' not found");
                }

                // Check if loan type is in use
                var usageCount = await GetLoanTypeUsageCountAsync(loanCode, loanTypeDto.CompanyCode);
                if (usageCount > 0)
                {
                    //// Validate that critical fields aren't being changed when in use
                    //if (loanType.Bridging != loanTypeDto.Bridging ||
                    //    loanType.MobileLoan != loanTypeDto.MobileLoan ||
                    //    loanType.SelfGuarantee != loanTypeDto.SelfGuarantee)


                     if ((loanType.Bridging == 1) != loanTypeDto.Bridging ||
                         loanType.MobileLoan != loanTypeDto.MobileLoan ||
                         loanType.SelfGuarantee != loanTypeDto.SelfGuarantee)
                        {
                        throw new ValidationException(
                            "Cannot change critical properties when loan type is in use by members");
                    }
                }

                // Store old values for blockchain record
                var oldValues = new
                {
                    loanType.LoanType1,
                    loanType.MaxAmount,
                    loanType.Interest,
                    loanType.ApprovalStatus
                };

                // Update fields
                loanType.LoanType1 = loanTypeDto.LoanType;
                loanType.ValueChain = loanTypeDto.ValueChain;
                loanType.LoanProduct = loanTypeDto.LoanProduct;
                loanType.LoanAcc = loanTypeDto.LoanAcc;
                loanType.InterestAcc = loanTypeDto.InterestAcc;
                loanType.PenaltyAcc = loanTypeDto.PenaltyAcc;
                loanType.RepayPeriod = loanTypeDto.RepayPeriod;
                loanType.Interest = loanTypeDto.Interest;
                loanType.MaxAmount = loanTypeDto.MaxAmount;
                loanType.Guarantor = loanTypeDto.Guarantor;
                loanType.UseintRange = loanTypeDto.UseIntRange;
                loanType.EarningRation = loanTypeDto.EarningRatio;
                loanType.Penalty = loanTypeDto.Penalty ? 1 : 0;
                loanType.Processingfee = loanTypeDto.ProcessingFee;
                loanType.GracePeriod = loanTypeDto.GracePeriod;
                loanType.Repaymethod = loanTypeDto.RepayMethod;
                loanType.Bridging = loanTypeDto.Bridging ? 1 : 0;
                loanType.SelfGuarantee = loanTypeDto.SelfGuarantee;
                loanType.MobileLoan = loanTypeDto.MobileLoan;
                loanType.Ppacc = loanTypeDto.Ppacc ?? string.Empty;
                loanType.ContraAccount = loanTypeDto.ContraAccount ?? string.Empty;
                loanType.Priority = loanTypeDto.Priority;
                loanType.MaxLoans = loanTypeDto.MaxLoans;
                loanType.AuditId = loanTypeDto.UpdatedBy;
                loanType.AuditTime = DateTime.Now;
                loanType.AuditDateTime = DateTime.Now;

                // If updating from Pending, keep as Pending (requires re-approval)
                if (loanType.ApprovalStatus == "Active")
                {
                    loanType.ApprovalStatus = "Pending"; // Require re-approval after update
                }

                await _context.SaveChangesAsync();

                // Create blockchain transaction for update
                await CreateBlockchainTransaction("LOAN_TYPE_UPDATE", loanType, loanTypeDto.UpdatedBy, oldValues);

                await transaction.CommitAsync();
                _logger.LogInformation($"Loan type {loanCode} updated successfully");

                return await GetLoanTypeResponseDto(loanType);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating loan type {loanCode}");
                throw;
            }
        }

        public async Task<bool> DeleteLoanTypeAsync(string loanCode, string companyCode)
        {
            _logger.LogInformation($"Deleting loan type: {loanCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loanCode &&
                                              lt.CompanyCode == companyCode);

                if (loanType == null)
                {
                    throw new KeyNotFoundException($"Loan type '{loanCode}' not found");
                }

                // Check if loan type is in use
                var usageCount = await GetLoanTypeUsageCountAsync(loanCode, companyCode);
                if (usageCount > 0)
                {
                    throw new ValidationException(
                        $"Cannot delete loan type '{loanCode}' because it's used by {usageCount} loan(s)");
                }

                // Create blockchain transaction before deletion
                await CreateBlockchainTransaction("LOAN_TYPE_DELETE", loanType, "SYSTEM");

                // Delete from database
                _context.Loantypes.Remove(loanType);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation($"Loan type {loanCode} deleted successfully");

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error deleting loan type {loanCode}");
                throw;
            }
        }

        public async Task<LoanTypeResponseDTO> GetLoanTypeByCodeAsync(string loanCode, string companyCode)
        {
            var loanType = await _context.Loantypes
                .FirstOrDefaultAsync(lt => lt.LoanCode == loanCode &&
                                          lt.CompanyCode == companyCode);

            if (loanType == null)
            {
                throw new KeyNotFoundException($"Loan type '{loanCode}' not found");
            }

            return await GetLoanTypeResponseDto(loanType);
        }

        public async Task<List<LoanTypeResponseDTO>> GetLoanTypesByCompanyAsync(string companyCode)
        {
            var loanTypes = await _context.Loantypes
                .Where(lt => lt.CompanyCode == companyCode)
                .OrderBy(lt => lt.Priority)
                .ThenBy(lt => lt.LoanType1)
                .ToListAsync();

            var result = new List<LoanTypeResponseDTO>();
            foreach (var loanType in loanTypes)
            {
                result.Add(await GetLoanTypeResponseDto(loanType));
            }

            return result;
        }

        public async Task<List<LoanTypeSimpleDTO>> GetActiveLoanTypesAsync(string companyCode)
        {
            return await _context.Loantypes
                .Where(lt => lt.CompanyCode == companyCode &&
                            lt.ApprovalStatus == "Active")
                .OrderBy(lt => lt.Priority)
                .Select(lt => new LoanTypeSimpleDTO
                {
                    LoanCode = lt.LoanCode,
                    LoanType = lt.LoanType1,
                    MaxAmount = lt.MaxAmount,
                    RepayPeriod = lt.RepayPeriod,
                    Interest = lt.Interest,
                    Guarantor = lt.Guarantor,
                    ProcessingFee = lt.Processingfee.ToString(),
                    Bridging = lt.Bridging == 0,
                    MobileLoan = lt.MobileLoan ?? false,
                    SelfGuarantee = lt.SelfGuarantee.ToString(),
                    Priority = lt.Priority ?? 1,
                    IsEligible = true
                })
                .ToListAsync();
        }

        public async Task<List<LoanTypeResponseDTO>> SearchLoanTypesAsync(string searchTerm, string companyCode)
        {
            var query = _context.Loantypes
                .Where(lt => lt.CompanyCode == companyCode);

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(lt =>
                    lt.LoanCode.Contains(searchTerm) ||
                    lt.LoanType1.Contains(searchTerm) ||
                    (lt.LoanProduct != null && lt.LoanProduct.Contains(searchTerm)) ||
                    (lt.ValueChain != null && lt.ValueChain.Contains(searchTerm)));
            }

            var loanTypes = await query
                .OrderBy(lt => lt.Priority)
                .ToListAsync();

            var result = new List<LoanTypeResponseDTO>();
            foreach (var loanType in loanTypes)
            {
                result.Add(await GetLoanTypeResponseDto(loanType));
            }

            return result;
        }

        public async Task<bool> ValidateLoanTypeAsync(LoanTypeCreateDTO loanTypeDto)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(loanTypeDto.LoanCode))
                throw new ValidationException("Loan code is required");

            if (string.IsNullOrWhiteSpace(loanTypeDto.LoanType))
                throw new ValidationException("Loan type name is required");

            if (string.IsNullOrWhiteSpace(loanTypeDto.LoanAcc))
                throw new ValidationException("Loan account is required");

            // Remove PP and Contra account required validation
            // if (string.IsNullOrWhiteSpace(loanTypeDto.Ppacc))
            //     throw new ValidationException("PP Account is required");

            // if (string.IsNullOrWhiteSpace(loanTypeDto.ContraAccount))
            //     throw new ValidationException("Contra account is required");

            if (loanTypeDto.Priority < 1 || loanTypeDto.Priority > 10)
                throw new ValidationException("Priority must be between 1 and 10");

            if (loanTypeDto.GracePeriod < 0)
                throw new ValidationException("Grace period cannot be negative");

            if (loanTypeDto.RepayPeriod.HasValue && loanTypeDto.RepayPeriod <= 0)
                throw new ValidationException("Repayment period must be greater than 0");

            if (loanTypeDto.MaxAmount.HasValue && loanTypeDto.MaxAmount <= 0)
                throw new ValidationException("Maximum amount must be greater than 0");

            if (loanTypeDto.ProcessingFee.HasValue && loanTypeDto.ProcessingFee < 0)
                throw new ValidationException("Processing fee cannot be negative");

            return true;
        }

        public async Task<int> GetLoanTypeUsageCountAsync(string loanCode, string companyCode)
        {
            // Count loans using this loan type
            return await _context.Loans
                .CountAsync(l => l.LoanCode == loanCode &&
                               l.CompanyCode == companyCode);
        }

        public async Task<List<LoanTypeSimpleDTO>> GetLoanTypesForMemberAsync(string memberNo, string companyCode)
        {
            // Get member details
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo &&
                                         m.CompanyCode == companyCode);

            if (member == null)
            {
                throw new KeyNotFoundException($"Member '{memberNo}' not found");
            }

            // Get member's total shares
            var totalShares = await _context.Shares
                .Where(s => s.MemberNo == memberNo && s.CompanyCode == companyCode)
                .SumAsync(s => s.TotalShares ?? 0);

            // Get member's existing loans
            var activeStatuses = new[] { (int)Status.Approved, (int)Status.Endorsed, (int)Status.Disbursed };
            var existingLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo &&
                           l.CompanyCode == companyCode &&
                           activeStatuses.Contains(l.Status ?? 0))
                .ToListAsync();

            var existingLoanCount = existingLoans.Count;

            // Get all active loan types
            var loanTypes = await _context.Loantypes
                .Where(lt => lt.CompanyCode == companyCode &&
                            lt.ApprovalStatus == "Active")
                .OrderBy(lt => lt.Priority)
                .ToListAsync();

            // Calculate eligibility for each loan type
            var result = new List<LoanTypeSimpleDTO>();
            foreach (var loanType in loanTypes)
            {
                var isEligible = true;
                var reason = string.Empty;
                decimal eligibleAmount = 0;

                // Check maximum number of loans
                if (loanType.MaxLoans.HasValue && existingLoanCount >= loanType.MaxLoans)
                {
                    isEligible = false;
                    reason = $"Maximum number of loans ({loanType.MaxLoans}) reached";
                }

                // Check if member has existing loan of this type (if bridging not allowed)
                if (isEligible && loanType.Bridging != 1)
                {
                    var hasExistingType = existingLoans.Any(l => l.LoanCode == loanType.LoanCode);
                    if (hasExistingType)
                    {
                        isEligible = false;
                        reason = "You already have an active loan of this type";
                    }
                }

                // Calculate based on earning ratio (shares)
                if (isEligible && loanType.EarningRation.HasValue && loanType.EarningRation > 0)
                {
                    var calculatedAmount = totalShares * (decimal)loanType.EarningRation;
                    if (loanType.MaxAmount.HasValue)
                    {
                        eligibleAmount = Math.Min(calculatedAmount, loanType.MaxAmount.Value);
                    }
                    else
                    {
                        eligibleAmount = calculatedAmount;
                    }
                }
                else if (loanType.MaxAmount.HasValue)
                {
                    eligibleAmount = loanType.MaxAmount.Value;
                }

                result.Add(new LoanTypeSimpleDTO
                {
                    LoanCode = loanType.LoanCode,
                    LoanType = loanType.LoanType1,
                    MaxAmount = loanType.MaxAmount,
                    RepayPeriod = loanType.RepayPeriod,
                    Interest = loanType.Interest,
                    Guarantor = loanType.Guarantor,
                    ProcessingFee = loanType.Processingfee?.ToString(),
                    Bridging = loanType.Bridging == 0,
                    MobileLoan = loanType.MobileLoan ?? false,
                    SelfGuarantee = loanType.SelfGuarantee?.ToString(),
                    Priority = loanType.Priority ?? 1,
                    IsEligible = isEligible,
                    EligibleAmount = eligibleAmount,
                    Reason = reason
                });
            }

            return result;
        }

        public async Task<LoanTypeStatisticsDTO> GetLoanTypeStatisticsAsync(string companyCode)
        {
            var loanTypes = await _context.Loantypes
                .Where(lt => lt.CompanyCode == companyCode)
                .ToListAsync();

            var totalLoanTypes = loanTypes.Count;
            var activeLoanTypes = loanTypes.Count(lt => lt.ApprovalStatus == "Active");
            var pendingLoanTypes = loanTypes.Count(lt => lt.ApprovalStatus == "Pending");

            var totalLoans = await _context.Loans
                .Where(l => l.CompanyCode == companyCode)
                .CountAsync();

            var totalLoanAmount = await _context.Loans
                .Where(l => l.CompanyCode == companyCode)
                .SumAsync(l => l.LoanAmt ?? 0);

            var totalDisbursed = await _context.Loans
                .Where(l => l.CompanyCode == companyCode && l.AuditDateTime != null)
                .SumAsync(l => l.LoanAmt ?? 0);

            var activeStatuses = new[] { (int)Status.Approved, (int)Status.Endorsed, (int)Status.Disbursed };
            var totalOutstanding = await _context.Loans
                .Where(l => l.CompanyCode == companyCode && activeStatuses.Contains(l.Status ?? 0))
                .SumAsync(l => l.LoanAmt ?? 0);

            return new LoanTypeStatisticsDTO
            {
                TotalLoanTypes = totalLoanTypes,
                ActiveLoanTypes = activeLoanTypes,
                TotalLoans = totalLoans,
                TotalLoanAmount = totalLoanAmount,
                TotalDisbursed = totalDisbursed,
                TotalOutstanding = totalOutstanding,
                AverageLoanAmount = totalLoans > 0 ? totalLoanAmount / totalLoans : 0,
                LoanTypesByStatus = loanTypes
                    .GroupBy(lt => lt.ApprovalStatus ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        private async Task<LoanTypeResponseDTO> GetLoanTypeResponseDto(Loantype loanType)
        {
            // Get usage statistics
            var totalLoans = await _context.Loans
                .CountAsync(l => l.LoanCode == loanType.LoanCode &&
                               l.CompanyCode == loanType.CompanyCode);

            var activeStatuses = new[] { (int)Status.Approved, (int)Status.Endorsed, (int)Status.Disbursed };
            var activeLoans = await _context.Loans
                .CountAsync(l => l.LoanCode == loanType.LoanCode &&
                               l.CompanyCode == loanType.CompanyCode &&
                               activeStatuses.Contains(l.Status ?? 0));

            var totalLoanAmount = await _context.Loans
                .Where(l => l.LoanCode == loanType.LoanCode &&
                          l.CompanyCode == loanType.CompanyCode)
                .SumAsync(l => l.LoanAmt ?? 0);

            var totalDisbursed = await _context.Loans
                .Where(l => l.LoanCode == loanType.LoanCode &&
                          l.CompanyCode == loanType.CompanyCode &&
                          l.AuditDateTime != null)
                .SumAsync(l => l.LoanAmt ?? 0);

            return new LoanTypeResponseDTO
            {
                LoanCode = loanType.LoanCode,
                LoanType = loanType.LoanType1,
                ValueChain = loanType.ValueChain,
                LoanProduct = loanType.LoanProduct,
                LoanAcc = loanType.LoanAcc,
                InterestAcc = loanType.InterestAcc,
                PenaltyAcc = loanType.PenaltyAcc,
                RepayPeriod = loanType.RepayPeriod,
                Interest = loanType.Interest,
                MaxAmount = loanType.MaxAmount,
                Guarantor = loanType.Guarantor,
                UseIntRange = loanType.UseintRange,
                EarningRatio = loanType.EarningRation,
                Penalty = loanType.Penalty == 0,
                ProcessingFee = loanType.Processingfee,
                GracePeriod = loanType.GracePeriod,
                RepayMethod = loanType.Repaymethod,
                Bridging = loanType.Bridging == 0,
                SelfGuarantee = loanType.SelfGuarantee ?? false,
                MobileLoan = loanType.MobileLoan ?? false,
                Ppacc = loanType.Ppacc,
                ContraAccount = loanType.ContraAccount,
                MaxLoans = loanType.MaxLoans,
                Priority = loanType.Priority ?? 1,
                CompanyCode = loanType.CompanyCode,
                CreatedBy = loanType.AuditId,
                CreatedAt = loanType.AuditDateTime,
                UpdatedAt = loanType.AuditDateTime,
                TotalLoans = totalLoans,
                TotalLoanAmount = totalLoanAmount,
                ActiveLoans = activeLoans,
                TotalDisbursed = totalDisbursed,
                ApprovalStatus = loanType.ApprovalStatus
            };
        }

        public async Task<dynamic> GetAllLoanTypesAsync(string companyCode)
        {
            try
            {
                var loanTypes = await _context.Loantypes
                    .Where(lt => lt.CompanyCode == companyCode)
                    .OrderBy(lt => lt.Priority)
                    .ThenBy(lt => lt.LoanType1)
                    .Select(lt => new
                    {
                        lt.LoanCode,
                        lt.LoanType1,
                        LoanName = lt.LoanType1 ?? lt.LoanCode,
                        lt.MaxAmount,
                        lt.RepayPeriod,
                        lt.Interest,
                        lt.Bridging,
                        lt.MobileLoan,
                        lt.Priority,
                        lt.Guarantor,
                        lt.SelfGuarantee,
                        lt.Processingfee,
                        lt.GracePeriod,
                        lt.Repaymethod,
                        lt.CompanyCode,
                        lt.ApprovalStatus,
                        TotalLoans = _context.Loans.Count(l => l.LoanCode == lt.LoanCode &&
                                                              l.CompanyCode == lt.CompanyCode),
                        ActiveLoans = _context.Loans.Count(l => l.LoanCode == lt.LoanCode &&
                                       l.CompanyCode == lt.CompanyCode &&
                                       (l.Status == (int)Status.Approved ||
                                        l.Status == (int)Status.Endorsed ||
                                        l.Status == (int)Status.Disbursed))
                     })
                    .ToListAsync();

                return loanTypes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting all loan types for company {companyCode}");
                throw;
            }
        }

        private async Task ValidateAccountsExist(LoanTypeCreateDTO loanTypeDto)
        {
            // Check if loan account exists (still required)
            if (!string.IsNullOrEmpty(loanTypeDto.LoanAcc))
            {
                var loanAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(a => a.Glaccname == loanTypeDto.LoanAcc &&
                                             a.CompanyCode == loanTypeDto.CompanyCode);
                if (loanAccount == null)
                    throw new ValidationException($"Loan account '{loanTypeDto.LoanAcc}' does not exist");
            }

            // Check if interest account exists (if provided)
            if (!string.IsNullOrEmpty(loanTypeDto.InterestAcc))
            {
                var interestAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(a => a.Glaccname == loanTypeDto.InterestAcc &&
                                             a.CompanyCode == loanTypeDto.CompanyCode);
                if (interestAccount == null)
                    throw new ValidationException($"Interest account '{loanTypeDto.InterestAcc}' does not exist");
            }

            // Check if penalty account exists (if provided)
            if (!string.IsNullOrEmpty(loanTypeDto.PenaltyAcc))
            {
                var penaltyAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(a => a.Glaccname == loanTypeDto.PenaltyAcc &&
                                             a.CompanyCode == loanTypeDto.CompanyCode);
                if (penaltyAccount == null)
                    throw new ValidationException($"Penalty account '{loanTypeDto.PenaltyAcc}' does not exist");
            }

            // Check if PP account exists (if provided) - NOW OPTIONAL
            if (!string.IsNullOrEmpty(loanTypeDto.Ppacc))
            {
                var ppAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(a => a.Glaccname == loanTypeDto.Ppacc &&
                                             a.CompanyCode == loanTypeDto.CompanyCode);
                if (ppAccount == null)
                    throw new ValidationException($"PP account '{loanTypeDto.Ppacc}' does not exist");
            }

            // Check if contra account exists (if provided) - NOW OPTIONAL
            if (!string.IsNullOrEmpty(loanTypeDto.ContraAccount))
            {
                var contraAccount = await _context.GlSetup
                    .FirstOrDefaultAsync(a => a.Glaccname == loanTypeDto.ContraAccount &&
                                             a.CompanyCode == loanTypeDto.CompanyCode);
                if (contraAccount == null)
                    throw new ValidationException($"Contra account '{loanTypeDto.ContraAccount}' does not exist");
            }
        }

        public async Task<RepaymentScheduleDTO> CalculateRepaymentScheduleAsync(
            string loanCode,
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod,
            string companyCode)
        {
            var schedule = new RepaymentScheduleDTO
            {
                LoanCode = loanCode,
                Principal = principal,
                TermMonths = termMonths,
                AnnualInterestRate = annualInterestRate,
                RepaymentMethod = repaymentMethod,
                Installments = new List<InstallmentDTO>()
            };

            decimal monthlyInterestRate = annualInterestRate / 12 / 100;
            decimal outstandingBalance = principal;

            if (repaymentMethod == "STL")
            {
                // STL Calculation - Fixed Principal
                decimal monthlyPrincipal = principal / termMonths;

                for (int month = 1; month <= termMonths; month++)
                {
                    decimal interest = outstandingBalance * monthlyInterestRate;
                    decimal totalPayment = monthlyPrincipal + interest;

                    schedule.Installments.Add(new InstallmentDTO
                    {
                        InstallmentNumber = month,
                        PrincipalPayment = monthlyPrincipal,
                        InterestPayment = interest,
                        TotalPayment = totalPayment,
                        OutstandingBalance = outstandingBalance - monthlyPrincipal
                    });

                    outstandingBalance -= monthlyPrincipal;
                }
            }
            else if (repaymentMethod == "AMT")
            {
                // AMT Calculation - Equal Installments
                decimal emi = principal * monthlyInterestRate *
                              (decimal)Math.Pow((double)(1 + monthlyInterestRate), termMonths) /
                              ((decimal)Math.Pow((double)(1 + monthlyInterestRate), termMonths) - 1);

                for (int month = 1; month <= termMonths; month++)
                {
                    decimal interest = outstandingBalance * monthlyInterestRate;
                    decimal principalPayment = emi - interest;

                    schedule.Installments.Add(new InstallmentDTO
                    {
                        InstallmentNumber = month,
                        PrincipalPayment = principalPayment,
                        InterestPayment = interest,
                        TotalPayment = emi,
                        OutstandingBalance = outstandingBalance - principalPayment
                    });

                    outstandingBalance -= principalPayment;
                }
            }
            else if (repaymentMethod == "RBAL")
            {
                // RBAL Calculation - Flexible, returns schedule showing interest only
                // Actual payment amounts will be determined during application
                for (int month = 1; month <= termMonths; month++)
                {
                    decimal interest = outstandingBalance * monthlyInterestRate;

                    schedule.Installments.Add(new InstallmentDTO
                    {
                        InstallmentNumber = month,
                        PrincipalPayment = 0, // To be determined during payment
                        InterestPayment = interest,
                        TotalPayment = interest, // Minimum payment (interest only)
                        OutstandingBalance = outstandingBalance,
                        IsFlexible = true,
                        MinimumPayment = interest
                    });

                    // Note: Balance doesn't reduce until principal is paid
                }
            }

            schedule.TotalInterest = schedule.Installments.Sum(i => i.InterestPayment);
            schedule.TotalRepayment = principal + schedule.TotalInterest;

            return schedule;
        }

        public async Task<decimal> CalculateMonthlyPaymentAsync(
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod)
        {
            decimal monthlyInterestRate = annualInterestRate / 12 / 100;

            if (repaymentMethod == "STL")
            {
                // For STL, payment varies each month, return average or first month
                decimal monthlyPrincipal = principal / termMonths;
                decimal firstMonthInterest = principal * monthlyInterestRate;
                return monthlyPrincipal + firstMonthInterest;
            }
            else if (repaymentMethod == "AMT")
            {
                // EMI calculation
                return principal * monthlyInterestRate *
                       (decimal)Math.Pow((double)(1 + monthlyInterestRate), termMonths) /
                       ((decimal)Math.Pow((double)(1 + monthlyInterestRate), termMonths) - 1);
            }
            else if (repaymentMethod == "RBAL")
            {
                // Minimum payment is interest only
                return principal * monthlyInterestRate;
            }

            return 0;
        }

        public async Task<decimal> CalculateOutstandingBalanceAsync(
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod,
            int monthsPaid)
        {
            decimal monthlyInterestRate = annualInterestRate / 12 / 100;
            decimal outstandingBalance = principal;

            if (repaymentMethod == "STL")
            {
                decimal monthlyPrincipal = principal / termMonths;
                outstandingBalance = principal - (monthlyPrincipal * monthsPaid);
            }
            else if (repaymentMethod == "AMT")
            {
                decimal emi = await CalculateMonthlyPaymentAsync(principal, termMonths, annualInterestRate, repaymentMethod);

                for (int i = 1; i <= monthsPaid; i++)
                {
                    decimal interest = outstandingBalance * monthlyInterestRate;
                    decimal principalPaid = emi - interest;
                    outstandingBalance -= principalPaid;
                }
            }
            else if (repaymentMethod == "RBAL")
            {
                // For RBAL, balance only reduces when principal is paid
                // This would need actual payment history
                return principal; // Placeholder
            }

            return Math.Max(0, outstandingBalance);
        }

        public async Task<decimal> CalculateInterestForPeriodAsync(
    decimal outstandingBalance,
    decimal annualInterestRate,
    int days)
        {
            // Simple daily interest calculation
            // This works for STL, AMT, and RBAL
            decimal dailyInterestRate = annualInterestRate / 365 / 100; // Convert percentage to decimal
            decimal interest = outstandingBalance * dailyInterestRate * days;

            return Math.Round(interest, 2);
        }

        private async Task CreateBlockchainTransaction(string transactionType, Loantype loanType, string user, object oldValues = null)
        {
            var blockchainData = new
            {
                LoanTypeCode = loanType.LoanCode,
                LoanTypeName = loanType.LoanType1,
                CompanyCode = loanType.CompanyCode,
                User = user,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Properties = new
                {
                    loanType.MaxAmount,
                    loanType.RepayPeriod,
                    loanType.Interest,
                    loanType.Bridging,
                    loanType.MobileLoan,
                    loanType.Priority,
                    loanType.Guarantor,
                    loanType.SelfGuarantee,
                    loanType.Processingfee,
                    loanType.GracePeriod,
                    loanType.Repaymethod,
                    loanType.ApprovalStatus
                },
                OldValues = oldValues
            };

            var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                transactionType,
                user ?? "SYSTEM",
                loanType.CompanyCode,
                0,
                loanType.LoanCode,
                blockchainData
            );

            if (blockchainTx != null)
            {
                _logger.LogInformation($"Blockchain transaction created: {blockchainTx.TransactionId}");
            }
        }
    }
}