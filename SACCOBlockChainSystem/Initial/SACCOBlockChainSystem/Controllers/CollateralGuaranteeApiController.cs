using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class CollateralGuaranteeApiController : ControllerBase
    {
        private readonly ILoanService _loanService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CollateralGuaranteeApiController> _logger;

        public CollateralGuaranteeApiController(
            ILoanService loanService,
            ApplicationDbContext context,
            ILogger<CollateralGuaranteeApiController> logger)
        {
            _loanService = loanService;
            _context = context;
            _logger = logger;
        }

        [HttpGet("member-collaterals")]
        public async Task<IActionResult> GetMemberCollaterals(string memberNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Getting collaterals for member: {memberNo}");

                // Get all collateral types
                var collateralTypes = await _context.Collaterals
                    .Where(c => c.CompanyCode == companyCode)
                    .Select(c => new
                    {
                        c.ColCode,
                        c.Coldescription,
                        c.Percentage
                    })
                    .ToListAsync();

                // Get active collateral guarantees for this member
                var activeGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.MemberNo == memberNo &&
                                cg.CompanyCode == companyCode &&
                                cg.Balance > 0)
                    .ToListAsync();

                // Get active loans for those guarantees
                var activeLoanNos = activeGuarantees.Select(cg => cg.LoanNo).Distinct().ToList();
                var activeLoans = await _context.Loans
                    .Where(l => activeLoanNos.Contains(l.LoanNo) &&
                               l.CompanyCode == companyCode &&
                               l.Status != (int)Status.Closed &&
                               l.Status != (int)Status.Rejected &&
                               l.Status != (int)Status.WrittenOff)
                    .Select(l => l.LoanNo)
                    .ToListAsync();

                var activeGuaranteesForActiveLoans = activeGuarantees
                    .Where(cg => activeLoans.Contains(cg.LoanNo))
                    .ToList();

                // Group by ColCode to get total used amount
                var usedAmountByColCode = activeGuaranteesForActiveLoans
                    .GroupBy(cg => cg.ColCode)
                    .ToDictionary(g => g.Key, g => g.Sum(cg => cg.Balance));

                var result = new List<object>();

                // For now, we need to know what collaterals the member actually owns
                // This would come from a MemberCollateral table
                // For demonstration, we'll show all collateral types with usage info

                foreach (var collateral in collateralTypes)
                {
                    var usedAmount = usedAmountByColCode.GetValueOrDefault(collateral.ColCode, 0);

                    result.Add(new
                    {
                        colCode = collateral.ColCode,
                        description = collateral.Coldescription,
                        percentage = collateral.Percentage,
                        currentlyUsed = usedAmount,
                        // These would need actual member collateral values
                        marketValue = 0,
                        maxGuaranteeAmount = 0,
                        availableAmount = 0,
                        docNo = ""
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member collaterals");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("assign")]
        public async Task<IActionResult> AssignCollateral([FromBody] CollateralGuaranteeDTO guaranteeDto)
        {
            try
            {
                _logger.LogInformation($"Assigning collateral guarantee for loan {guaranteeDto.LoanNo}");

                var result = await _loanService.AssignCollateralGuaranteeAsync(guaranteeDto, User.Identity?.Name ?? "SYSTEM");

                // Get the updated loan to return status
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == guaranteeDto.LoanNo);

                return Ok(new
                {
                    success = true,
                    message = "Collateral guarantee assigned successfully",
                    data = new
                    {
                        id = result.Id,
                        colCode = result.ColCode,
                        docNo = result.DocNo,
                        guaranteeAmount = result.Balance,
                        loanStatus = loan?.Status,
                        isFullyGuaranteed = loan != null && (loan.LoanAmt ?? 0) <= (await _context.ColloanGuars.Where(cg => cg.LoanNo == guaranteeDto.LoanNo && cg.Balance > 0).SumAsync(cg => cg.Balance) + await _context.Loanguar.Where(lg => lg.LoanNo == guaranteeDto.LoanNo && lg.Transfered == false).SumAsync(lg => lg.Amount ?? 0))
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning collateral guarantee");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateCollateral(string memberNo, string colCode, string docNo, decimal marketValue, string loanNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Validating collateral {colCode} (Doc: {docNo}, Value: {marketValue:C}) for loan {loanNo}");

                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                    return Ok(new { success = false, message = "Loan not found" });

                var collateral = await _context.Collaterals
                    .FirstOrDefaultAsync(c => c.ColCode == colCode && c.CompanyCode == companyCode);

                if (collateral == null)
                    return Ok(new { success = false, message = "Collateral type not found" });

                // Check if this document is already used
                var existingGuarantee = await _context.ColloanGuars
                    .FirstOrDefaultAsync(cg => cg.DocNo == docNo &&
                                              cg.MemberNo == memberNo &&
                                              cg.Balance > 0 &&
                                              cg.CompanyCode == companyCode);

                if (existingGuarantee != null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"This document (Doc No: {docNo}) is already used to guarantee loan {existingGuarantee.LoanNo}"
                    });
                }

                // Calculate maximum guarantee amount based on collateral percentage
                decimal maxGuaranteeAmount = marketValue * (decimal)(collateral.Percentage / 100);

                // Get existing guarantees for this loan
                var existingCollateralGuarantee = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                var existingMemberGuarantee = await _context.Loanguar
                    .Where(lg => lg.LoanNo == loanNo && lg.Transfered == false)
                    .SumAsync(lg => lg.Amount ?? 0);

                var totalExistingGuarantee = existingCollateralGuarantee + existingMemberGuarantee;
                var remainingLoanAmount = (loan.LoanAmt ?? 0) - totalExistingGuarantee;
                var maxGuaranteeForLoan = Math.Min(maxGuaranteeAmount, remainingLoanAmount);

                if (maxGuaranteeForLoan <= 0)
                {
                    string reason = remainingLoanAmount <= 0
                        ? "Loan is already fully guaranteed"
                        : "Collateral value insufficient to cover remaining loan amount";

                    return Ok(new { success = false, message = reason });
                }

                return Ok(new
                {
                    success = true,
                    message = $"Collateral can guarantee up to KES {maxGuaranteeForLoan:N0}",
                    data = new
                    {
                        colCode = collateral.ColCode,
                        description = collateral.Coldescription,
                        percentage = collateral.Percentage,
                        marketValue = marketValue,
                        maxGuaranteeAmount = maxGuaranteeAmount,
                        maxGuaranteeForLoan = maxGuaranteeForLoan,
                        remainingLoanAmount = remainingLoanAmount,
                        totalExistingGuarantee = totalExistingGuarantee
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating collateral");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("check-document")]
        public async Task<IActionResult> CheckDocumentExists(string colCode, string docNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Checking if document exists - ColCode: {colCode}, DocNo: {docNo}");

                var existingGuarantee = await _context.ColloanGuars
                    .FirstOrDefaultAsync(cg => cg.ColCode == colCode &&
                                               cg.DocNo == docNo &&
                                               cg.CompanyCode == companyCode &&
                                               cg.Balance > 0);

                if (existingGuarantee != null)
                {
                    return Ok(new
                    {
                        exists = true,
                        loanNo = existingGuarantee.LoanNo,
                        message = $"Document {docNo} is already used for loan {existingGuarantee.LoanNo}"
                    });
                }

                return Ok(new { exists = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking document existence");
                return Ok(new { exists = false, error = ex.Message });
            }
        }
    }
}