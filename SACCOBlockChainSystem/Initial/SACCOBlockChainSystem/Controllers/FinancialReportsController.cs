// Controllers/FinancialReportsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("Reports")]
    public class FinancialReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FinancialReportsController> _logger;

        public FinancialReportsController(
            ApplicationDbContext context,
            ILogger<FinancialReportsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ===============================
        // GET: /Reports
        // ===============================
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetCompanyCode();
                var model = new FinancialReportViewModel
                {
                    StartDate = new DateTime(DateTime.Now.Year, 1, 1),
                    EndDate = DateTime.Now
                };

                // Get suspense account info
                var suspenseAccount = await GetSuspenseAccount(companyCode);
                ViewBag.HasSuspenseAccount = suspenseAccount != null;
                ViewBag.SuspenseAccountNo = suspenseAccount?.AccNo;
                ViewBag.SuspenseAccountName = suspenseAccount?.Glaccname;

                ViewBag.CompanyCode = companyCode;
                ViewBag.CompanyName = GetCompanyName();
                ViewBag.ReportDate = DateTime.Now;

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading reports page");
                TempData["Error"] = $"Error loading page: {ex.Message}";
                return View(new FinancialReportViewModel());
            }
        }

        // ===============================
        // POST: /Reports/Generate
        // ===============================
        [HttpPost("Generate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(
            DateTime startDate,
            DateTime endDate,
            string reportType)
        {
            try
            {
                startDate = startDate.Date;
                endDate = endDate.Date.AddDays(1).AddTicks(-1);

                var companyCode = GetCompanyCode();
                var model = new FinancialReportViewModel
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    ReportType = reportType
                };

                // Get all accounts
                var accounts = await _context.GlSetup
                    .Where(x => x.Status == true && x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToListAsync();

                // Get suspense account
                var suspenseAccount = accounts.FirstOrDefault(x => x.IsSuspense);
                model.HasSuspenseAccount = suspenseAccount != null;
                model.SuspenseAccountNo = suspenseAccount?.AccNo;
                model.SuspenseAccountName = suspenseAccount?.Glaccname;

                // Get transactions for the period
                var transactions = await _context.Gltransactions
                    .Where(x => x.TransDate >= startDate
                             && x.TransDate <= endDate
                             && x.CompanyCode == companyCode
                             && x.DocPosted == 1)
                    .ToListAsync();

                // Get journal listings for the period
                var journalListings = await _context.JournalsListings
                    .Where(x => x.TransDate >= startDate
                             && x.TransDate <= endDate
                             && x.CompanyCode == companyCode
                             && x.TransType != "REF"
                             && x.TransType != "GRP")
                    .ToListAsync();

                switch (reportType)
                {
                    case "TrialBalance":
                        await GenerateTrialBalance(model, accounts, transactions, journalListings, companyCode);
                        break;
                    case "IncomeStatement":
                        await GenerateIncomeStatement(model, accounts, transactions, journalListings, companyCode);
                        break;
                    case "BalanceSheet":
                        await GenerateBalanceSheet(model, accounts, transactions, journalListings, companyCode);
                        break;
                    case "CashFlow":
                        await GenerateCashFlow(model, accounts, transactions, journalListings, companyCode);
                        break;
                }

                // Calculate suspense aging if suspense account exists
                if (suspenseAccount != null)
                {
                    model.SuspenseBalance = await CalculateAccountBalance(
                        suspenseAccount.AccNo ?? "", endDate, companyCode);

                    model.SuspenseAging = await CalculateSuspenseAging(
                        suspenseAccount.AccNo ?? "", companyCode);
                }

                ViewBag.CompanyCode = companyCode;
                ViewBag.CompanyName = GetCompanyName();
                ViewBag.ReportDate = DateTime.Now;

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                TempData["Error"] = $"Error generating report: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ===============================
        // POST: /Reports/CreateSuspenseEntry
        // ===============================
        [HttpPost("CreateSuspenseEntry")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin,Accountant")]
        public async Task<IActionResult> CreateSuspenseEntry(
            DateTime asAtDate,
            string notes = "Suspense entry to balance Balance Sheet")
        {
            try
            {
                var companyCode = GetCompanyCode();
                var auditUser = User.Identity?.Name ?? "SYSTEM";
                var now = DateTime.Now;

                // Get suspense account
                var suspenseAccount = await GetSuspenseAccount(companyCode);

                if (suspenseAccount == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No suspense account defined. Please create a suspense account in GL Setup first."
                    });
                }

                // Recalculate balance sheet totals
                var accounts = await _context.GlSetup
                    .Where(x => x.Status == true && x.CompanyCode == companyCode)
                    .ToListAsync();

                decimal totalAssets = 0;
                decimal totalLiabilities = 0;
                decimal totalEquity = 0;

                // Calculate Assets
                var assetAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                       x.GlAccMainGroup == "Assets");
                foreach (var account in assetAccounts)
                {
                    totalAssets += await CalculateAccountBalance(
                        account.AccNo ?? "", asAtDate, companyCode);
                }

                // Calculate Liabilities
                var liabilityAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                           x.GlAccMainGroup == "Liabilities");
                foreach (var account in liabilityAccounts)
                {
                    totalLiabilities += await CalculateAccountBalance(
                        account.AccNo ?? "", asAtDate, companyCode);
                }

                // Calculate Equity
                var equityAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                        (x.GlAccMainGroup == "Capital Reserved" ||
                                                         x.GlAccMainGroup == "Retained Earnings" ||
                                                         x.GlAccMainGroup == "Shareholder Equity"));
                foreach (var account in equityAccounts)
                {
                    totalEquity += await CalculateAccountBalance(
                        account.AccNo ?? "", asAtDate, companyCode);
                }

                var difference = totalAssets - (totalLiabilities + totalEquity);

                if (Math.Abs(difference) < 0.01m)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Balance Sheet is already balanced. No suspense entry needed."
                    });
                }

                // Create suspense journal entry
                var voucherNo = $"SUS-{DateTime.Now:yyyyMMdd-HHmmss}";
                var amount = Math.Abs(difference);

                // Determine if suspense should be DR or CR
                bool suspenseIsDr = difference < 0; // If Assets are less, need DR suspense
                bool suspenseIsCr = difference > 0; // If Assets are more, need CR suspense

                var suspenseEntry = new JournalsListing
                {
                    VoucherNo = voucherNo,
                    AccountNo = suspenseAccount.AccNo,
                    AccountName = suspenseAccount.Glaccname,
                    Narration = notes,
                    MemberNo = "SYSTEM",
                    ShareType = "SUSPENSE",
                    LoanNo = "0",
                    AmountDr = suspenseIsDr ? amount : 0,
                    AmountCr = suspenseIsCr ? amount : 0,
                    Amount = amount,
                    TransType = suspenseIsDr ? "DR" : "CR",
                    AuditId = auditUser,
                    TransDate = asAtDate,
                    AuditDate = now,
                    Posted = true,
                    PostedDate = now,
                    TransactionNo = GenerateTransactionNumber(),
                    CompanyCode = companyCode
                };

                _context.JournalsListings.Add(suspenseEntry);

                // Also create corresponding GL transaction
                var glTransaction = new Gltransaction
                {
                    TransDate = asAtDate,
                    Amount = amount,
                    DrAccNo = suspenseIsDr ? suspenseAccount.AccNo : GetCashOrBankAccount(),
                    CrAccNo = suspenseIsCr ? suspenseAccount.AccNo : GetCashOrBankAccount(),
                    DocumentNo = voucherNo,
                    Source = "SUSPENSE",
                    Temp = "ADJ",
                    CompanyCode = companyCode,
                    TransDescript = notes,
                    AuditId = auditUser,
                    AuditTime = now,
                    TransactionNo = voucherNo,
                    DocPosted = 1,
                    Module = "GL",
                    Cash = 0,
                    Recon = false
                };

                _context.Gltransactions.Add(glTransaction);
                await _context.SaveChangesAsync();

                string direction = suspenseIsDr ? "DEBIT (added to Assets)" : "CREDIT (added to Liabilities/Equity)";

                _logger.LogInformation($"Balance Sheet suspense entry created: {voucherNo}, " +
                                      $"Amount: {amount:N2}, {direction}");

                return Json(new
                {
                    success = true,
                    message = $"Balance Sheet suspense entry created successfully. Voucher: {voucherNo}",
                    voucherNo = voucherNo,
                    amount = amount,
                    direction = direction,
                    placement = suspenseIsDr ? "Asset" : "Liability/Equity"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Balance Sheet suspense entry");
                return Json(new { success = false, message = $"Error creating suspense entry: {ex.Message}" });
            }
        }

        // ===============================
        // GET: /Reports/ClearSuspense
        // ===============================
        [HttpGet("ClearSuspense")]
        [Authorize(Roles = "Admin,SuperAdmin,Accountant")]
        public async Task<IActionResult> ClearSuspense()
        {
            try
            {
                var companyCode = GetCompanyCode();
                var suspenseAccount = await GetSuspenseAccount(companyCode);

                if (suspenseAccount == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No suspense account defined."
                    });
                }

                var suspenseBalance = await CalculateAccountBalance(
                    suspenseAccount.AccNo ?? "", DateTime.Now, companyCode);

                var aging = await CalculateSuspenseAging(
                    suspenseAccount.AccNo ?? "", companyCode);

                return Json(new
                {
                    success = true,
                    hasBalance = Math.Abs(suspenseBalance) > 0.01m,
                    balance = suspenseBalance,
                    aging = aging,
                    accountNo = suspenseAccount.AccNo,
                    accountName = suspenseAccount.Glaccname
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking suspense status");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ===============================
        // Report Generation Methods
        // ===============================
        private async Task GenerateTrialBalance(
            FinancialReportViewModel model,
            List<GlSetup> accounts,
            List<Gltransaction> transactions,
            List<JournalsListing> journalListings,
            string companyCode)
        {
            foreach (var account in accounts)
            {
                var accountTransactions = transactions
                    .Where(x => x.DrAccNo == account.AccNo || x.CrAccNo == account.AccNo);

                var accountJournals = journalListings
                    .Where(x => x.AccountNo == account.AccNo);

                var debit = accountTransactions
                    .Where(x => x.DrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                debit += accountJournals
                    .Where(x => x.TransType == "DR")
                    .Sum(x => x.AmountDr ?? 0);

                var credit = accountTransactions
                    .Where(x => x.CrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                credit += accountJournals
                    .Where(x => x.TransType == "CR")
                    .Sum(x => x.AmountCr ?? 0);

                var balance = account.Normalbal == "DR" ? debit - credit : credit - debit;

                model.TrialBalance.Add(new TrialBalanceItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    AccountType = account.Type ?? "",
                    NormalBalance = account.Normalbal ?? "DR",
                    Debit = debit,
                    Credit = credit,
                    Balance = balance,
                    IsSuspense = account.IsSuspense
                });

                model.TotalDebits += debit;
                model.TotalCredits += credit;
            }
        }

        private async Task GenerateIncomeStatement(
            FinancialReportViewModel model,
            List<GlSetup> accounts,
            List<Gltransaction> transactions,
            List<JournalsListing> journalListings,
            string companyCode)
        {
            var revenueAccounts = accounts.Where(x => x.Type == "Income Statement" &&
                                                      x.GlAccMainGroup == "Income");
            var expenseAccounts = accounts.Where(x => x.Type == "Income Statement" &&
                                                      x.GlAccMainGroup == "Expenses");

            foreach (var account in revenueAccounts)
            {
                var credit = transactions
                    .Where(x => x.CrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                credit += journalListings
                    .Where(x => x.AccountNo == account.AccNo && x.TransType == "CR")
                    .Sum(x => x.AmountCr ?? 0);

                var debit = transactions
                    .Where(x => x.DrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                debit += journalListings
                    .Where(x => x.AccountNo == account.AccNo && x.TransType == "DR")
                    .Sum(x => x.AmountDr ?? 0);

                model.IncomeStatement.Revenue.Add(new IncomeStatementItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    Amount = credit - debit
                });
            }

            foreach (var account in expenseAccounts)
            {
                var debit = transactions
                    .Where(x => x.DrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                debit += journalListings
                    .Where(x => x.AccountNo == account.AccNo && x.TransType == "DR")
                    .Sum(x => x.AmountDr ?? 0);

                var credit = transactions
                    .Where(x => x.CrAccNo == account.AccNo)
                    .Sum(x => x.Amount);

                credit += journalListings
                    .Where(x => x.AccountNo == account.AccNo && x.TransType == "CR")
                    .Sum(x => x.AmountCr ?? 0);

                model.IncomeStatement.Expenses.Add(new IncomeStatementItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    Amount = debit - credit
                });
            }
        }

        private async Task GenerateBalanceSheet(
     FinancialReportViewModel model,
     List<GlSetup> accounts,
     List<Gltransaction> transactions,
     List<JournalsListing> journalListings,
     string companyCode)
        {
            decimal totalAssets = 0;
            decimal totalLiabilities = 0;
            decimal totalEquity = 0;

            // Get Net Income from Income Statement for the period
            var netIncome = await CalculateNetIncome(accounts, transactions, journalListings, companyCode, model.EndDate);

            // Process Asset Accounts (Normal Balance = DR)
            var assetAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                   (x.GlAccMainGroup?.ToLower() == "assets" ||
                                                    x.GlAccMainGroup?.ToLower().Contains("asset") == true));

            foreach (var account in assetAccounts)
            {
                var balance = await CalculateAccountBalance(
                    account.AccNo ?? "", model.EndDate, companyCode);

                model.BalanceSheet.Assets.Add(new BalanceSheetItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    Amount = balance,
                    IsSuspense = account.IsSuspense
                });

                totalAssets += balance;
            }

            // Process Liability Accounts (Normal Balance = CR)
            var liabilityAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                       (x.GlAccMainGroup?.ToLower() == "liabilities" ||
                                                        x.GlAccMainGroup?.ToLower().Contains("liability") == true));

            foreach (var account in liabilityAccounts)
            {
                var balance = await CalculateAccountBalance(
                    account.AccNo ?? "", model.EndDate, companyCode);

                model.BalanceSheet.Liabilities.Add(new BalanceSheetItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    Amount = balance,
                    IsSuspense = account.IsSuspense
                });

                totalLiabilities += balance;
            }

            // Process Equity Accounts
            var equityAccounts = accounts.Where(x => x.Type == "Balance Sheet" &&
                                                    (x.GlAccMainGroup?.ToLower().Contains("capital") == true ||
                                                     x.GlAccMainGroup?.ToLower().Contains("reserve") == true ||
                                                     x.GlAccMainGroup?.ToLower().Contains("equity") == true ||
                                                     x.GlAccMainGroup?.ToLower().Contains("retained earnings") == true));

            foreach (var account in equityAccounts)
            {
                var balance = await CalculateAccountBalance(
                    account.AccNo ?? "", model.EndDate, companyCode);

                model.BalanceSheet.Equity.Add(new BalanceSheetItem
                {
                    AccountNo = account.AccNo ?? "",
                    AccountName = account.Glaccname ?? "",
                    Amount = balance,
                    IsSuspense = account.IsSuspense
                });

                totalEquity += balance;
            }

            // Add Net Income to Equity (as Retained Earnings for the period)
            if (netIncome != 0)
            {
                // Check if we already have a retained earnings entry
                var existingRetainedEarnings = model.BalanceSheet.Equity
                    .FirstOrDefault(x => x.AccountName?.Contains("Retained Earnings") == true);

                if (existingRetainedEarnings != null)
                {
                    // Update existing retained earnings to include net income
                    existingRetainedEarnings.Amount += netIncome;
                }
                else
                {
                    // Add new retained earnings entry
                    model.BalanceSheet.Equity.Add(new BalanceSheetItem
                    {
                        AccountNo = "NETINC",
                        AccountName = "Retained Earnings (Current Period)",
                        Amount = netIncome,
                        IsSuspense = false
                    });
                }

                totalEquity += netIncome;
            }

            // Set the top-level totals (these are writable)
            model.TotalAssets = totalAssets;
            model.TotalLiabilities = totalLiabilities;
            model.TotalEquity = totalEquity;

            // Calculate if Balance Sheet is balanced using the actual totals
            var liabilitiesPlusEquity = totalLiabilities + totalEquity;
            var difference = totalAssets - liabilitiesPlusEquity;

            // Set the balance sheet balanced flag (this is writable)
            model.BalanceSheetBalanced = Math.Abs(difference) < 0.01m;

            if (!model.BalanceSheetBalanced)
            {
                model.HasSuspenseAccount = true;
                model.SuspenseBalance = Math.Abs(difference);
                model.SuspensePlacement = difference > 0 ? "Liability/Equity" : "Asset";

                _logger.LogWarning($"Balance Sheet unbalanced by {difference:N2}. Assets: {totalAssets:N2}, Liabilities+Equity: {liabilitiesPlusEquity:N2}");
            }
        }
        private async Task<decimal> CalculateAccountBalance(
         string accountNo,
         DateTime asAtDate,
         string companyCode)
        {
            decimal totalDebits = 0;
            decimal totalCredits = 0;

            // ============================================
            // 1. Get from Journals table (POSTED entries)
            // ============================================
            var journalEntries = await _context.Journals
                .Where(x => x.ACCNO == accountNo
                         && x.CompanyCode == companyCode
                         && x.POSTED == true
                         && x.TRANSDATE <= asAtDate)
                .ToListAsync();

            foreach (var j in journalEntries)
            {
                if (j.TRANSTYPE == "DR")
                    totalDebits += j.AMOUNT ?? 0;
                else if (j.TRANSTYPE == "CR")
                    totalCredits += j.AMOUNT ?? 0;
            }

            // ============================================
            // 2. Get from JournalsListings (if any posted entries exist here)
            // ============================================
            var journalListings = await _context.JournalsListings
                .Where(x => x.AccountNo == accountNo
                         && x.CompanyCode == companyCode
                         && x.Posted == true
                         && x.TransDate <= asAtDate
                         && x.TransType != "REF"
                         && x.TransType != "GRP")
                .ToListAsync();

            totalDebits += journalListings.Sum(x => x.AmountDr ?? 0);
            totalCredits += journalListings.Sum(x => x.AmountCr ?? 0);

            // Get account normal balance
            var account = await _context.GlSetup
                .FirstOrDefaultAsync(x => x.AccNo == accountNo && x.CompanyCode == companyCode);

            if (account == null)
                return 0;

            _logger.LogDebug(
                $"BS Account {accountNo} | DR={totalDebits:N2}, CR={totalCredits:N2}, Normal={account.Normalbal}");

            // Normal balance handling
            return account.Normalbal?.ToUpper() == "DR"
                ? totalDebits - totalCredits
                : totalCredits - totalDebits;
        }
        private async Task GenerateCashFlow(
            FinancialReportViewModel model,
            List<GlSetup> accounts,
            List<Gltransaction> transactions,
            List<JournalsListing> journalListings,
            string companyCode)
        {
            // Get cash account
            var cashAccount = accounts.FirstOrDefault(x => x.AccNo == "1010");

            if (cashAccount != null)
            {
                model.CashFlow.BeginningCash = await CalculateAccountBalance(
                    cashAccount.AccNo ?? "", model.StartDate.AddDays(-1), companyCode);

                model.CashFlow.EndingCash = await CalculateAccountBalance(
                    cashAccount.AccNo ?? "", model.EndDate, companyCode);

                // Get all cash transactions including suspense
                var cashTransactions = transactions
                    .Where(x => x.DrAccNo == cashAccount.AccNo || x.CrAccNo == cashAccount.AccNo);

                foreach (var t in cashTransactions)
                {
                    var amount = t.DrAccNo == cashAccount.AccNo ? t.Amount : -t.Amount;
                    var description = t.TransDescript ?? "Cash transaction";

                    // Check if related to suspense
                    var isSuspenseRelated = t.DrAccNo == "9999" || t.CrAccNo == "9999" ||
                                           description.ToLower().Contains("suspense");

                    if (isSuspenseRelated)
                    {
                        description = "[SUSPENSE] " + description;
                    }

                    // Categorize cash flows
                    if (description.ToLower().Contains("loan") ||
                        description.ToLower().Contains("borrow"))
                    {
                        model.CashFlow.FinancingActivities.Add(new CashFlowItem
                        {
                            Description = description,
                            Amount = amount
                        });
                    }
                    else if (description.ToLower().Contains("invest") ||
                             description.ToLower().Contains("asset"))
                    {
                        model.CashFlow.InvestingActivities.Add(new CashFlowItem
                        {
                            Description = description,
                            Amount = amount
                        });
                    }
                    else
                    {
                        model.CashFlow.OperatingActivities.Add(new CashFlowItem
                        {
                            Description = description,
                            Amount = amount
                        });
                    }
                }
            }
        }

        // ===============================
        // Helper Methods
        // ===============================
        private string GetCompanyCode()
        {
            return User.FindFirst("CompanyCode")?.Value ?? "001";
        }

        private string GetCompanyName()
        {
            return User.FindFirst("CompanyName")?.Value ?? "Main SACCO";
        }

        private string GenerateTransactionNumber()
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            var random = new Random();
            var randomPart = random.Next(1000, 9999);
            return $"TXN-{date}-{randomPart}";
        }

        private string GetCashOrBankAccount()
        {
            try
            {
                var cashAccount = _context.GlSetup
                    .FirstOrDefault(x => x.AccNo == "1010" && x.Status == true);
                return cashAccount?.AccNo ?? "1010";
            }
            catch
            {
                return "1010";
            }
        }

        private async Task<GlSetup?> GetSuspenseAccount(string companyCode)
        {
            return await _context.GlSetup
                .FirstOrDefaultAsync(x => x.IsSuspense == true
                                       && x.CompanyCode == companyCode
                                       && x.Status == true);
        }

        private async Task<decimal> CalculateNetIncome(
    List<GlSetup> accounts,
    List<Gltransaction> transactions,
    List<JournalsListing> journalListings,
    string companyCode,
    DateTime asAtDate)
        {
            decimal totalRevenue = 0;
            decimal totalExpenses = 0;

            // Get revenue accounts (Income Statement, Income group)
            var revenueAccounts = accounts.Where(x => x.Type == "Income Statement" &&
                                                      x.GlAccMainGroup?.ToLower() == "income");

            foreach (var account in revenueAccounts)
            {
                var balance = await CalculateAccountBalance(
                    account.AccNo ?? "", asAtDate, companyCode);

                // Revenue normally has CREDIT balance
                totalRevenue += balance;
            }

            // Get expense accounts (Income Statement, Expenses group)
            var expenseAccounts = accounts.Where(x => x.Type == "Income Statement" &&
                                                      x.GlAccMainGroup?.ToLower() == "expenses");

            foreach (var account in expenseAccounts)
            {
                var balance = await CalculateAccountBalance(
                    account.AccNo ?? "", asAtDate, companyCode);

                // Expenses normally have DEBIT balance
                totalExpenses += balance;
            }

            // Net Income = Revenue - Expenses
            return totalRevenue - totalExpenses;
        }
        private async Task<List<SuspenseAgingItem>> CalculateSuspenseAging(
            string suspenseAccountNo,
            string companyCode)
        {
            var suspenseEntries = await _context.JournalsListings
                .Where(x => x.AccountNo == suspenseAccountNo
                         && x.CompanyCode == companyCode
                         && x.TransType != "REF")
                .OrderBy(x => x.TransDate)
                .ToListAsync();

            var aging = new List<SuspenseAgingItem>();
            var now = DateTime.Now;

            foreach (var entry in suspenseEntries)
            {
                var daysOld = (now - (entry.TransDate ?? now)).Days;
                var amount = (entry.AmountDr ?? 0) - (entry.AmountCr ?? 0);

                aging.Add(new SuspenseAgingItem
                {
                    VoucherNo = entry.VoucherNo,
                    TransactionDate = entry.TransDate ?? now,
                    Description = entry.Narration,
                    Amount = Math.Abs(amount),
                    Type = amount > 0 ? "DR" : "CR",
                    DaysOld = daysOld,
                    AgingBucket = GetAgingBucket(daysOld)
                });
            }

            return aging;
        }

        private string GetAgingBucket(int daysOld)
        {
            if (daysOld <= 7) return "Current (0-7 days)";
            if (daysOld <= 30) return "1-30 days";
            if (daysOld <= 60) return "31-60 days";
            if (daysOld <= 90) return "61-90 days";
            return "Over 90 days";
        }

        private string GenerateCsvReport(FinancialReportViewModel model, string companyName, string companyCode)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"Company: {companyName} ({companyCode})");
            sb.AppendLine($"Report Type: {model.ReportType}");
            sb.AppendLine($"Period: {model.StartDate:dd/MM/yyyy} to {model.EndDate:dd/MM/yyyy}");
            sb.AppendLine($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");

            if (model.HasSuspenseAccount && Math.Abs(model.SuspenseBalance) > 0.01m)
            {
                sb.AppendLine($"*** WARNING: Suspense Account has balance of {model.SuspenseBalance:N2} ***");
            }
            sb.AppendLine();

            switch (model.ReportType)
            {
                case "TrialBalance":
                    sb.AppendLine("Trial Balance");
                    sb.AppendLine("Account No,Account Name,Account Type,Normal Balance,Debit,Credit,Balance,Suspense");
                    foreach (var item in model.TrialBalance)
                    {
                        sb.AppendLine($"{item.AccountNo},{item.AccountName},{item.AccountType},{item.NormalBalance},{item.Debit:N2},{item.Credit:N2},{item.Balance:N2},{(item.IsSuspense ? "YES" : "NO")}");
                    }
                    sb.AppendLine();
                    sb.AppendLine($"Total Debits:,{model.TotalDebits:N2}");
                    sb.AppendLine($"Total Credits:,{model.TotalCredits:N2}");
                    sb.AppendLine($"Difference:,{model.TotalDebits - model.TotalCredits:N2}");
                    break;

                case "IncomeStatement":
                    sb.AppendLine("Income Statement");
                    sb.AppendLine();
                    sb.AppendLine("Revenue");
                    foreach (var item in model.IncomeStatement.Revenue)
                    {
                        sb.AppendLine($"{item.AccountName},{item.Amount:N2}");
                    }
                    sb.AppendLine($"Total Revenue,{model.IncomeStatement.TotalRevenue:N2}");
                    sb.AppendLine();
                    sb.AppendLine("Expenses");
                    foreach (var item in model.IncomeStatement.Expenses)
                    {
                        sb.AppendLine($"{item.AccountName},{item.Amount:N2}");
                    }
                    sb.AppendLine($"Total Expenses,{model.IncomeStatement.TotalExpenses:N2}");
                    sb.AppendLine();
                    sb.AppendLine($"Net Income,{model.IncomeStatement.NetIncome:N2}");
                    break;

                case "BalanceSheet":
                    sb.AppendLine("Balance Sheet");
                    sb.AppendLine();
                    sb.AppendLine("Assets");
                    foreach (var item in model.BalanceSheet.Assets)
                    {
                        sb.AppendLine($"{item.AccountName},{item.Amount:N2}{(item.IsSuspense ? " (SUSPENSE)" : "")}");
                    }
                    sb.AppendLine($"Total Assets,{model.BalanceSheet.TotalAssets:N2}");
                    sb.AppendLine();
                    sb.AppendLine("Liabilities");
                    foreach (var item in model.BalanceSheet.Liabilities)
                    {
                        sb.AppendLine($"{item.AccountName},{item.Amount:N2}{(item.IsSuspense ? " (SUSPENSE)" : "")}");
                    }
                    sb.AppendLine($"Total Liabilities,{model.BalanceSheet.TotalLiabilities:N2}");
                    sb.AppendLine();
                    sb.AppendLine("Equity");
                    foreach (var item in model.BalanceSheet.Equity)
                    {
                        sb.AppendLine($"{item.AccountName},{item.Amount:N2}{(item.IsSuspense ? " (SUSPENSE)" : "")}");
                    }
                    sb.AppendLine($"Total Equity,{model.BalanceSheet.TotalEquity:N2}");
                    sb.AppendLine();
                    sb.AppendLine($"Total Liabilities & Equity,{model.BalanceSheet.TotalLiabilities + model.BalanceSheet.TotalEquity:N2}");

                    if (!model.BalanceSheetBalanced)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"*** BALANCE SHEET IS UNBALANCED by {model.BalanceSheetDifference:N2} ***");
                    }
                    break;
            }

            return sb.ToString();
        }
    }
}