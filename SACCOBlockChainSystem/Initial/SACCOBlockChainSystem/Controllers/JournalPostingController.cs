using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    [Route("Journal")]
    public class JournalPostingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<JournalPostingController> _logger;

        public JournalPostingController(
            ApplicationDbContext context,
            ILogger<JournalPostingController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string? voucherNo)
        {
            var companyCode = User.FindFirst("CompanyCode")?.Value ?? "001";

            var accounts = await _context.GlSetup
                .Where(x => x.Status == true && x.CompanyCode == companyCode)
                .ToListAsync();

            ViewBag.Accounts = accounts;
            ViewBag.CompanyCode = companyCode;
            ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "Main SACCO";

            var model = new JournalEntryViewModel
            {
                VoucherNo = voucherNo ?? await GenerateVoucherNumberAsync(),
                VoucherDate = DateTime.Now,
                CompanyCode = companyCode,
                AuditId = User.Identity?.Name ?? "SYSTEM",
                AuditDate = DateTime.Now
            };

            // 🔥 VERY IMPORTANT PART
            if (!string.IsNullOrEmpty(voucherNo))
            {
                var journalEntries = await _context.Journals
                    .Where(x => x.VNO == voucherNo && x.CompanyCode == companyCode)
                    .ToListAsync();

                model.Details = journalEntries.Select(x => new JournalDetailViewModel
                {
                    AccountNo = x.ACCNO,
                    AccountName = x.NAME,
                    Narration = x.NARATION,
                    Debit = x.AMOUNT ?? 0,
                    Credit = x.AMOUNT ?? 0,
                    Posted = x.POSTED
                }).ToList();

                model.Posted = journalEntries.FirstOrDefault()?.POSTED ?? false;
                model.PostedDate = journalEntries.FirstOrDefault()?.POSTEDDATE ?? DateTime.MinValue;
            }

            return View(model);
        }

        // ===============================
        // POST: /Journal/Save
        // ===============================
        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] JournalEntryViewModel model)
        {
            try
            {
                #region Validation
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();

                    return Json(new
                    {
                        success = false,
                        message = "Invalid journal data.",
                        errors = errors
                    });
                }

                if (model.Details == null || !model.Details.Any())
                    return Json(new { success = false, message = "Journal must have at least one line." });

                if (model.Details.Count < 2)
                    return Json(new { success = false, message = "Journal must have at least two lines (Debit and Credit)." });

                // Validate each line has either debit or credit
                foreach (var detail in model.Details)
                {
                    if (string.IsNullOrEmpty(detail.AccountNo))
                        return Json(new { success = false, message = "Account number is required for all lines." });

                    if (detail.Debit < 0 || detail.Credit < 0)
                        return Json(new { success = false, message = "Debit and Credit amounts cannot be negative." });

                    if (detail.Debit == 0 && detail.Credit == 0)
                        return Json(new { success = false, message = "Each line must have either Debit or Credit amount." });

                    if (detail.Debit > 0 && detail.Credit > 0)
                        return Json(new { success = false, message = "A line cannot have both Debit and Credit amounts." });
                }
                #endregion

                #region Check for Existing Journal
                // Check if journal already exists
                var existingJournal = await _context.Journals
                    .AnyAsync(x => x.VNO == model.VoucherNo);

                if (existingJournal)
                {
                    // If editing existing journal, delete old entries first
                    var oldEntries = await _context.Journals
                        .Where(x => x.VNO == model.VoucherNo)
                        .ToListAsync();

                    var oldListingEntries = await _context.JournalsListings
                        .Where(x => x.VoucherNo == model.VoucherNo)
                        .ToListAsync();

                    if (oldEntries.Any())
                    {
                        if (oldEntries.First().POSTED)
                        {
                            return Json(new
                            {
                                success = false,
                                message = "Cannot edit a posted journal. Please create a new one."
                            });
                        }

                        _context.Journals.RemoveRange(oldEntries);
                        _context.JournalsListings.RemoveRange(oldListingEntries);
                        await _context.SaveChangesAsync();
                    }
                }
                #endregion

                #region Prepare Journal Entries
                var companyCode = model.CompanyCode ?? User.FindFirst("CompanyCode")?.Value ?? "001";
                var auditUser = User.Identity?.Name ?? "SYSTEM";
                var now = DateTime.Now;

                // Generate voucher number if not provided
                if (string.IsNullOrEmpty(model.VoucherNo))
                {
                    model.VoucherNo = await GenerateVoucherNumberAsync();
                }

                var journals = new List<Journal>();
                var journalListings = new List<JournalsListing>();
                var detailList = model.Details.Where(d => d.Debit > 0 || d.Credit > 0).ToList();

                foreach (var d in detailList)
                {
                    // Get account name from GL Setup if not provided
                    if (string.IsNullOrEmpty(d.AccountName))
                    {
                        var account = await _context.GlSetup
                            .FirstOrDefaultAsync(x => x.AccNo == d.AccountNo);
                        d.AccountName = account?.Glaccname;
                    }

                    var amount = d.Debit > 0 ? d.Debit : d.Credit;
                    var transType = d.Debit > 0 ? "DR" : "CR";

                    // Add to Journals table
                    journals.Add(new Journal
                    {
                        VNO = model.VoucherNo,
                        ACCNO = d.AccountNo,
                        NAME = d.AccountName,
                        NARATION = d.Narration ?? model.Description,
                        MEMBERNO = model.MemberNo ?? "SYSTEM",
                        SHARETYPE = d.ShareType ?? "CASH",
                        Loanno = model.LoanNo ?? "0",
                        AMOUNT = amount,
                        TRANSTYPE = transType,
                        TRANSDATE = model.VoucherDate,
                        AUDITID = auditUser,
                        AUDITDATE = now,
                        POSTED = false,
                        POSTEDDATE = DateTime.MinValue,
                        Transactionno = model.TransactionNo,
                        CompanyCode = companyCode
                    });

                    // Add to JournalsListing table
                    journalListings.Add(new JournalsListing
                    {
                        VoucherNo = model.VoucherNo,
                        AccountNo = d.AccountNo,
                        AccountName = d.AccountName,
                        Narration = d.Narration ?? model.Description,
                        MemberNo = model.MemberNo ?? "SYSTEM",
                        ShareType = d.ShareType ?? "CASH",
                        LoanNo = model.LoanNo ?? "0",
                        AmountDr = d.Debit > 0 ? d.Debit : (decimal?)null,
                        AmountCr = d.Credit > 0 ? d.Credit : (decimal?)null,
                        Amount = amount,
                        TransType = transType,
                        AuditId = auditUser,
                        TransDate = model.VoucherDate,
                        AuditDate = now,
                        Posted = false,
                        PostedDate = DateTime.MinValue,
                        TransactionNo = model.TransactionNo,
                        CompanyCode = companyCode
                    });
                }
                #endregion

                #region Validate Balance
                decimal totalDr = journals.Where(x => x.TRANSTYPE == "DR").Sum(x => x.AMOUNT ?? 0);
                decimal totalCr = journals.Where(x => x.TRANSTYPE == "CR").Sum(x => x.AMOUNT ?? 0);

                if (Math.Abs(totalDr - totalCr) > 0.01m)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Journal is not balanced! Debit: {totalDr:N2}, Credit: {totalCr:N2}",
                        totalDebit = totalDr,
                        totalCredit = totalCr,
                        difference = totalDr - totalCr
                    });
                }
                #endregion

                #region Save to Database
                await _context.Journals.AddRangeAsync(journals);
                await _context.JournalsListings.AddRangeAsync(journalListings);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Journal saved successfully. Voucher No: {model.VoucherNo}, Entries: {journals.Count}, DR: {totalDr:N2}, CR: {totalCr:N2}");
                #endregion

                return Json(new
                {
                    success = true,
                    message = "Journal saved successfully!",
                    voucherNo = model.VoucherNo,
                    entries = journals.Count,
                    totalDebit = totalDr,
                    totalCredit = totalCr,
                    isBalanced = true
                });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error saving journal {model.VoucherNo}");

                // Check for specific SQL errors
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;

                if (innerMessage.Contains("FOREIGN KEY"))
                {
                    return Json(new { success = false, message = "Invalid account number or reference." });
                }
                else if (innerMessage.Contains("duplicate"))
                {
                    return Json(new { success = false, message = "Duplicate entry detected. Please try again." });
                }

                return Json(new { success = false, message = $"Database error: {innerMessage}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving journal {model.VoucherNo}");
                return Json(new
                {
                    success = false,
                    message = $"Error saving journal: {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }
        [HttpPost("ProcessAndCombine")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessAndCombine([FromBody] ProcessAndCombineViewModel model)
        {
            IDbContextTransaction? trx = null;

            try
            {
                #region Validation
                if (model == null)
                    return Json(new { success = false, message = "Invalid data." });

                _logger.LogInformation("===== PROCESS AND COMBINE JOURNAL =====");
                _logger.LogInformation($"VoucherNo: {model.VoucherNo}");
                _logger.LogInformation($"OriginalEntries count: {model.OriginalEntries?.Count ?? 0}");

                if (model.OriginalEntries == null || !model.OriginalEntries.Any())
                    return Json(new { success = false, message = "No entries provided." });

                // Start transaction
                trx = await _context.Database.BeginTransactionAsync();

                // =========================================================
                // DELETE ALL EXISTING UNPOSTED ENTRIES FOR THIS VOUCHER
                // =========================================================
                var existingJournals = await _context.Journals
                    .Where(x => x.VNO == model.VoucherNo && x.POSTED == false)
                    .ToListAsync();

                var existingListings = await _context.JournalsListings
                    .Where(x => x.VoucherNo == model.VoucherNo && x.Posted == false)
                    .ToListAsync();

                if (existingJournals.Any() || existingListings.Any())
                {
                    _logger.LogInformation($"Deleting {existingJournals.Count} existing journal entries and {existingListings.Count} listings for voucher {model.VoucherNo}");

                    if (existingJournals.Any())
                        _context.Journals.RemoveRange(existingJournals);

                    if (existingListings.Any())
                        _context.JournalsListings.RemoveRange(existingListings);

                    await _context.SaveChangesAsync();
                }

                // Calculate totals and validate balance
                decimal totalDebit = 0;
                decimal totalCredit = 0;

                foreach (var entry in model.OriginalEntries)
                {
                    _logger.LogInformation($"Entry - Account: {entry.AccountNo}, DR: {entry.Debit}, CR: {entry.Credit}");
                    totalDebit += entry.Debit;
                    totalCredit += entry.Credit;
                }

                _logger.LogInformation($"Total Debit: {totalDebit:N2}, Total Credit: {totalCredit:N2}");

                if (Math.Abs(totalDebit - totalCredit) > 0.01m)
                {
                    await trx.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = $"Entries not balanced. DR: {totalDebit:N2}, CR: {totalCredit:N2}"
                    });
                }
                #endregion

                var companyCode = model.CompanyCode ?? User.FindFirst("CompanyCode")?.Value ?? "001";
                var auditUser = User.Identity?.Name ?? "SYSTEM";
                var now = DateTime.Now;

                // Create entries for EACH original entry (don't combine into one)
                var journalsList = new List<Journal>();
                var listingsList = new List<JournalsListing>();

                foreach (var entry in model.OriginalEntries)
                {
                    var amount = entry.Debit > 0 ? entry.Debit : entry.Credit;
                    var transType = entry.Debit > 0 ? "DR" : "CR";

                    // Add to Journals table
                    journalsList.Add(new Journal
                    {
                        VNO = model.VoucherNo,
                        ACCNO = entry.AccountNo,
                        NAME = entry.AccountName ?? entry.AccountNo,
                        NARATION = entry.Narration ?? model.Description,
                        MEMBERNO = model.MemberNo ?? "SYSTEM",
                        SHARETYPE = entry.ShareType ?? "CASH",
                        Loanno = model.LoanNo ?? "0",
                        AMOUNT = amount,
                        TRANSTYPE = transType,
                        TRANSDATE = DateTime.Now,
                        AUDITID = auditUser,
                        AUDITDATE = now,
                        POSTED = false,
                        POSTEDDATE = DateTime.MinValue,
                        Transactionno = GenerateTransactionNumber(),
                        CompanyCode = companyCode
                    });

                    // Add to JournalsListing table
                    listingsList.Add(new JournalsListing
                    {
                        VoucherNo = model.VoucherNo,
                        AccountNo = entry.AccountNo,
                        AccountName = entry.AccountName ?? entry.AccountNo,
                        Narration = entry.Narration ?? model.Description,
                        MemberNo = model.MemberNo ?? "SYSTEM",
                        ShareType = entry.ShareType ?? "CASH",
                        LoanNo = model.LoanNo ?? "0",
                        AmountDr = entry.Debit > 0 ? entry.Debit : (decimal?)null,
                        AmountCr = entry.Credit > 0 ? entry.Credit : (decimal?)null,
                        Amount = amount,
                        TransType = transType,
                        AuditId = auditUser,
                        TransDate = DateTime.Now,
                        AuditDate = now,
                        Posted = false,
                        PostedDate = DateTime.MinValue,
                        TransactionNo = GenerateTransactionNumber(),
                        CompanyCode = companyCode
                    });
                }

                await _context.Journals.AddRangeAsync(journalsList);
                await _context.JournalsListings.AddRangeAsync(listingsList);
                await _context.SaveChangesAsync();

                await trx.CommitAsync();

                _logger.LogInformation($"Successfully saved {model.OriginalEntries.Count} entries for voucher {model.VoucherNo}");

                return Json(new
                {
                    success = true,
                    message = "Entries saved successfully.",
                    voucherNo = model.VoucherNo,
                    totalDebit = totalDebit,
                    totalCredit = totalCredit,
                    entries = model.OriginalEntries.Count
                });
            }
            catch (Exception ex)
            {
                if (trx != null) await trx.RollbackAsync();
                _logger.LogError(ex, "Error saving journal entries");
                return Json(new
                {
                    success = false,
                    message = "Error saving entries: " + ex.Message
                });
            }
        }
        // ===============================
        // POST: /Journal/Post
        // ===============================
        [HttpPost("Post/{voucherNo}")]
        public async Task<IActionResult> Post(string voucherNo)
        {
            IDbContextTransaction? trx = null;

            try
            {
                if (string.IsNullOrWhiteSpace(voucherNo))
                    return Json(new { success = false, message = "Voucher number is required." });

                trx = await _context.Database.BeginTransactionAsync();

                // 🔹 Load unposted journals
                var journals = await _context.Journals
                    .Where(x => x.VNO == voucherNo && !x.POSTED)
                    .OrderBy(x => x.JVID)
                    .ToListAsync();

                var listings = await _context.JournalsListings
                    .Where(x => x.VoucherNo == voucherNo && !x.Posted)
                    .OrderBy(x => x.JlId)
                    .ToListAsync();

                if (!journals.Any() && !listings.Any())
                    return Json(new { success = false, message = "Journal already posted or not found." });

                // 🔹 Calculate balance
                decimal totalDr = 0;
                decimal totalCr = 0;

                foreach (var j in journals)
                {
                    if (j.TRANSTYPE == "DR")
                        totalDr += j.AMOUNT ?? 0;
                    else
                        totalCr += j.AMOUNT ?? 0;
                }

                foreach (var jl in listings)
                {
                    if (jl.TransType == "DR")
                        totalDr += jl.Amount ?? 0;
                    else
                        totalCr += jl.Amount ?? 0;
                }

                if (Math.Abs(totalDr - totalCr) > 0.01m)
                    return Json(new
                    {
                        success = false,
                        message = $"Journal not balanced. DR: {totalDr:N2}, CR: {totalCr:N2}"
                    });

                string companyCode =
                    journals.FirstOrDefault()?.CompanyCode ??
                    listings.FirstOrDefault()?.CompanyCode ??
                    "001";

                string auditUser = User.Identity?.Name ?? "SYSTEM";
                DateTime now = DateTime.Now;
                string txnNo = GenerateTransactionNumber();

                var glList = new List<Gltransaction>();

                // 🔹 Post Journals table
                for (int i = 0; i < journals.Count; i += 2)
                {
                    var dr = journals.FirstOrDefault(x => x.TRANSTYPE == "DR");
                    var cr = journals.FirstOrDefault(x => x.TRANSTYPE == "CR");

                    if (dr != null && cr != null)
                    {
                        glList.Add(new Gltransaction
                        {
                            TransDate = now,
                            Amount = dr.AMOUNT ?? 0,
                            DrAccNo = dr.ACCNO,
                            CrAccNo = cr.ACCNO,
                            DocumentNo = voucherNo,
                            Source = "JOURNAL",
                            Temp = "JV",
                            CompanyCode = companyCode,
                            TransDescript = dr.NARATION,
                            AuditId = auditUser,
                            AuditTime = now,
                            TransactionNo = txnNo,
                            DocPosted = 1,
                            Module = "GL"
                        });
                    }
                }

                // 🔹 Post JournalsListings table
                // Combine DR and CR into proper GL rows
                var drEntries = journals.Where(x => x.TRANSTYPE == "DR").ToList();
                var crEntries = journals.Where(x => x.TRANSTYPE == "CR").ToList();

                for (int i = 0; i < drEntries.Count; i++)
                {
                    var dr = drEntries[i];
                    var cr = crEntries[i];
                    
                    glList.Add(new Gltransaction
                    {
                        TransDate = now,
                        Amount = dr.AMOUNT ?? 0,
                        DrAccNo = dr.ACCNO,
                        CrAccNo = cr.ACCNO,
                        DocumentNo = voucherNo,
                        Source = "JV",
                        Temp = "JV",
                        CompanyCode = companyCode,
                        TransDescript = dr.NARATION,
                        AuditId = auditUser,
                        AuditTime = now,
                        TransactionNo = GenerateTransactionNumber(),
                        DocPosted = 1,
                        Module = "GL"
                    });

                    // Mark posted
                    dr.POSTED = true;
                    cr.POSTED = true;
                    dr.POSTEDDATE = now;
                    cr.POSTEDDATE = now;
                }
                await _context.Gltransactions.AddRangeAsync(glList);
                await _context.SaveChangesAsync();
                await trx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = "Journal posted successfully",
                    voucherNo,
                    transactionNo = txnNo,
                    totalDebit = totalDr,
                    totalCredit = totalCr
                });
            }
            catch (Exception ex)
            {
                if (trx != null) await trx.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ===============================
        // GET: /Journal/List
        // ===============================
        [HttpGet("List")]
        public async Task<IActionResult> List(
            DateTime? fromDate,
            DateTime? toDate,
            string? memberNo,
            string? loanNo)
        {
            try
            {
                var query = _context.Journals
                    .Where(x => x.CompanyCode == User.FindFirst("CompanyCode")!.Value)
                    .GroupBy(x => x.VNO)
                    .Select(g => new
                    {
                        VoucherNo = g.Key,
                        VoucherDate = g.OrderBy(x => x.JVID).Select(x => x.TRANSDATE).FirstOrDefault(),
                        Description = g.OrderBy(x => x.JVID).Select(x => x.NARATION).FirstOrDefault(),
                        MemberNo = g.OrderBy(x => x.JVID).Select(x => x.MEMBERNO).FirstOrDefault(),
                        LoanNo = g.OrderBy(x => x.JVID).Select(x => x.Loanno).FirstOrDefault(),
                        TotalDebit = g.Where(x => x.TRANSTYPE == "DR").Sum(x => x.AMOUNT ?? 0),
                        TotalCredit = g.Where(x => x.TRANSTYPE == "CR").Sum(x => x.AMOUNT ?? 0),
                        EntryCount = g.Count(),
                        Posted = g.OrderBy(x => x.JVID).Select(x => x.POSTED).FirstOrDefault(),
                        PostedDate = g.OrderBy(x => x.JVID).Select(x => x.POSTEDDATE).FirstOrDefault(),
                        CreatedBy = g.OrderBy(x => x.JVID).Select(x => x.AUDITID).FirstOrDefault(),
                        CreatedDate = g.OrderBy(x => x.JVID).Select(x => x.AUDITDATE).FirstOrDefault()
                    });

                if (fromDate.HasValue)
                    query = query.Where(x => x.VoucherDate >= fromDate);

                if (toDate.HasValue)
                    query = query.Where(x => x.VoucherDate <= toDate);

                if (!string.IsNullOrEmpty(memberNo))
                    query = query.Where(x => x.MemberNo == memberNo);

                if (!string.IsNullOrEmpty(loanNo))
                    query = query.Where(x => x.LoanNo == loanNo);

                var vouchers = await query
                    .OrderByDescending(x => x.VoucherDate)
                    .ThenByDescending(x => x.VoucherNo)
                    .ToListAsync();

                ViewBag.CompanyCode = User.FindFirst("CompanyCode")?.Value ?? "001";
                ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "Main SACCO";

                return View(vouchers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading journal list");
                TempData["Error"] = $"Error loading journals: {ex.Message}";
                return View(new List<dynamic>());
            }
        }

        // ===============================
        // GET: /Journal/Edit
        // ===============================
        [HttpGet("Edit")]
        [HttpGet("Edit/{voucherNo}")]
        public async Task<IActionResult> Edit(string voucherNo)
        {
            if (string.IsNullOrEmpty(voucherNo))
            {
                return NotFound("Voucher number is required.");
            }

            try
            {
                var journals = await _context.Journals
                    .Where(x => x.VNO == voucherNo)
                    .OrderBy(x => x.TRANSTYPE)
                    .ToListAsync();

                if (!journals.Any())
                {
                    return NotFound($"Journal with voucher number {voucherNo} not found.");
                }

                var firstJournal = journals.First();

                if (firstJournal.POSTED)
                {
                    TempData["Info"] = "This journal is already posted and cannot be edited.";
                    return RedirectToAction("View", new { voucherNo });
                }

                var companyCode = User.FindFirst("CompanyCode")?.Value ?? "001";

                ViewBag.Accounts = await _context.GlSetup
                    .Where(x => x.Status == true && x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToListAsync();

                ViewBag.CompanyCode = companyCode;
                ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "Main SACCO";

                var model = new JournalEntryViewModel
                {
                    VoucherNo = voucherNo,
                    VoucherDate = firstJournal.TRANSDATE ?? DateTime.Now,
                    Description = firstJournal.NARATION,
                    MemberNo = firstJournal.MEMBERNO,
                    LoanNo = firstJournal.Loanno,
                    CompanyCode = firstJournal.CompanyCode,
                    TransactionNo = firstJournal.Transactionno,
                    AuditId = firstJournal.AUDITID,
                    AuditDate = firstJournal.AUDITDATE,
                    Posted = firstJournal.POSTED,
                    PostedDate = firstJournal.POSTEDDATE ?? DateTime.MinValue,
                    Details = journals.Select(j => new JournalDetailViewModel
                    {
                        AccountNo = j.ACCNO ?? "",
                        AccountName = j.NAME,
                        Narration = j.NARATION,
                        ShareType = j.SHARETYPE,
                        Debit = j.TRANSTYPE == "DR" ? j.AMOUNT ?? 0 : 0,
                        Credit = j.TRANSTYPE == "CR" ? j.AMOUNT ?? 0 : 0
                    }).ToList()
                };

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error editing journal {voucherNo}");
                TempData["Error"] = $"Error loading journal: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        // ===============================
        // GET: /Journal/View
        // ===============================
        [HttpGet("View")]
        [HttpGet("View/{voucherNo}")]
        public async Task<IActionResult> View(string voucherNo)
        {
            if (string.IsNullOrEmpty(voucherNo))
            {
                return NotFound("Voucher number is required.");
            }

            try
            {
                var journals = await _context.Journals
                    .Where(x => x.VNO == voucherNo)
                    .OrderBy(x => x.TRANSTYPE)
                    .ToListAsync();

                if (!journals.Any())
                {
                    return NotFound($"Journal with voucher number {voucherNo} not found.");
                }

                var companyCode = User.FindFirst("CompanyCode")?.Value ?? "001";

                ViewBag.Accounts = await _context.GlSetup
                    .Where(x => x.Status == true && x.CompanyCode == companyCode)
                    .OrderBy(x => x.AccNo)
                    .ToListAsync();

                ViewBag.CompanyCode = companyCode;
                ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "Main SACCO";

                var firstJournal = journals.First();
                var model = new JournalEntryViewModel
                {
                    VoucherNo = voucherNo,
                    VoucherDate = firstJournal.TRANSDATE ?? DateTime.Now,
                    Description = firstJournal.NARATION,
                    MemberNo = firstJournal.MEMBERNO,
                    LoanNo = firstJournal.Loanno,
                    CompanyCode = firstJournal.CompanyCode,
                    TransactionNo = firstJournal.Transactionno,
                    AuditId = firstJournal.AUDITID,
                    AuditDate = firstJournal.AUDITDATE,
                    Posted = firstJournal.POSTED,
                    PostedDate = firstJournal.POSTEDDATE ?? DateTime.MinValue,
                    Details = journals.Select(j => new JournalDetailViewModel
                    {
                        AccountNo = j.ACCNO ?? "",
                        AccountName = j.NAME,
                        Narration = j.NARATION,
                        ShareType = j.SHARETYPE,
                        Debit = j.TRANSTYPE == "DR" ? j.AMOUNT ?? 0 : 0,
                        Credit = j.TRANSTYPE == "CR" ? j.AMOUNT ?? 0 : 0
                    }).ToList()
                };

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error viewing journal {voucherNo}");
                TempData["Error"] = $"Error loading journal: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        // ===============================
        // POST: /Journal/Delete
        // ===============================
        [HttpPost("Delete")]
        [HttpPost("Delete/{voucherNo}")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(string voucherNo)
        {
            if (string.IsNullOrEmpty(voucherNo))
            {
                return Json(new { success = false, message = "Voucher number is required." });
            }

            try
            {
                var journals = await _context.Journals
                    .Where(x => x.VNO == voucherNo)
                    .ToListAsync();

                var journalListings = await _context.JournalsListings
                    .Where(x => x.VoucherNo == voucherNo)
                    .ToListAsync();

                if (!journals.Any() && !journalListings.Any())
                {
                    return Json(new { success = false, message = "Journal not found." });
                }

                if ((journals.Any() && journals.First().POSTED) ||
                    (journalListings.Any() && journalListings.First().Posted))
                {
                    return Json(new { success = false, message = "Cannot delete posted journal!" });
                }

                if (journals.Any())
                    _context.Journals.RemoveRange(journals);

                if (journalListings.Any())
                    _context.JournalsListings.RemoveRange(journalListings);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Journal deleted successfully. Voucher No: {voucherNo}");
                return Json(new { success = true, message = "Journal deleted successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting journal {voucherNo}");
                return Json(new { success = false, message = $"Error deleting journal: {ex.Message}" });
            }
        }

        // ===============================
        // Helpers
        // ===============================
        private string GenerateTransactionNumber()
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            var random = new Random();
            var randomPart = random.Next(1000, 9999);
            return $"TXN-{date}-{randomPart}";
        }

        private async Task<string> GenerateVoucherNumberAsync()
        {
            string companyCode = User.FindFirst("CompanyCode")?.Value ?? "001";

            var now = DateTime.Now;

            string day = now.Day.ToString("00");      // 14
            string month = now.Month.ToString("00");  // 04
            string year = now.ToString("yy");         // 26

            string prefix = $"JV{day}{month}{year}";

            // Get last sequence for today
            var last = await _context.Journals
                .Where(x => x.VNO!.StartsWith(prefix))
                .OrderByDescending(x => x.VNO)
                .Select(x => x.VNO)
                .FirstOrDefaultAsync();

            int seq = 1;

            if (last != null)
            {
                var parts = last.Split('-');
                if (parts.Length > 1 && int.TryParse(parts.Last(), out int n))
                {
                    seq = n + 1;
                }
            }

            return $"{prefix}-{seq:D3}";
        }
    }

    // ViewModels
    public class OriginalEntryViewModel
    {
        public string AccountNo { get; set; } = string.Empty;
        public string? AccountName { get; set; }
        public string? Narration { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? ShareType { get; set; }
    }
    public class ProcessAndCombineViewModel
    {
    
        public string? VoucherNo { get; set; }

        /// <summary>
        /// The date of the journal entry
        /// </summary>
        public DateTime VoucherDate { get; set; }

        /// <summary>
        /// Description/narration for the journal entry
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Member number associated with the journal entry
        /// </summary>
        public string? MemberNo { get; set; }

        /// <summary>
        /// Loan number associated with the journal entry
        /// </summary>
        public string? LoanNo { get; set; }

        /// <summary>
        /// Company code for multi-company support
        /// </summary>
        public string? CompanyCode { get; set; }

        /// <summary>
        /// List of original journal entries to be combined
        /// </summary>
        public List<OriginalEntryViewModel> OriginalEntries { get; set; } = new();
    }

   
}