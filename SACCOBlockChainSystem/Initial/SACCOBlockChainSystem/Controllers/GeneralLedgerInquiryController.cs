// Controllers/GeneralLedgerInquiryController.cs
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
    [Route("GLInquiry")]
    public class GeneralLedgerInquiryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GeneralLedgerInquiryController> _logger;

        public GeneralLedgerInquiryController(
            ApplicationDbContext context,
            ILogger<GeneralLedgerInquiryController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ===============================
        // GET: /GLInquiry
        // ===============================
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetCompanyCode();

                var model = new GeneralLedgerInquiryViewModel
                {
                    StartDate = DateTime.Now.AddMonths(-1),
                    EndDate = DateTime.Now,
                    Accounts = await GetActiveAccounts(companyCode)
                };

                ViewBag.CompanyCode = companyCode;
                ViewBag.CompanyName = GetCompanyName();

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading GL Inquiry page");
                TempData["Error"] = $"Error loading page: {ex.Message}";
                return View(new GeneralLedgerInquiryViewModel());
            }
        }

        // ===============================
        // POST: /GLInquiry/Search
        // ===============================
        [HttpPost("Search")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search([FromForm] GeneralLedgerInquiryViewModel model)
        {
            try
            {
                var companyCode = GetCompanyCode();

                // Validate dates
                if (model.StartDate > model.EndDate)
                {
                    TempData["Error"] = "Start date cannot be after end date.";
                    model.Accounts = await GetActiveAccounts(companyCode);
                    return View("Index", model);
                }

                if (string.IsNullOrEmpty(model.SelectedAccountNo))
                {
                    TempData["Error"] = "Please select an account.";
                    model.Accounts = await GetActiveAccounts(companyCode);
                    return View("Index", model);
                }

                // Get account details
                var account = await _context.GlSetup
                    .FirstOrDefaultAsync(x => x.AccNo == model.SelectedAccountNo
                                           && x.CompanyCode == companyCode);

                if (account == null)
                {
                    TempData["Error"] = "Selected account not found.";
                    model.Accounts = await GetActiveAccounts(companyCode);
                    return View("Index", model);
                }

                model.SelectedAccountName = account.Glaccname;
                model.CurrentAccount = $"{account.AccNo} - {account.Glaccname}";

                // Calculate opening balance as of start date
                model.OpeningBalance = await CalculateAccountBalance(
                    model.SelectedAccountNo,
                    model.StartDate.AddDays(-1),
                    companyCode);

                // ============================================
                // GET TRANSACTIONS FROM ALL SOURCES
                // ============================================
                var transactions = new List<GeneralLedgerTransaction>();

                // 1. Get from Gltransaction table (CONTRIBUTIONS and other GL entries)
                var glTransactions = await _context.Gltransactions
                    .Where(x => (x.DrAccNo == model.SelectedAccountNo || x.CrAccNo == model.SelectedAccountNo)
                             && x.TransDate >= model.StartDate
                             && x.TransDate <= model.EndDate
                             && x.CompanyCode == companyCode
                             && x.DocPosted == 1)  // Only posted entries
                    .OrderBy(x => x.TransDate)
                    .ThenBy(x => x.Id)
                    .ToListAsync();

                _logger.LogInformation($"Found {glTransactions.Count} transactions in Gltransaction table for account {model.SelectedAccountNo}");

                foreach (var gl in glTransactions)
                {
                    // Determine if this is a debit or credit for the selected account
                    bool isDebit = gl.DrAccNo == model.SelectedAccountNo;
                    bool isCredit = gl.CrAccNo == model.SelectedAccountNo;

                    // Extract member number from transaction description if possible
                    string memberNo = ExtractMemberNoFromDescription(gl.TransDescript);

                    transactions.Add(new GeneralLedgerTransaction
                    {
                        AuditId = gl.AuditId,
                        TransactionDate = gl.TransDate,
                        TransactionNo = gl.TransactionNo ?? gl.DocumentNo,
                        TransactionRemarks = gl.TransDescript ?? "No description",
                        MemberNo = memberNo,
                        DebitAmount = isDebit ? gl.Amount : 0,
                        CreditAmount = isCredit ? gl.Amount : 0,
                        DocumentNo = gl.DocumentNo,
                        Source = gl.Module ?? "GL",
                        Posted = gl.DocPosted == 1,
                        PostedDate = gl.AuditDateTime ?? gl.AuditTime,
                        Reference = gl.ChequeNo ?? gl.DocumentNo
                    });
                }

                // 2. Get from Contrib table (direct contributions - for backward compatibility)
                var contributions = await _context.Contribs
                    .Where(x => x.CompanyCode == companyCode
                             && x.ContrDate >= model.StartDate
                             && x.ContrDate <= model.EndDate
                             && x.Amount > 0)
                    .Include(x => x.SharescodeNavigation)
                    .ToListAsync();

                _logger.LogInformation($"Found {contributions.Count} contributions in Contrib table");

                foreach (var contrib in contributions)
                {
                    // Check if this contribution affects the selected account
                    bool affectsAccount = false;
                    decimal amount = contrib.Amount ?? 0;

                    // Check if the share type's accounts match the selected account
                    if (contrib.SharescodeNavigation != null)
                    {
                        if (contrib.SharescodeNavigation.SharesAcc == model.SelectedAccountNo ||
                            contrib.SharescodeNavigation.ContraAcc == model.SelectedAccountNo)
                        {
                            affectsAccount = true;
                        }
                    }

                    if (affectsAccount)
                    {
                        bool isDebit = contrib.SharescodeNavigation?.ContraAcc == model.SelectedAccountNo;
                        bool isCredit = contrib.SharescodeNavigation?.SharesAcc == model.SelectedAccountNo;

                        transactions.Add(new GeneralLedgerTransaction
                        {
                            AuditId = contrib.AuditId,
                            TransactionDate = contrib.ContrDate ?? contrib.TransDate ?? DateTime.Now,
                            TransactionNo = contrib.TransactionNo ?? contrib.ReceiptNo,
                            TransactionRemarks = $"Contribution - {contrib.SharescodeNavigation?.SharesType ?? contrib.Sharescode} - {contrib.Remarks}",
                            MemberNo = contrib.MemberNo,
                            DebitAmount = isDebit ? amount : 0,
                            CreditAmount = isCredit ? amount : 0,
                            DocumentNo = contrib.ReceiptNo,
                            Source = "CONTRIBUTION",
                            Posted = contrib.Posted == "Y",
                            PostedDate = contrib.AuditDateTime ?? contrib.AuditTime,
                            Reference = contrib.ChequeNo
                        });
                    }
                }

                // 3. Get from Journals table if you have any
                var journalEntries = await _context.Journals
                    .Where(x => x.ACCNO == model.SelectedAccountNo
                             && x.TRANSDATE >= model.StartDate
                             && x.TRANSDATE <= model.EndDate
                             && x.CompanyCode == companyCode
                             && x.POSTED == true)
                    .OrderBy(x => x.TRANSDATE)
                    .ThenBy(x => x.JVID)
                    .ToListAsync();

                _logger.LogInformation($"Found {journalEntries.Count} entries in Journals table");

                foreach (var j in journalEntries)
                {
                    transactions.Add(new GeneralLedgerTransaction
                    {
                        AuditId = j.AUDITID,
                        TransactionDate = j.TRANSDATE ?? DateTime.Now,
                        TransactionNo = j.Transactionno ?? j.VNO,
                        TransactionRemarks = j.NARATION ?? "No description",
                        MemberNo = j.MEMBERNO ?? "-",
                        DebitAmount = j.TRANSTYPE == "DR" ? j.AMOUNT ?? 0 : 0,
                        CreditAmount = j.TRANSTYPE == "CR" ? j.AMOUNT ?? 0 : 0,
                        DocumentNo = j.VNO,
                        Source = "JOURNAL",
                        Posted = j.POSTED,
                        PostedDate = j.POSTEDDATE,
                        Reference = $"JV {j.VNO}"
                    });
                }

                // 4. Get from JournalsListings (draft entries)
                var journalListings = await _context.JournalsListings
                    .Where(x => x.AccountNo == model.SelectedAccountNo
                             && x.TransDate >= model.StartDate
                             && x.TransDate <= model.EndDate
                             && x.CompanyCode == companyCode
                             && x.TransType != "REF"
                             && x.TransType != "GRP")
                    .OrderBy(x => x.TransDate)
                    .ThenBy(x => x.JlId)
                    .ToListAsync();

                _logger.LogInformation($"Found {journalListings.Count} entries in JournalsListings table");

                foreach (var j in journalListings)
                {
                    transactions.Add(new GeneralLedgerTransaction
                    {
                        AuditId = j.AuditId,
                        TransactionDate = j.TransDate ?? DateTime.Now,
                        TransactionNo = j.VoucherNo,
                        TransactionRemarks = j.Narration ?? "No description",
                        MemberNo = j.MemberNo ?? "-",
                        DebitAmount = j.AmountDr ?? 0,
                        CreditAmount = j.AmountCr ?? 0,
                        DocumentNo = j.VoucherNo,
                        Source = "DRAFT",
                        Posted = j.Posted,
                        PostedDate = j.PostedDate,
                        Reference = j.VoucherNo
                    });
                }

                // Sort all transactions by date
                transactions = transactions
                    .OrderBy(x => x.TransactionDate)
                    .ThenBy(x => x.TransactionNo)
                    .ToList();

                model.Transactions = transactions;

                // Calculate running balance
                decimal runningBalance = model.OpeningBalance;
                foreach (var transaction in model.Transactions)
                {
                    if (account.Normalbal == "DR")
                    {
                        runningBalance += transaction.DebitAmount - transaction.CreditAmount;
                    }
                    else
                    {
                        runningBalance += transaction.CreditAmount - transaction.DebitAmount;
                    }
                    transaction.RunningBalance = runningBalance;
                }

                // Calculate period totals
                var periodDebit = model.Transactions.Sum(x => x.DebitAmount);
                var periodCredit = model.Transactions.Sum(x => x.CreditAmount);

                if (account.Normalbal == "DR")
                {
                    model.BookBalance = model.OpeningBalance + periodDebit - periodCredit;
                }
                else
                {
                    model.BookBalance = model.OpeningBalance + periodCredit - periodDebit;
                }

                model.Accounts = await GetActiveAccounts(companyCode);

                _logger.LogInformation($"GL Inquiry executed for Account: {model.SelectedAccountNo}, " +
                    $"Period: {model.StartDate:dd/MM/yyyy} - {model.EndDate:dd/MM/yyyy}, " +
                    $"Found {model.Transactions.Count} transactions " +
                    $"({glTransactions.Count} from Gltransaction, {contributions.Count} from Contrib, " +
                    $"{journalEntries.Count} posted, {journalListings.Count} draft)");

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GL Inquiry");
                TempData["Error"] = $"Error executing inquiry: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper method to extract member number from description
        private string ExtractMemberNoFromDescription(string? description)
        {
            if (string.IsNullOrEmpty(description))
                return "-";

            // Look for member number patterns (e.g., M001234, MEM001, or just numbers)
            var words = description.Split(' ', '-', ',');
            foreach (var word in words)
            {
                // Check for member number patterns
                if ((word.StartsWith("M") || word.StartsWith("MEM") || word.StartsWith("MBR")) && word.Length >= 4)
                {
                    return word;
                }
                // Check if it's a numeric ID that looks like a member number (6-10 digits)
                if (word.Length >= 6 && word.Length <= 10 && word.All(char.IsDigit))
                {
                    return word;
                }
            }
            return "-";
        }

        // ===============================
        // GET: /GLInquiry/AccountDetails/{accountNo}
        // ===============================
        [HttpGet("AccountDetails/{accountNo}")]
        public async Task<IActionResult> AccountDetails(string accountNo, DateTime? asAt)
        {
            try
            {
                var companyCode = GetCompanyCode();
                var asAtDate = asAt ?? DateTime.Now;

                var account = await _context.GlSetup
                    .FirstOrDefaultAsync(x => x.AccNo == accountNo
                                           && x.CompanyCode == companyCode);

                if (account == null)
                    return NotFound();

                var balance = await CalculateAccountBalance(accountNo, asAtDate, companyCode);
                var transactions = await GetAllAccountTransactions(
                    accountNo,
                    asAtDate.AddMonths(-3),
                    asAtDate,
                    companyCode,
                    100);

                return Json(new
                {
                    success = true,
                    accountNo = account.AccNo,
                    accountName = account.Glaccname,
                    normalBalance = account.Normalbal,
                    currentBalance = balance,
                    openingBalance = account.OpeningBal,
                    transactionCount = transactions.Count,
                    recentTransactions = transactions.Take(10)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting account details for {accountNo}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ===============================
        // GET: /GLInquiry/DownloadStatement
        // ===============================
        [HttpGet("DownloadStatement")]
        public async Task<IActionResult> DownloadStatement(string accountNo, DateTime startDate, DateTime endDate)
        {
            try
            {
                var companyCode = GetCompanyCode();

                var account = await _context.GlSetup
                    .FirstOrDefaultAsync(x => x.AccNo == accountNo
                                           && x.CompanyCode == companyCode);

                if (account == null)
                    return NotFound();

                var transactions = await GetAllAccountTransactions(
                    accountNo, startDate, endDate, companyCode);

                var openingBalance = await CalculateAccountBalance(
                    accountNo, startDate.AddDays(-1), companyCode);

                // Generate CSV content
                var csv = GenerateCsvStatement(account, transactions, openingBalance, startDate, endDate);

                var fileName = $"GL_Statement_{accountNo}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv";
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading statement");
                TempData["Error"] = $"Error downloading statement: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ===============================
        // GET: /GLInquiry/Print/{accountNo}
        // ===============================
        [HttpGet("Print/{accountNo}")]
        public async Task<IActionResult> Print(string accountNo, DateTime startDate, DateTime endDate)
        {
            try
            {
                var companyCode = GetCompanyCode();

                var account = await _context.GlSetup
                    .FirstOrDefaultAsync(x => x.AccNo == accountNo
                                           && x.CompanyCode == companyCode);

                if (account == null)
                    return NotFound();

                var transactions = await GetAllAccountTransactions(
                    accountNo, startDate, endDate, companyCode);

                var openingBalance = await CalculateAccountBalance(
                    accountNo, startDate.AddDays(-1), companyCode);

                ViewBag.Account = account;
                ViewBag.OpeningBalance = openingBalance;
                ViewBag.StartDate = startDate;
                ViewBag.EndDate = endDate;
                ViewBag.CompanyName = GetCompanyName();
                ViewBag.CompanyCode = companyCode;
                ViewBag.PrintDate = DateTime.Now;

                return View(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating print view");
                TempData["Error"] = $"Error generating print view: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ===============================
        // Private Helper Methods
        // ===============================
        private string GetCompanyCode()
        {
            var claim = User.FindFirst("CompanyCode");
            return claim?.Value ?? "001";
        }

        private string GetCompanyName()
        {
            var claim = User.FindFirst("CompanyName");
            return claim?.Value ?? "Main SACCO";
        }

        private async Task<List<AccountDropdownViewModel>> GetActiveAccounts(string companyCode)
        {
            return await _context.GlSetup
                .Where(x => x.Status == true
                         && x.CompanyCode == companyCode)
                .OrderBy(x => x.AccNo)
                .Select(x => new AccountDropdownViewModel
                {
                    AccountNo = x.AccNo ?? "",
                    AccountName = x.Glaccname ?? "",
                    NormalBalance = x.Normalbal,
                    IsActive = x.Status
                })
                .ToListAsync();
        }

        private async Task<decimal> CalculateAccountBalance(
            string accountNo,
            DateTime asAtDate,
            string companyCode)
        {
            // Get account details
            var account = await _context.GlSetup
                .FirstOrDefaultAsync(x => x.AccNo == accountNo
                                       && x.CompanyCode == companyCode);

            var openingBalance = account?.OpeningBal ?? 0;

            decimal totalDebit = 0;
            decimal totalCredit = 0;

            // 1. Get from Gltransaction table
            var glTransactions = await _context.Gltransactions
                .Where(x => (x.DrAccNo == accountNo || x.CrAccNo == accountNo)
                         && x.TransDate <= asAtDate
                         && x.CompanyCode == companyCode
                         && x.DocPosted == 1)
                .ToListAsync();

            if (glTransactions.Any())
            {
                totalDebit += glTransactions.Where(x => x.DrAccNo == accountNo).Sum(x => x.Amount);
                totalCredit += glTransactions.Where(x => x.CrAccNo == accountNo).Sum(x => x.Amount);
                _logger.LogDebug($"Gltransaction table - Account {accountNo}: Debits={totalDebit:N2}, Credits={totalCredit:N2}");
            }

            // 2. Get from Journals table
            var journalEntries = await _context.Journals
                .Where(x => x.ACCNO == accountNo
                         && x.TRANSDATE <= asAtDate
                         && x.CompanyCode == companyCode
                         && x.POSTED == true)
                .ToListAsync();

            if (journalEntries.Any())
            {
                totalDebit += journalEntries
                    .Where(x => x.TRANSTYPE == "DR")
                    .Sum(x => x.AMOUNT ?? 0);

                totalCredit += journalEntries
                    .Where(x => x.TRANSTYPE == "CR")
                    .Sum(x => x.AMOUNT ?? 0);

                _logger.LogDebug($"Journals table - Account {accountNo}: Debits={totalDebit:N2}, Credits={totalCredit:N2}");
            }

            // 3. Get from JournalsListings
            var journalListings = await _context.JournalsListings
                .Where(x => x.AccountNo == accountNo
                         && x.TransDate <= asAtDate
                         && x.CompanyCode == companyCode
                         && x.TransType != "REF"
                         && x.TransType != "GRP")
                .ToListAsync();

            if (journalListings.Any())
            {
                totalDebit += journalListings.Sum(x => x.AmountDr ?? 0);
                totalCredit += journalListings.Sum(x => x.AmountCr ?? 0);
                _logger.LogDebug($"JournalsListings table - Account {accountNo}: Debits={totalDebit:N2}, Credits={totalCredit:N2}");
            }

            _logger.LogInformation($"Account {accountNo}: Total Debits={totalDebit:N2}, Total Credits={totalCredit:N2}");

            if (account?.Normalbal == "DR")
            {
                return openingBalance + totalDebit - totalCredit;
            }
            else
            {
                return openingBalance + totalCredit - totalDebit;
            }
        }

        private async Task<List<GeneralLedgerTransaction>> GetAllAccountTransactions(
            string accountNo,
            DateTime startDate,
            DateTime endDate,
            string companyCode,
            int? limit = null)
        {
            var result = new List<GeneralLedgerTransaction>();

            // 1. Get from Gltransaction table
            var glTransactions = await _context.Gltransactions
                .Where(x => (x.DrAccNo == accountNo || x.CrAccNo == accountNo)
                         && x.TransDate >= startDate
                         && x.TransDate <= endDate
                         && x.CompanyCode == companyCode
                         && x.DocPosted == 1)
                .OrderBy(x => x.TransDate)
                .ThenBy(x => x.Id)
                .ToListAsync();

            foreach (var gl in glTransactions)
            {
                bool isDebit = gl.DrAccNo == accountNo;
                bool isCredit = gl.CrAccNo == accountNo;
                string memberNo = ExtractMemberNoFromDescription(gl.TransDescript);

                result.Add(new GeneralLedgerTransaction
                {
                    AuditId = gl.AuditId,
                    TransactionDate = gl.TransDate,
                    TransactionNo = gl.TransactionNo ?? gl.DocumentNo,
                    TransactionRemarks = gl.TransDescript ?? "No description",
                    MemberNo = memberNo,
                    DebitAmount = isDebit ? gl.Amount : 0,
                    CreditAmount = isCredit ? gl.Amount : 0,
                    DocumentNo = gl.DocumentNo,
                    Source = gl.Module ?? "GL",
                    Posted = gl.DocPosted == 1,
                    PostedDate = gl.AuditDateTime ?? gl.AuditTime,
                    Reference = gl.ChequeNo ?? gl.DocumentNo
                });
            }

            // 2. Get from Journals table
            var journalEntries = await _context.Journals
                .Where(x => x.ACCNO == accountNo
                         && x.TRANSDATE >= startDate
                         && x.TRANSDATE <= endDate
                         && x.CompanyCode == companyCode
                         && x.POSTED == true)
                .OrderBy(x => x.TRANSDATE)
                .ThenBy(x => x.JVID)
                .ToListAsync();

            foreach (var j in journalEntries)
            {
                result.Add(new GeneralLedgerTransaction
                {
                    AuditId = j.AUDITID,
                    TransactionDate = j.TRANSDATE ?? DateTime.Now,
                    TransactionNo = j.Transactionno ?? j.VNO,
                    TransactionRemarks = j.NARATION ?? "No description",
                    MemberNo = j.MEMBERNO ?? "-",
                    DebitAmount = j.TRANSTYPE == "DR" ? j.AMOUNT ?? 0 : 0,
                    CreditAmount = j.TRANSTYPE == "CR" ? j.AMOUNT ?? 0 : 0,
                    DocumentNo = j.VNO,
                    Source = "JOURNAL",
                    Posted = j.POSTED,
                    PostedDate = j.POSTEDDATE,
                    Reference = $"JV {j.VNO}"
                });
            }

            // 3. Get from JournalsListings
            var journalListings = await _context.JournalsListings
                .Where(x => x.AccountNo == accountNo
                         && x.TransDate >= startDate
                         && x.TransDate <= endDate
                         && x.CompanyCode == companyCode
                         && x.TransType != "REF"
                         && x.TransType != "GRP")
                .OrderBy(x => x.TransDate)
                .ThenBy(x => x.JlId)
                .ToListAsync();

            foreach (var j in journalListings)
            {
                result.Add(new GeneralLedgerTransaction
                {
                    AuditId = j.AuditId,
                    TransactionDate = j.TransDate ?? DateTime.Now,
                    TransactionNo = j.VoucherNo,
                    TransactionRemarks = j.Narration ?? "No description",
                    MemberNo = j.MemberNo ?? "-",
                    DebitAmount = j.AmountDr ?? 0,
                    CreditAmount = j.AmountCr ?? 0,
                    DocumentNo = j.VoucherNo,
                    Source = "DRAFT",
                    Posted = j.Posted,
                    PostedDate = j.PostedDate,
                    Reference = j.VoucherNo
                });
            }

            // Sort and limit
            result = result.OrderBy(x => x.TransactionDate)
                           .ThenBy(x => x.TransactionNo)
                           .ToList();

            if (limit.HasValue)
                result = result.Take(limit.Value).ToList();

            // Calculate running balance
            decimal runningBalance = await CalculateAccountBalance(accountNo, startDate.AddDays(-1), companyCode);
            var account = await _context.GlSetup
                .FirstOrDefaultAsync(x => x.AccNo == accountNo);

            foreach (var transaction in result)
            {
                if (account?.Normalbal == "DR")
                {
                    runningBalance += transaction.DebitAmount - transaction.CreditAmount;
                }
                else
                {
                    runningBalance += transaction.CreditAmount - transaction.DebitAmount;
                }

                transaction.RunningBalance = runningBalance;
            }

            return result;
        }

        private string GenerateCsvStatement(
            GlSetup account,
            List<GeneralLedgerTransaction> transactions,
            decimal openingBalance,
            DateTime startDate,
            DateTime endDate)
        {
            var sb = new System.Text.StringBuilder();

            // Header
            sb.AppendLine($"General Ledger Statement");
            sb.AppendLine($"Account: {account.AccNo} - {account.Glaccname}");
            sb.AppendLine($"Period: {startDate:dd/MM/yyyy} to {endDate:dd/MM/yyyy}");
            sb.AppendLine($"Opening Balance: {openingBalance:N2}");
            sb.AppendLine();

            // Column Headers
            sb.AppendLine("Date,Transaction No,Description,Member No,Debit,Credit,Balance,Source");

            // Transactions
            foreach (var t in transactions)
            {
                sb.AppendLine($"{t.TransactionDate:dd/MM/yyyy}," +
                    $"{t.TransactionNo}," +
                    $"\"{t.TransactionRemarks?.Replace("\"", "\"\"")}\"," +
                    $"{t.MemberNo}," +
                    $"{t.DebitAmount:N2}," +
                    $"{t.CreditAmount:N2}," +
                    $"{t.RunningBalance:N2}," +
                    $"{t.Source}");
            }

            // Footer
            sb.AppendLine();
            sb.AppendLine($"Total Debits: {transactions.Sum(x => x.DebitAmount):N2}");
            sb.AppendLine($"Total Credits: {transactions.Sum(x => x.CreditAmount):N2}");
            sb.AppendLine($"Closing Balance: {(transactions.Any() ? transactions.Last().RunningBalance : openingBalance):N2}");
            sb.AppendLine($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");

            return sb.ToString();
        }
    }
}