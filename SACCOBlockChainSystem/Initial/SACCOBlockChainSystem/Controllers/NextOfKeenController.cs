using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class NextOfKeenController : Controller
    {
        private readonly INextOfKeenService _nextOfKeenService;
        private readonly IMemberService _memberService;
        private readonly IBlockchainService _blockchainService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<NextOfKeenController> _logger;

        public NextOfKeenController(
            INextOfKeenService nextOfKeenService,
            IMemberService memberService,
            IBlockchainService blockchainService,
            ICompanyContextService companyContextService,
            ILogger<NextOfKeenController> logger)
        {
            _nextOfKeenService = nextOfKeenService;
            _memberService = memberService;
            _blockchainService = blockchainService;
            _companyContextService = companyContextService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string memberNo)
        {
            try
            {
                if (string.IsNullOrEmpty(memberNo))
                {
                    return View(new List<NextOfKeenResponseDTO>());
                }

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);

                if (member == null)
                {
                    TempData["ErrorMessage"] = "Member not found";
                    return RedirectToAction("Index", "MemberMvc");
                }

                var nextOfKeens = await _nextOfKeenService.GetNextOfKeensByMemberAsync(memberNo, companyCode);

                ViewBag.MemberNo = memberNo;
                ViewBag.MemberName = $"{member.Surname} {member.OtherNames}";
                ViewBag.MemberIdNo = member.Idno;
                ViewBag.MemberPhone = member.PhoneNo;
                ViewBag.TotalPercentage = nextOfKeens.Sum(n => n.BenefitPercentage ?? 0);

                return View(nextOfKeens);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading next of kin list");
                TempData["ErrorMessage"] = "Error loading next of kin list";
                return RedirectToAction("Index", "MemberMvc");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetByMember(string memberNo)
        {
            try
            {
                if (string.IsNullOrEmpty(memberNo))
                {
                    return Json(new { success = false, message = "Member number is required" });
                }

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var nextOfKeens = await _nextOfKeenService.GetNextOfKeensByMemberAsync(memberNo, companyCode);
                return Json(new { success = true, data = nextOfKeens });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next of kin by member");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] NextOfKeenDTO dto)
        {
            try
            {
                // Check if MemberNo is provided in the DTO
                if (dto == null || string.IsNullOrEmpty(dto.MemberNo))
                {
                    return Json(new { success = false, message = "Member number is required" });
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join(", ", errors) });
                }

                // Get current total benefit percentage to show in error message if needed
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var currentTotal = await _nextOfKeenService.GetTotalBenefitPercentageAsync(dto.MemberNo, companyCode);
                var newPercentage = dto.BenefitPercentage ?? 0;

                if (currentTotal + newPercentage > 100)
                {
                    var remaining = 100 - currentTotal;
                    return Json(new
                    {
                        success = false,
                        message = $"Cannot add. Total benefit percentage would exceed 100%. " +
                                 $"Current total: {currentTotal:F2}%, Available: {remaining:F2}%, " +
                                 $"Requested: {newPercentage:F2}%"
                    });
                }

                var nextOfKeen = await _nextOfKeenService.CreateNextOfKeenAsync(
                    dto.MemberNo,
                    dto,
                    User.Identity?.Name ?? "SYSTEM");

                // Verify blockchain transaction was recorded
                if (!string.IsNullOrEmpty(nextOfKeen.BlockchainTxId))
                {
                    var isVerified = await _blockchainService.VerifyTransactionAsync(nextOfKeen.BlockchainTxId);
                    if (isVerified)
                    {
                        _logger.LogInformation($"Blockchain transaction verified for next of kin: {nextOfKeen.BlockchainTxId}");
                    }
                }

                // Get updated total percentage
                var newTotal = await _nextOfKeenService.GetTotalBenefitPercentageAsync(dto.MemberNo, companyCode);

                return Json(new
                {
                    success = true,
                    message = $"Next of kin {nextOfKeen.FullName} added successfully! Total benefit allocation: {newTotal:F2}%",
                    totalPercentage = newTotal
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validation error creating next of kin");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating next of kin");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromBody] NextOfKeenDTO dto)
        {
            try
            {
                if (dto == null)
                {
                    return Json(new { success = false, message = "Invalid data" });
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join(", ", errors) });
                }

                // Get the existing record to know the member number
                var existingRecord = await _nextOfKeenService.GetNextOfKeenByIdAsync(id);
                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "Next of kin record not found" });
                }

                // Get current total excluding this record
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var currentTotalExcludingThis = await _nextOfKeenService.GetTotalBenefitPercentageAsync(
                    existingRecord.MemberNo, companyCode, id);

                var newPercentage = dto.BenefitPercentage ?? 0;

                if (currentTotalExcludingThis + newPercentage > 100)
                {
                    var remaining = 100 - currentTotalExcludingThis;
                    return Json(new
                    {
                        success = false,
                        message = $"Cannot update. Total benefit percentage would exceed 100%. " +
                                 $"Current total (excluding this record): {currentTotalExcludingThis:F2}%, " +
                                 $"Available: {remaining:F2}%, Requested: {newPercentage:F2}%"
                    });
                }

                var nextOfKeen = await _nextOfKeenService.UpdateNextOfKeenAsync(
                    id,
                    dto,
                    User.Identity?.Name ?? "SYSTEM");

                // Verify blockchain transaction was recorded
                if (!string.IsNullOrEmpty(nextOfKeen.BlockchainTxId))
                {
                    var isVerified = await _blockchainService.VerifyTransactionAsync(nextOfKeen.BlockchainTxId);
                    if (isVerified)
                    {
                        _logger.LogInformation($"Blockchain transaction verified for next of kin update: {nextOfKeen.BlockchainTxId}");
                    }
                }

                // Get updated total percentage
                var newTotal = await _nextOfKeenService.GetTotalBenefitPercentageAsync(existingRecord.MemberNo, companyCode);

                return Json(new
                {
                    success = true,
                    message = $"Next of kin {nextOfKeen.FullName} updated successfully! Total benefit allocation: {newTotal:F2}%",
                    totalPercentage = newTotal
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validation error updating next of kin");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating next of kin");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _nextOfKeenService.DeleteNextOfKeenAsync(id);
                return Json(new { success = true, message = "Next of kin removed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting next of kin");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPrimary(int id, string memberNo)
        {
            try
            {
                if (string.IsNullOrEmpty(memberNo))
                {
                    return Json(new { success = false, message = "Member number is required" });
                }

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);

                if (member == null)
                {
                    return Json(new { success = false, message = "Member not found" });
                }

                await _nextOfKeenService.SetPrimaryNextOfKeenAsync(id, memberNo, companyCode);
                return Json(new { success = true, message = "Primary next of kin set successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting primary next of kin");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBenefitPercentages(string memberNo, [FromBody] List<BenefitPercentageUpdateDTO> updates)
        {
            try
            {
                if (string.IsNullOrEmpty(memberNo))
                {
                    return Json(new { success = false, message = "Member number is required" });
                }

                var result = await _nextOfKeenService.UpdateBenefitPercentagesAsync(memberNo, updates);
                if (result)
                {
                    return Json(new { success = true, message = "Benefit percentages updated successfully" });
                }
                return Json(new { success = false, message = "Failed to update benefit percentages" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating benefit percentages");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBlockchainStatus(string transactionId)
        {
            try
            {
                if (string.IsNullOrEmpty(transactionId))
                {
                    var status = await _blockchainService.GetBlockchainStatus();
                    return Json(new { success = true, status });
                }
                else
                {
                    var transaction = await _blockchainService.GetTransactionAsync(transactionId);
                    if (transaction == null)
                    {
                        return Json(new { success = false, message = "Transaction not found" });
                    }

                    var isVerified = await _blockchainService.VerifyTransactionAsync(transactionId);
                    return Json(new
                    {
                        success = true,
                        transaction = new
                        {
                            transaction.TransactionId,
                            transaction.TransactionType,
                            transaction.MemberNo,
                            transaction.Amount,
                            transaction.Timestamp,
                            transaction.Status,
                            transaction.BlockHash,
                            IsVerified = isVerified
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blockchain status");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}