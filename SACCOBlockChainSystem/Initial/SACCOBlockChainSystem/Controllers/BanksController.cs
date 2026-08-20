using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class BankController : Controller
    {
        private readonly IBankService _bankService;
        private readonly ILogger<BankController> _logger;

        public BankController(IBankService bankService, ILogger<BankController> logger)
        {
            _bankService = bankService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string search, bool showInactive = false)
        {
            try
            {
                ViewBag.CurrentSearch = search;
                ViewBag.ShowInactive = showInactive;

                var banks = await _bankService.GetAllBanksAsync(search, showInactive);
                return View(banks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading banks");
                TempData["ErrorMessage"] = "Error loading banks. Please try again.";
                return View(new List<BankResponseDTO>());
            }
        }

        // NEW: Get GL Accounts for dropdown
        [HttpGet]
        public async Task<IActionResult> GetGlAccounts()
        {
            try
            {
                var glAccounts = await _bankService.GetGlAccountsForDropdownAsync();
                return Json(new { success = true, data = glAccounts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting GL accounts");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] BankDTO bankDto)
        {
            try
            {
                _logger.LogInformation("Create bank called");

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage);
                    var errorMessage = string.Join(", ", errors);
                    _logger.LogWarning($"Model state invalid: {errorMessage}");
                    return Json(new { success = false, message = errorMessage });
                }

                var bank = await _bankService.CreateBankAsync(bankDto);
                _logger.LogInformation($"Bank created successfully: {bank.BankCode}");
                return Json(new { success = true, message = $"Bank {bank.BankCode} created successfully.", bank });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bank");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromForm] BankDTO bankDto, int id)
        {
            try
            {
                _logger.LogInformation($"Edit bank called with ID: {id}");

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage);
                    var errorMessage = string.Join(", ", errors);
                    _logger.LogWarning($"Model state invalid: {errorMessage}");
                    return Json(new { success = false, message = errorMessage });
                }

                var bank = await _bankService.UpdateBankAsync(id, bankDto);
                _logger.LogInformation($"Bank updated successfully: {bank.BankCode}");
                return Json(new { success = true, message = $"Bank {bank.BankCode} updated successfully.", bank });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bank");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBankDetails(int id)
        {
            try
            {
                var bank = await _bankService.GetBankByIdAsync(id);
                if (bank == null)
                {
                    return Json(new { success = false, message = "Bank not found." });
                }

                return Json(new { success = true, bank });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bank details");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            try
            {
                await _bankService.ToggleBankStatusAsync(id);
                return Json(new { success = true, message = "Bank status updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling bank status");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _bankService.DeleteBankAsync(id);
                return Json(new { success = true, message = "Bank deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting bank");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}