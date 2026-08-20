using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("GlSetup")]
    public class GlSetupController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GlSetupController> _logger;

        public GlSetupController(ApplicationDbContext context, ILogger<GlSetupController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Helper method to get current user's company code from claims
        private string GetCurrentCompanyCode()
        {
            var companyCode = User.FindFirstValue("CompanyCode");
            if (string.IsNullOrEmpty(companyCode))
            {
                // Try to get from claims identity
                companyCode = User.Claims.FirstOrDefault(c => c.Type == "CompanyCode")?.Value;
            }
            return companyCode ?? "000"; // Default fallback
        }

        // Helper method to get current user's name
        private string GetCurrentUserName()
        {
            return User.Identity?.Name ?? "SYSTEM";
        }

        // ===============================
        // GET: /GlSetup
        // ===============================
        [HttpGet("")]
        public IActionResult Index()
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();
                _logger.LogInformation($"Loading GL accounts for company: {companyCode}");

                // Filter accounts by company code
                ViewBag.Accounts = _context.GlSetup
                    .Where(x => x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToList();

                // Populate dropdown lists
                ViewBag.AccountTypes = GetAccountTypes();
                ViewBag.AccountCategories = GetAccountCategories();
                ViewBag.Currencies = GetCurrencies();
                ViewBag.SubCategories = GetSubCategories();
                ViewBag.CompanyCode = companyCode;

                return View(new GlSetup());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading GL Setup page");
                TempData["Error"] = "An error occurred while loading the page.";
                return View(new GlSetup());
            }
        }

        // ===============================
        // GET: /GlSetup/GetGroupsByType
        // ===============================
        [HttpGet("GetGroupsByType")]
        public IActionResult GetGroupsByType(string accountType)
        {
            try
            {
                var accountTypes = GetAccountTypes();
                var selectedType = accountTypes.FirstOrDefault(t => t.Type == accountType);

                if (selectedType == null)
                    return Json(new List<object>());

                var groups = selectedType.Groups.Select(g => new
                {
                    name = g.Name,
                    normalBalance = g.NormalBalance
                }).ToList();

                return Json(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting groups by type");
                return Json(new List<object>());
            }
        }

        // ===============================
        // POST: /GlSetup/Save
        // ===============================
        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(GlSetup model)
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();
                var userName = GetCurrentUserName();

                // Set company code from logged-in user
                model.CompanyCode = companyCode;

                // Remove validation for nullable fields
                ModelState.Remove("AuditDate");
                ModelState.Remove("EoyDate");
                ModelState.Remove("NewGlOpeningBalDate");

                if (!ModelState.IsValid)
                {
                    ViewBag.Accounts = _context.GlSetup
                        .Where(x => x.CompanyCode == companyCode)
                        .OrderBy(x => x.AccNo)
                        .ToList();
                    ViewBag.AccountTypes = GetAccountTypes();
                    ViewBag.AccountCategories = GetAccountCategories();
                    ViewBag.Currencies = GetCurrencies();
                    ViewBag.SubCategories = GetSubCategories();
                    return View("Index", model);
                }

                // Set default values
                model.TransDate = DateTime.Now;
                model.Status = true;
                model.AuditDate = DateTime.Now;
                model.AuditId = userName;

                // =========================================================
                // FIX: Handle Normal Balance based on Account Group
                // =========================================================
                if (model.GlAccMainGroup == "Capital Reserved")
                {
                    // For Capital Reserved, validate and preserve user-entered value
                    if (string.IsNullOrEmpty(model.Normalbal))
                    {
                        ModelState.AddModelError("Normalbal", "Normal Balance is required for Capital Reserved accounts.");
                        ViewBag.Accounts = _context.GlSetup
                            .Where(x => x.CompanyCode == companyCode)
                            .OrderBy(x => x.AccNo)
                            .ToList();
                        ViewBag.AccountTypes = GetAccountTypes();
                        ViewBag.AccountCategories = GetAccountCategories();
                        ViewBag.Currencies = GetCurrencies();
                        ViewBag.SubCategories = GetSubCategories();
                        return View("Index", model);
                    }

                    // Ensure it's either DR or CR (uppercase)
                    model.Normalbal = model.Normalbal.ToUpper();
                    if (model.Normalbal != "DR" && model.Normalbal != "CR")
                    {
                        ModelState.AddModelError("Normalbal", "Normal Balance must be either DR or CR.");
                        ViewBag.Accounts = _context.GlSetup
                            .Where(x => x.CompanyCode == companyCode)
                            .OrderBy(x => x.AccNo)
                            .ToList();
                        ViewBag.AccountTypes = GetAccountTypes();
                        ViewBag.AccountCategories = GetAccountCategories();
                        ViewBag.Currencies = GetCurrencies();
                        ViewBag.SubCategories = GetSubCategories();
                        return View("Index", model);
                    }
                }
                else
                {
                    // For other groups, auto-set the normal balance
                    model.Normalbal = GetNormalBalanceByGroup(model.GlAccMainGroup);
                }

                // Set default values for required fields
                if (string.IsNullOrEmpty(model.Type)) model.Type = "Balance Sheet";
                if (string.IsNullOrEmpty(model.SubType)) model.SubType = "Others";
                if (model.OpeningBal == 0) model.OpeningBal = 0;
                if (model.NewGlOpeningBal == 0) model.NewGlOpeningBal = 0;
                if (model.NewGlOpeningBalDate == DateTime.MinValue) model.NewGlOpeningBalDate = DateTime.Now;

                // Check if account number already exists for this company
                var existingAccount = _context.GlSetup
                    .FirstOrDefault(x => x.AccNo == model.AccNo && x.CompanyCode == companyCode);

                if (existingAccount != null)
                {
                    ModelState.AddModelError("AccNo", "Account number already exists for this company.");
                    ViewBag.Accounts = _context.GlSetup
                        .Where(x => x.CompanyCode == companyCode)
                        .OrderBy(x => x.AccNo)
                        .ToList();
                    ViewBag.AccountTypes = GetAccountTypes();
                    ViewBag.AccountCategories = GetAccountCategories();
                    ViewBag.Currencies = GetCurrencies();
                    ViewBag.SubCategories = GetSubCategories();
                    return View("Index", model);
                }

                _context.GlSetup.Add(model);
                _context.SaveChanges();

                _logger.LogInformation($"Account {model.AccNo} saved successfully for company {companyCode} by {userName}");
                TempData["Success"] = "Account saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving account");
                ModelState.AddModelError("", $"Error saving account: {ex.Message}");

                var companyCode = GetCurrentCompanyCode();
                ViewBag.Accounts = _context.GlSetup
                    .Where(x => x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToList();
                ViewBag.AccountTypes = GetAccountTypes();
                ViewBag.AccountCategories = GetAccountCategories();
                ViewBag.Currencies = GetCurrencies();
                ViewBag.SubCategories = GetSubCategories();
                return View("Index", model);
            }
        }

        // ===============================
        // GET: /GlSetup/Edit/5
        // ===============================
        [HttpGet("Edit/{id}")]
        public IActionResult Edit(long id)
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();

                var account = _context.GlSetup
                    .FirstOrDefault(x => x.GlId == id && x.CompanyCode == companyCode);

                if (account == null)
                {
                    TempData["Error"] = "Account not found or you don't have permission to edit it.";
                    return RedirectToAction(nameof(Index));
                }

                ViewBag.Accounts = _context.GlSetup
                    .Where(x => x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToList();
                ViewBag.AccountTypes = GetAccountTypes();
                ViewBag.AccountCategories = GetAccountCategories();
                ViewBag.Currencies = GetCurrencies();
                ViewBag.SubCategories = GetSubCategories();

                return View("Index", account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading account for edit: {id}");
                TempData["Error"] = "An error occurred while loading the account.";
                return RedirectToAction(nameof(Index));
            }
        }

        // ===============================
        // POST: /GlSetup/Update
        // ===============================
        [HttpPost("Update")]
        [ValidateAntiForgeryToken]
        public IActionResult Update(GlSetup model)
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();
                var userName = GetCurrentUserName();

                // Remove validation for nullable fields
                ModelState.Remove("AuditDate");
                ModelState.Remove("EoyDate");

                if (!ModelState.IsValid)
                {
                    ViewBag.Accounts = _context.GlSetup
                        .Where(x => x.CompanyCode == companyCode)
                        .OrderBy(x => x.AccNo)
                        .ToList();
                    ViewBag.AccountTypes = GetAccountTypes();
                    ViewBag.AccountCategories = GetAccountCategories();
                    ViewBag.Currencies = GetCurrencies();
                    ViewBag.SubCategories = GetSubCategories();
                    return View("Index", model);
                }

                // Find existing account with company code check
                var existing = _context.GlSetup
                    .FirstOrDefault(x => x.GlId == model.GlId && x.CompanyCode == companyCode);

                if (existing == null)
                {
                    TempData["Error"] = "Account not found or you don't have permission to update it.";
                    return RedirectToAction(nameof(Index));
                }

                // Check if account number is being changed and if it already exists in this company
                if (existing.AccNo != model.AccNo)
                {
                    var duplicateAccount = _context.GlSetup
                        .FirstOrDefault(x => x.AccNo == model.AccNo && x.CompanyCode == companyCode && x.GlId != model.GlId);

                    if (duplicateAccount != null)
                    {
                        ModelState.AddModelError("AccNo", "Account number already exists for this company.");
                        ViewBag.Accounts = _context.GlSetup
                            .Where(x => x.CompanyCode == companyCode)
                            .OrderBy(x => x.AccNo)
                            .ToList();
                        ViewBag.AccountTypes = GetAccountTypes();
                        ViewBag.AccountCategories = GetAccountCategories();
                        ViewBag.Currencies = GetCurrencies();
                        ViewBag.SubCategories = GetSubCategories();
                        return View("Index", model);
                    }
                }

                // =========================================================
                // FIX: Handle Normal Balance based on Account Group
                // =========================================================
                if (model.GlAccMainGroup == "Capital Reserved")
                {
                    // For Capital Reserved, keep the user-entered value (DR or CR)
                    // Don't overwrite it - just validate it
                    if (string.IsNullOrEmpty(model.Normalbal))
                    {
                        ModelState.AddModelError("Normalbal", "Normal Balance is required for Capital Reserved accounts.");
                        ViewBag.Accounts = _context.GlSetup
                            .Where(x => x.CompanyCode == companyCode)
                            .OrderBy(x => x.AccNo)
                            .ToList();
                        ViewBag.AccountTypes = GetAccountTypes();
                        ViewBag.AccountCategories = GetAccountCategories();
                        ViewBag.Currencies = GetCurrencies();
                        ViewBag.SubCategories = GetSubCategories();
                        return View("Index", model);
                    }

                    // Ensure it's either DR or CR (uppercase)
                    model.Normalbal = model.Normalbal.ToUpper();
                    if (model.Normalbal != "DR" && model.Normalbal != "CR")
                    {
                        ModelState.AddModelError("Normalbal", "Normal Balance must be either DR or CR.");
                        ViewBag.Accounts = _context.GlSetup
                            .Where(x => x.CompanyCode == companyCode)
                            .OrderBy(x => x.AccNo)
                            .ToList();
                        ViewBag.AccountTypes = GetAccountTypes();
                        ViewBag.AccountCategories = GetAccountCategories();
                        ViewBag.Currencies = GetCurrencies();
                        ViewBag.SubCategories = GetSubCategories();
                        return View("Index", model);
                    }
                }
                else
                {
                    // For other groups, auto-set the normal balance
                    model.Normalbal = GetNormalBalanceByGroup(model.GlAccMainGroup);
                }

                // Preserve audit information
                model.Status = true;
                model.AuditDate = DateTime.Now;
                model.AuditId = userName;
                model.TransDate = existing.TransDate; // Keep original transaction date
                model.CompanyCode = companyCode; // Ensure company code remains the same

                _context.Entry(existing).CurrentValues.SetValues(model);
                _context.SaveChanges();

                _logger.LogInformation($"Account {model.AccNo} updated successfully for company {companyCode} by {userName}");
                TempData["Success"] = "Account updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating account");
                ModelState.AddModelError("", $"Error updating account: {ex.Message}");

                var companyCode = GetCurrentCompanyCode();
                ViewBag.Accounts = _context.GlSetup
                    .Where(x => x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToList();
                ViewBag.AccountTypes = GetAccountTypes();
                ViewBag.AccountCategories = GetAccountCategories();
                ViewBag.Currencies = GetCurrencies();
                ViewBag.SubCategories = GetSubCategories();
                return View("Index", model);
            }
        }
        // ===============================
        // POST: /GlSetup/Delete
        // ===============================
        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(long glId)
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();
                var userName = GetCurrentUserName();

                var account = _context.GlSetup
                    .FirstOrDefault(x => x.GlId == glId && x.CompanyCode == companyCode);

                if (account == null)
                {
                    TempData["Error"] = "Account not found or you don't have permission to delete it.";
                    return RedirectToAction(nameof(Index));
                }

                // Check if account is being used in transactions (with company code filter)
                bool hasTransactions = _context.Gltransactions
                    .Any(x => (x.DrAccNo == account.AccNo || x.CrAccNo == account.AccNo) &&
                              x.CompanyCode == companyCode);

                if (hasTransactions)
                {
                    TempData["Error"] = "Cannot delete account because it has associated transactions.";
                    return RedirectToAction(nameof(Index));
                }

                _context.GlSetup.Remove(account);
                _context.SaveChanges();

                _logger.LogInformation($"Account {account.AccNo} deleted successfully for company {companyCode} by {userName}");
                TempData["Success"] = "Account deleted successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting account");
                TempData["Error"] = $"Error deleting account: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===============================
        // GET: /GlSetup/GetAccountsForDropdown
        // Used by LoanType controller to get accounts for dropdowns
        // ===============================
        [HttpGet("GetAccountsForDropdown")]
        public async Task<IActionResult> GetAccountsForDropdown()
        {
            try
            {
                var companyCode = GetCurrentCompanyCode();

                var accounts = await _context.GlSetup
                    .Where(a => a.CompanyCode == companyCode && a.Status == true)
                    .OrderBy(a => a.AccNo)
                    .Select(a => new
                    {
                        Value = a.AccNo,
                        Text = $"{a.AccNo} - {a.Glaccname}"
                    })
                    .ToListAsync();

                return Json(accounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accounts for dropdown");
                return Json(new List<object>());
            }
        }

        // ===============================
        // Private Methods - Data Sources
        // ===============================
        private List<AccountTypeConfig> GetAccountTypes()
        {
            return new List<AccountTypeConfig>
    {
        new AccountTypeConfig
        {
            Type = "Income Statement",
            Groups = new List<AccountGroup>
            {
                new AccountGroup { Name = "Expenses", NormalBalance = "DR" },
                new AccountGroup { Name = "Income", NormalBalance = "CR" }
            }
        },
        new AccountTypeConfig
        {
            Type = "Balance Sheet",
            Groups = new List<AccountGroup>
            {
                new AccountGroup { Name = "Assets", NormalBalance = "DR" },
                 new AccountGroup { Name = "Capital Reserved", NormalBalance = "" },
                new AccountGroup { Name = "Liabilities", NormalBalance = "CR" },
                new AccountGroup { Name = "Retained Earnings", NormalBalance = "CR" },
                new AccountGroup { Name = "Revenue Reserved", NormalBalance = "CR" },
                new AccountGroup { Name = "Shareholder Equity", NormalBalance = "CR" }
            }
        }
    };
        }

        // NEW: Get Account SubCategories based on selected Group
        private List<AccountSubCategory> GetAccountSubCategories(string groupName)
        {
            var subCategories = new Dictionary<string, List<AccountSubCategory>>
            {
                // ASSETS SubCategories (from your list)
                ["Assets"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 1, Name = "Loans", Code = "LOAN" },
            new AccountSubCategory { Id = 2, Name = "KCB", Code = "KCB" },
            new AccountSubCategory { Id = 3, Name = "Cash at bank", Code = "CASH_BANK" },
            new AccountSubCategory { Id = 4, Name = "GROUP INVESTMENT", Code = "GRP_INV" },
            new AccountSubCategory { Id = 5, Name = "Computer & Accessories", Code = "COMP_ACC" },
            new AccountSubCategory { Id = 6, Name = "Loan Interest Receivable", Code = "LOAN_INT_REC" },
            new AccountSubCategory { Id = 7, Name = "STATIONERY", Code = "STATIONERY" },
            new AccountSubCategory { Id = 8, Name = "Software", Code = "SOFTWARE" },
            new AccountSubCategory { Id = 9, Name = "Checkoff & Payroll Control Acc", Code = "CHECKOFF" },
            new AccountSubCategory { Id = 10, Name = "Property Plant & Equipment", Code = "PPE" },
            new AccountSubCategory { Id = 11, Name = "Investment Income Receivable", Code = "INV_INC_REC" },
            new AccountSubCategory { Id = 12, Name = "Other Receivables", Code = "OTHER_REC" },
            new AccountSubCategory { Id = 13, Name = "Intangible Assets", Code = "INTANGIBLE" },
            new AccountSubCategory { Id = 14, Name = "Fixed Assets", Code = "FIXED_ASSETS" },
            new AccountSubCategory { Id = 15, Name = "Cash & Cash Equivalent", Code = "CASH_EQ" },
            new AccountSubCategory { Id = 16, Name = "Current Assets", Code = "CURR_ASSETS" },
            new AccountSubCategory { Id = 17, Name = "Loans to Members", Code = "LOAN_MEM" },
            new AccountSubCategory { Id = 18, Name = "Investment", Code = "INVESTMENT" },
            new AccountSubCategory { Id = 19, Name = "Receivables & Prepayments", Code = "REC_PREP" }
        },

                // CAPITAL RESERVED SubCategories
                ["Capital Reserved"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 20, Name = "Grants", Code = "GRANTS" },
            new AccountSubCategory { Id = 21, Name = "Capital Reserve Fund", Code = "CAP_RES_FUND" },
            new AccountSubCategory { Id = 22, Name = "Revaluation Reserve", Code = "REV_RES" },
            new AccountSubCategory { Id = 23, Name = "Statutory Reserve", Code = "STAT_RES" }
        },

                // LIABILITIES SubCategories
                ["Liabilities"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 24, Name = "ShareCapital", Code = "SHARE_CAP" },
            new AccountSubCategory { Id = 25, Name = "Liabilities", Code = "LIABILITIES" },
            new AccountSubCategory { Id = 26, Name = "Equity", Code = "EQUITY" },
            new AccountSubCategory { Id = 27, Name = "Current Liabilities", Code = "CURR_LIAB" },
            new AccountSubCategory { Id = 28, Name = "Long Term Liabilities", Code = "LONG_LIAB" },
            new AccountSubCategory { Id = 29, Name = "Accounts Payable", Code = "AP" },
            new AccountSubCategory { Id = 30, Name = "Accrued Expenses", Code = "ACC_EXP" },
            new AccountSubCategory { Id = 31, Name = "Member Deposits", Code = "MEM_DEP" },
            new AccountSubCategory { Id = 32, Name = "Loans Payable", Code = "LOAN_PAY" }
        },

                // RETAINED EARNINGS SubCategories
                ["Retained Earnings"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 33, Name = "Retained Earnings", Code = "RET_EARN" },
            new AccountSubCategory { Id = 34, Name = "Accumulated Profits", Code = "ACC_PROF" },
            new AccountSubCategory { Id = 35, Name = "Prior Year Adjustments", Code = "PRIOR_ADJ" }
        },

                // REVENUE RESERVED SubCategories
                ["Revenue Reserved"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 36, Name = "Revenue Reserve", Code = "REV_RES" },
            new AccountSubCategory { Id = 37, Name = "General Reserve", Code = "GEN_RES" },
            new AccountSubCategory { Id = 38, Name = "Dividend Reserve", Code = "DIV_RES" }
        },

                // SHAREHOLDER EQUITY SubCategories
                ["Shareholder Equity"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 39, Name = "Share Capital", Code = "SHARE_CAP" },
            new AccountSubCategory { Id = 40, Name = "Additional Paid-in Capital", Code = "APIC" },
            new AccountSubCategory { Id = 41, Name = "Treasury Shares", Code = "TREASURY" }
        },

                // INCOME SubCategories (for Income Statement)
                ["Income"] = new List<AccountSubCategory>
        {
            new AccountSubCategory { Id = 42, Name = "Interest Income", Code = "INT_INC" },
            new AccountSubCategory { Id = 43, Name = "Fee Income", Code = "FEE_INC" },
            new AccountSubCategory { Id = 44, Name = "Investment Income", Code = "INV_INC" },
            new AccountSubCategory { Id = 45, Name = "Other Operating Income", Code = "OP_INC" }
        },

                // EXPENSES SubCategories (for Income Statement)
                ["Expenses"] = new List<AccountSubCategory>
        {
                          new AccountSubCategory { Id = 46, Name = "Committee Travelling & Subsistence Allowance", Code = "CTA" },
                new AccountSubCategory { Id = 47, Name = "Printing & Stationery", Code = "PRINT" },
                new AccountSubCategory { Id = 48, Name = "Bank Charges", Code = "BANK_CHG" },
                new AccountSubCategory { Id = 49, Name = "Water & Electricity", Code = "UTIL" },
                new AccountSubCategory { Id = 50, Name = "Cleaning & detergents", Code = "CLEAN" },
                new AccountSubCategory { Id = 51, Name = "Interest on borrowings", Code = "INT_BORR" },
                new AccountSubCategory { Id = 52, Name = "Public relation & advertisement", Code = "PR_ADV" },
                new AccountSubCategory { Id = 53, Name = "ALLOWANCES", Code = "ALLOW" },
                new AccountSubCategory { Id = 54, Name = "Directors Expenses", Code = "DIR_EXP" },
                new AccountSubCategory { Id = 55, Name = "Administrative Expensive", Code = "ADMIN_EXP" },
                new AccountSubCategory { Id = 56, Name = "Committee Sitting Allowance", Code = "CSA" },
                new AccountSubCategory { Id = 57, Name = "AGM Expenses", Code = "AGM" },
                new AccountSubCategory { Id = 58, Name = "Depreciation", Code = "DEPRECIATION" },
                new AccountSubCategory { Id = 59, Name = "Audit Fees", Code = "AUDIT" },
                new AccountSubCategory { Id = 60, Name = "Bad debt w/o", Code = "BAD_DEBT" },
                new AccountSubCategory { Id = 61, Name = "Repairs & maintenance", Code = "REPAIRS" },
                new AccountSubCategory { Id = 62, Name = "Ushirika day expenses", Code = "USHIRIKA" },
                new AccountSubCategory { Id = 63, Name = "Postage & Airtime", Code = "POSTAGE" },
                new AccountSubCategory { Id = 64, Name = "Security Expenses", Code = "SECURITY" }
                    }
            };

            return subCategories.ContainsKey(groupName) ? subCategories[groupName] : new List<AccountSubCategory>();
        }

        // NEW: API endpoint to get subcategories based on selected group
        [HttpGet("GetSubCategoriesByGroup")]
        public IActionResult GetSubCategoriesByGroup(string groupName)
        {
            try
            {
                var subCategories = GetAccountSubCategories(groupName);
                return Json(subCategories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting sub categories for group: {groupName}");
                return Json(new List<AccountSubCategory>());
            }
        }

        private List<AccountCategory> GetAccountCategories()
        {
            return new List<AccountCategory>
    {
        new AccountCategory { Id = 1, Name = "BOSA" },
        new AccountCategory { Id = 2, Name = "Operating Income" },
        new AccountCategory { Id = 3, Name = "Operating Assets" },
        new AccountCategory { Id = 4, Name = "Operating Expenses" },
        new AccountCategory { Id = 5, Name = "Operating Liabilities" },
        new AccountCategory { Id = 6, Name = "Investing Activities" }
    };
        }

        private List<Currency> GetCurrencies()
        {
            return new List<Currency>
    {
        new Currency { Id = 1, Code = "KSH", Name = "Kenyan Shilling" },
        //new Currency { Id = 2, Code = "USD", Name = "US Dollar" },
        //new Currency { Id = 3, Code = "GBP", Name = "British Pound" },
        //new Currency { Id = 4, Code = "TSH", Name = "Tanzanian Shilling" },
        //new Currency { Id = 5, Code = "USH", Name = "Ugandan Shilling" },
        //new Currency { Id = 6, Code = "ZAR", Name = "South African Rand" }
    };
        }

        private List<SubCategory> GetSubCategories()
        {
            return new List<SubCategory>
    {
        new SubCategory { Id = 1, Name = "Loans" },
        new SubCategory { Id = 2, Name = "Interests" },
        new SubCategory { Id = 3, Name = "Shares" },
        new SubCategory { Id = 4, Name = "Others" }
    };
        }

        private string GetNormalBalanceByGroup(string groupName)
        {
            var accountTypes = GetAccountTypes();
            foreach (var type in accountTypes)
            {
                var group = type.Groups.FirstOrDefault(g => g.Name == groupName);
                if (group != null)
                    return group.NormalBalance;
            }
            return "DR"; // Default
        }

        // ===============================
        // Helper Classes
        // ===============================
        public class AccountTypeConfig
        {
            public string Type { get; set; } = "";
            public List<AccountGroup> Groups { get; set; } = new List<AccountGroup>();
        }

        public class AccountGroup
        {
            public string Name { get; set; } = "";
            public string NormalBalance { get; set; } = "";
        }

        public class AccountCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class Currency
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public class SubCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        // NEW: Account SubCategory Class
        public class AccountSubCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
        }
    }
}