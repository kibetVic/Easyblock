using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class LoanTypeMvcController : Controller
    {
        private readonly ILoanTypeService _loanTypeService;
        private readonly ILogger<LoanTypeMvcController> _logger;
        private readonly ICompanyContextService _companyContextService;
        private readonly ApplicationDbContext _context;

        public LoanTypeMvcController(
            ILoanTypeService loanTypeService,
            ILogger<LoanTypeMvcController> logger,
            ICompanyContextService companyContextService,
            ApplicationDbContext context)
        {
            _loanTypeService = loanTypeService;
            _logger = logger;
            _companyContextService = companyContextService;
            _context = context;
        }


        // GET: /LoanTypeMvc/LoanTypeManagement
        public async Task<IActionResult> LoanTypeManagement(string loanCode = null)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // Get company name from User claims or Session
                var companyName = User.FindFirst("CompanyName")?.Value ??
                                 HttpContext.Session.GetString("CompanyName") ??
                                 "Unknown Company";

                _logger.LogInformation($"Loading LoanTypeManagement for company: {companyCode} - {companyName}");

                // Get loan types list
                var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);
                ViewBag.LoanTypes = loanTypes;
                ViewBag.CompanyName = companyName;  // Pass company name to view
                ViewBag.CompanyCode = companyCode;  // Also pass company code if needed

                // Get available accounts for dropdowns
                var accounts = await GetAvailableAccounts(companyCode);

                if (accounts.Count == 0)
                {
                    TempData["InfoMessage"] = $"No GL accounts found for {companyName}. Please create GL accounts first.";
                }

                ViewBag.AvailableAccounts = new SelectList(accounts, "Value", "Text");
                ViewBag.InterestAccounts = new SelectList(accounts, "Value", "Text");
                ViewBag.PenaltyAccounts = new SelectList(accounts, "Value", "Text");
                ViewBag.PpAccounts = new SelectList(accounts, "Value", "Text");
                ViewBag.ContraAccounts = new SelectList(accounts, "Value", "Text");

                // Value chain options
                ViewBag.ValueChainOptions = GetValueChainOptions();

                // If editing an existing loan type
                if (!string.IsNullOrEmpty(loanCode))
                {
                    var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loanCode, companyCode);
                    var usageCount = await _loanTypeService.GetLoanTypeUsageCountAsync(loanCode, companyCode);

                    ViewBag.UsageCount = usageCount;
                    ViewBag.CanEditCritical = usageCount == 0;
                    ViewBag.CanDelete = usageCount == 0;
                    ViewBag.IsPending = loanType.ApprovalStatus == "Pending";

                    return View("LoanTypeManagement", loanType);
                }

                // New loan type
                return View("LoanTypeManagement", new LoanTypeResponseDTO());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading loan type management page");
                TempData["ErrorMessage"] = "An error occurred while loading the page.";
                return View("LoanTypeManagement", new LoanTypeResponseDTO());
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LoanTypeCreateDTO loanTypeDto)
        {
            try
            {
                _logger.LogInformation("Creating loan type");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid for loan type creation");
                    await ReloadViewBagForManagement(loanTypeDto.CompanyCode);
                    return View("LoanTypeManagement", new LoanTypeResponseDTO());
                }

                // Set user information
                loanTypeDto.CompanyCode = GetUserCompanyCode();
                loanTypeDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanTypeService.CreateLoanTypeAsync(loanTypeDto);

                TempData["SuccessMessage"] = $"Loan type '{result.LoanType}' created successfully! It is currently PENDING and needs to be approved before it can be used.";
                return RedirectToAction("LoanTypeManagement");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating loan type");

                if (ex.Message.Contains("already exists") ||
                    ex.Message.Contains("Validation error") ||
                    ex.Message.Contains("required") ||
                    ex.Message.Contains("does not exist"))
                {
                    ModelState.AddModelError("", ex.Message);
                }
                else
                {
                    ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                }

                await ReloadViewBagForManagement(loanTypeDto.CompanyCode);
                return View("LoanTypeManagement", new LoanTypeResponseDTO());
            }
        }

        // POST: /LoanTypeMvc/Edit/{loanCode}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string loanCode, LoanTypeUpdateDTO loanTypeDto)
        {
            try
            {
                _logger.LogInformation($"Updating loan type: {loanCode}");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid for loan type update");
                    await ReloadViewBagForManagement(loanTypeDto.CompanyCode);
                    return View("LoanTypeManagement", new LoanTypeResponseDTO { LoanCode = loanCode });
                }

                loanTypeDto.CompanyCode = GetUserCompanyCode();
                loanTypeDto.UpdatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanTypeService.UpdateLoanTypeAsync(loanCode, loanTypeDto);

                TempData["SuccessMessage"] = $"Loan type '{result.LoanType}' updated successfully! It has been reset to PENDING status and needs to be approved again.";
                return RedirectToAction("LoanTypeManagement");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating loan type {loanCode}");

                if (ex.Message.Contains("not found") ||
                    ex.Message.Contains("Validation error") ||
                    ex.Message.Contains("Cannot change") ||
                    ex.Message.Contains("in use"))
                {
                    ModelState.AddModelError("", ex.Message);
                }
                else
                {
                    ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                }

                await ReloadViewBagForManagement(loanTypeDto.CompanyCode);
                return View("LoanTypeManagement", new LoanTypeResponseDTO { LoanCode = loanCode });
            }
        }

        // POST: /LoanTypeMvc/Approve/{loanCode}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string loanCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var userName = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanTypeService.ApproveLoanTypeAsync(loanCode, companyCode, userName);

                TempData["SuccessMessage"] = $"Loan type '{result.LoanType}' approved successfully! It is now ACTIVE and available for use.";
                return RedirectToAction("LoanTypeManagement");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error approving loan type {loanCode}");
                TempData["ErrorMessage"] = $"Error approving loan type: {ex.Message}";
                return RedirectToAction("LoanTypeManagement");
            }
        }

        // POST: /LoanTypeMvc/Delete/{loanCode}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string loanCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                await _loanTypeService.DeleteLoanTypeAsync(loanCode, companyCode);

                TempData["SuccessMessage"] = $"Loan type '{loanCode}' deleted successfully!";
                return RedirectToAction("LoanTypeManagement");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting loan type {loanCode}");

                if (ex.Message.Contains("in use") || ex.Message.Contains("not found"))
                {
                    TempData["ErrorMessage"] = ex.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                }

                return RedirectToAction("LoanTypeManagement");
            }
        }

        // GET: /LoanTypeMvc/Details/{loanCode}
        public async Task<IActionResult> Details(string loanCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loanCode, companyCode);
                var usageCount = await _loanTypeService.GetLoanTypeUsageCountAsync(loanCode, companyCode);

                ViewBag.UsageCount = usageCount;
                ViewBag.CanDelete = usageCount == 0;

                return View(loanType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading loan type details {loanCode}");
                TempData["ErrorMessage"] = $"Error loading loan type: {ex.Message}";
                return RedirectToAction("LoanTypeManagement");
            }
        }

        // GET: /LoanTypeMvc/SearchResults
        [HttpGet]
        public async Task<IActionResult> SearchResults(string searchTerm)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loanTypes = await _loanTypeService.SearchLoanTypesAsync(searchTerm, companyCode);

                ViewBag.SearchTerm = searchTerm;
                ViewBag.ResultCount = loanTypes.Count;
                ViewBag.LoanTypes = loanTypes;

                return View(loanTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching loan types with term: {searchTerm}");
                TempData["ErrorMessage"] = "An error occurred while searching";
                return RedirectToAction("LoanTypeManagement");
            }
        }

        // GET: /LoanTypeMvc/Statistics
        public async Task<IActionResult> Statistics()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var statistics = await _loanTypeService.GetLoanTypeStatisticsAsync(companyCode);
                var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);

                ViewBag.LoanTypes = loanTypes;
                return View(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading statistics");
                TempData["ErrorMessage"] = "An error occurred while loading statistics";
                return RedirectToAction("LoanTypeManagement");
            }
        }

        // GET: /LoanTypeMvc/Export
        public async Task<IActionResult> Export()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loanTypes = await _loanTypeService.GetAllLoanTypesAsync(companyCode);

                // Generate CSV
                var csv = GenerateCsv(loanTypes);
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

                return File(bytes, "text/csv", $"LoanTypes_{DateTime.Now:yyyyMMdd}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting loan types");
                TempData["ErrorMessage"] = "An error occurred while exporting data";
                return RedirectToAction("LoanTypeManagement");
            }
        }

        // GET: /LoanTypeMvc/CheckDuplicate
        [HttpGet]
        public async Task<IActionResult> CheckDuplicate(string loanCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var existing = await _loanTypeService.GetLoanTypeByCodeAsync(loanCode, companyCode);
                return Json(new { exists = existing != null });
            }
            catch
            {
                return Json(new { exists = false });
            }
        }

        #region Private Methods

        private string GetUserCompanyCode()
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();
            if (string.IsNullOrEmpty(companyCode))
            {
                companyCode = HttpContext.Session.GetString("CompanyCode");
            }

            if (string.IsNullOrEmpty(companyCode))
            {
                throw new Exception("Company code not found. Please log in again.");
            }

            return companyCode;
        }

        private async Task<List<SelectListItem>> GetAvailableAccounts(string companyCode)
        {
            try
            {
                var accounts = await _context.GlSetup
                    .Where(a => a.CompanyCode == companyCode && a.Status == true)
                    .OrderBy(a => a.Glaccname)
                    .Select(a => new SelectListItem
                    {
                        Value = a.Glaccname,  // This should be the account code/number
                        Text = a.Glaccname + " - " + a.Glaccname  // This is redundant
                    })
                    .ToListAsync();

                return accounts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching GL accounts for company {companyCode}");
                return new List<SelectListItem>();
            }
        }

        private List<SelectListItem> GetValueChainOptions()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "Agriculture loan", Text = "Agriculture loan" },
                new SelectListItem { Value = "Normal loans", Text = "Normal loans" },
                new SelectListItem { Value = "Business loan", Text = "Business loan" }
            };
        }

        private async Task ReloadViewBagForManagement(string companyCode)
        {
            var accounts = await GetAvailableAccounts(companyCode);
            ViewBag.AvailableAccounts = new SelectList(accounts, "Value", "Text");
            ViewBag.InterestAccounts = new SelectList(accounts, "Value", "Text");
            ViewBag.PenaltyAccounts = new SelectList(accounts, "Value", "Text");
            ViewBag.PpAccounts = new SelectList(accounts, "Value", "Text");
            ViewBag.ContraAccounts = new SelectList(accounts, "Value", "Text");
            ViewBag.ValueChainOptions = GetValueChainOptions();

            var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);
            ViewBag.LoanTypes = loanTypes;
            ViewBag.TotalLoanTypes = loanTypes.Count;
            ViewBag.ActiveLoanTypes = loanTypes.Count(lt => lt.ApprovalStatus == "Active");
            ViewBag.PendingLoanTypes = loanTypes.Count(lt => lt.ApprovalStatus == "Pending");
            ViewBag.TotalLoans = loanTypes.Sum(lt => lt.TotalLoans);
            ViewBag.TotalLoanAmount = loanTypes.Sum(lt => lt.TotalLoanAmount);
        }

        private string GenerateCsv(dynamic loanTypes)
        {
            var sb = new System.Text.StringBuilder();

            // Header
            sb.AppendLine("Loan Code,Loan Type,Max Amount,Repay Period,Interest,Priority,Status,Total Loans,Active Loans");

            // Data
            foreach (var lt in loanTypes)
            {
                sb.AppendLine($"{lt.LoanCode},{lt.LoanType1},{lt.MaxAmount},{lt.RepayPeriod},{lt.Interest},{lt.Priority},{lt.ApprovalStatus},{lt.TotalLoans},{lt.ActiveLoans}");
            }

            return sb.ToString();
        }

        #endregion
    }
}