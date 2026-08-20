using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoanApiController : ControllerBase
    {
        private readonly ILoanService _loanService;
        private readonly IMemberService _memberService;
        private readonly IShareService _shareService;
        private readonly ILoanTypeService _loanTypeService;
        private readonly ILogger<LoanApiController> _logger;
        private readonly ApplicationDbContext _context;  

        public LoanApiController(
            ILoanService loanService,
            IMemberService memberService,
            IShareService shareService,
            ILoanTypeService loanTypeService,
            ILogger<LoanApiController> logger,
            ApplicationDbContext context)  
        {
            _loanService = loanService;
            _memberService = memberService;
            _shareService = shareService;
            _loanTypeService = loanTypeService;
            _logger = logger;
            _context = context;  
        }

        [HttpGet("validate-guarantor")]
        public async Task<IActionResult> ValidateGuarantor(string memberNo, string loanNo, string companyCode)
        {
            try
            {
                _logger.LogInformation($"Validating guarantor - MemberNo: {memberNo}, LoanNo: {loanNo}, CompanyCode: {companyCode}");

                // Get the loan details
                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);
                if (loan == null)
                {
                    return Ok(new { success = false, message = "Loan not found" });
                }

                // Get guarantor member details
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                if (member == null)
                {
                    return Ok(new { success = false, message = "Guarantor member not found" });
                }

                // Check if member is active
                bool isWithdrawn = member.Withdrawn ?? false;
                bool isArchived = member.Archived ?? false;
                int isDormant = member.Dormant ?? 0;

                if (isWithdrawn || isArchived || isDormant == 1)
                {
                    return Ok(new { success = false, message = "Guarantor is not an active member" });
                }

                // Get loan type to check if self guarantee is allowed
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                bool isSelfGuarantee = loanType?.SelfGuarantee ?? false;

                _logger.LogInformation($"Loan Type: {loan.LoanCode}, SelfGuarantee: {isSelfGuarantee}");

                // Check if trying to add self as guarantor
                if (memberNo == loan.MemberNo)
                {
                    if (!isSelfGuarantee)
                    {
                        return Ok(new { success = false, message = "The loan applicant cannot be a guarantor for their own loan. Self guarantee is not allowed for this loan type." });
                    }
                    _logger.LogInformation($"Self guarantee is allowed for this loan type. Proceeding with validation.");
                }

                // ============================================================
                // FIX: GET TOTAL DEPOSITS FROM CONTRIBSHARE TABLE
                // ============================================================
                decimal totalDeposits = await _context.ContribShares
                    .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                    .SumAsync(cs => cs.DepositsAmount ?? 0);

                _logger.LogInformation($"Member {memberNo}: Total DepositsAmount = {totalDeposits:C}");

                if (totalDeposits <= 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"Member has no savings/deposits. Total deposits: {totalDeposits:C}. Please make a deposit first before becoming a guarantor."
                    });
                }

                // 2. CHECK MINIMUM REQUIREMENT
                var saccoParams = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

                var minDepositRequirement = saccoParams?.MinGuarantor ?? 0;

                if (minDepositRequirement > 0 && totalDeposits < minDepositRequirement)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"Member's deposits ({totalDeposits:C}) is below minimum requirement of {minDepositRequirement:C}"
                    });
                }

                // 3. CHECK EXISTING GUARANTEES (deposits already locked for other loans)
                var existingGuarantees = await _context.Loanguar
                    .Where(g => g.MemberNo == memberNo &&
                               g.CompanyCode == companyCode &&
                               g.Transfered == false &&
                               (g.Balance > 0 || (g.Amount > 0 && g.Balance == null)))
                    .SumAsync(g => g.Amount ?? 0);

                // For self guarantee, exclude the current loan's guarantee from existing
                if (memberNo == loan.MemberNo)
                {
                    var currentLoanGuarantee = await _context.Loanguar
                        .Where(g => g.LoanNo == loanNo && g.MemberNo == memberNo && g.Transfered == false)
                        .SumAsync(g => g.Amount ?? 0);
                    existingGuarantees -= currentLoanGuarantee;
                    if (existingGuarantees < 0) existingGuarantees = 0;
                }

                var availableDeposits = totalDeposits - existingGuarantees;

                _logger.LogInformation($"Total Deposits: {totalDeposits:C}, Existing Guarantees: {existingGuarantees:C}, Available: {availableDeposits:C}");

                if (availableDeposits <= 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"All eligible deposits are already locked as guarantees. Total deposits: {totalDeposits:C}, Already guaranteeing: {existingGuarantees:C}, Available: {availableDeposits:C}"
                    });
                }

                // 4. CHECK IF GUARANTEE AMOUNT CAN BE COVERED (1:1 ratio - no multiplication)
                // Calculate remaining loan amount that needs guarantee
                var existingLoanGuarantees = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var currentLoanGuaranteeTotal = existingLoanGuarantees.Sum(g => g.GuaranteeAmount);

                // For self guarantee, exclude self from current total if we're recalculating
                if (memberNo == loan.MemberNo)
                {
                    var selfGuaranteeAmount = existingLoanGuarantees
                        .Where(g => g.GuarantorMemberNo == memberNo)
                        .Sum(g => g.GuaranteeAmount);
                    currentLoanGuaranteeTotal -= selfGuaranteeAmount;
                }

                var remainingLoanAmount = (loan.LoanAmt ?? 0) - currentLoanGuaranteeTotal;
                var maxGuarantee = Math.Min(availableDeposits, remainingLoanAmount);

                if (maxGuarantee <= 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"Loan is already fully guaranteed. Remaining: KES {remainingLoanAmount:N0}"
                    });
                }

                // Fix: Handle null values safely for member name
                var memberName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
                if (string.IsNullOrEmpty(memberName))
                {
                    memberName = member.MemberNo;
                }

                var responseData = new
                {
                    memberNo = member.MemberNo,
                    name = memberName,
                    idNo = member.Idno ?? "N/A",
                    // FIXED: Use totalDeposits instead of totalEligibleShares
                    totalDeposits = totalDeposits,
                    lockedAmount = existingGuarantees,
                    availableAmount = availableDeposits,
                    maxGuarantee = maxGuarantee,
                    currentLoanGuaranteeTotal = currentLoanGuaranteeTotal,
                    loanAmount = (loan.LoanAmt ?? 0),
                    remainingLoanAmount = remainingLoanAmount,
                    isSelfGuarantee = memberNo == loan.MemberNo
                };

                string successMessage = memberNo == loan.MemberNo && isSelfGuarantee
                    ? "Self guarantee is allowed. You can use your savings/deposits to guarantee this loan."
                    : $"Member is eligible to guarantee up to KES {maxGuarantee:N0} using their savings/deposits (1:1 ratio)";

                return Ok(new
                {
                    success = true,
                    message = successMessage,
                    data = responseData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating guarantor - MemberNo: {memberNo}, LoanNo: {loanNo}");
                return Ok(new { success = false, message = $"An error occurred while validating guarantor: {ex.Message}" });
            }
        }

        [HttpGet("get-max-guarantors")]
        public async Task<IActionResult> GetMaxGuarantors(string companyCode)
        {
            try
            {
                // This would come from your SaccoParram service
                // For now, return default
                return Ok(new { success = true, maxGuarantors = 5 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting max guarantors");
                return Ok(new { success = false, message = ex.Message });
            }
        }


        [HttpGet("check-eligibility")]
        public async Task<IActionResult> CheckEligibility(string memberNo, string companyCode)
        {
            try
            {
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                if (member == null)
                {
                    return Ok(new { success = false, message = "Member not found" });
                }

                var totalShares = await _shareService.GetTotalSharesValueAsync(memberNo);
                var existingGuarantees = await _loanService.GetGuarantorTotalGuaranteesAsync(memberNo, companyCode);
                var availableShares = totalShares - existingGuarantees;

                var memberName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
                if (string.IsNullOrEmpty(memberName))
                {
                    memberName = member.MemberNo;
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        memberNo = member.MemberNo,
                        name = memberName,
                        idNo = member.Idno ?? "N/A",
                        totalShares = totalShares,
                        existingGuarantees = existingGuarantees,
                        availableShares = availableShares,
                        isActive = !(member.Withdrawn ?? false) && !(member.Archived ?? false) && (member.Dormant ?? 0) != 1
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking eligibility");
                return Ok(new { success = false, message = ex.Message });
            }
        }
    }
}