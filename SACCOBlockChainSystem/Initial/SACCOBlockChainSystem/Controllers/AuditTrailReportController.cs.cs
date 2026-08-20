using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using System.Text;

namespace SACCOBlockChainSystem.Controllers
{
    public class AuditTrailReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditTrailReportController> _logger;

        public AuditTrailReportController(
            ApplicationDbContext context,
            ILogger<AuditTrailReportController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var companyCode = GetUserCompanyCode(); // Get current user's company code

            var model = new AuditTrailReportViewModel
            {
                CompanyCode = companyCode, // Add this property to your ViewModel if not exists
                StartDate = DateTime.Now.AddDays(-30),
                EndDate = DateTime.Now,
                UserBlocks = new List<UserBlock>(),
                TotalRecords = 0,
                UniqueUsers = 0,
                ReportGeneratedDate = DateTime.Now
            };
            return View(model);
        }

        private string GetUserCompanyCode()
        {
            // Try to get from claims first
            var companyCode = User.FindFirst("CompanyCode")?.Value;

            if (string.IsNullOrEmpty(companyCode))
            {
                // Try to get from session
                companyCode = HttpContext.Session.GetString("CompanyCode");
            }

            if (string.IsNullOrEmpty(companyCode))
            {
                // Default or throw exception
                companyCode = "001";
                _logger.LogWarning("Company code not found in claims or session, using default: {CompanyCode}", companyCode);
            }

            return companyCode;
        }

        [HttpPost]
        public async Task<IActionResult> GenerateReport(DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var companyCode = GetUserCompanyCode(); // Get company code

                var query = _context.AuditTrails
                    .Where(a => a.CompanyCode == companyCode) // ✅ FILTER BY COMPANY CODE
                    .AsQueryable();

                // Apply date filters
                if (startDate.HasValue)
                    query = query.Where(a => a.AuditTime >= startDate.Value.Date);

                if (endDate.HasValue)
                    query = query.Where(a => a.AuditTime <= endDate.Value.Date.AddDays(1).AddSeconds(-1));

                // Get all records within date range
                var auditRecords = await query
                    .OrderBy(a => a.AuditTime)
                    .ToListAsync();

                if (!auditRecords.Any())
                {
                    var emptyModel = new AuditTrailReportViewModel
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        UserBlocks = new List<UserBlock>(),
                        TotalRecords = 0,
                        UniqueUsers = 0,
                        ReportGeneratedDate = DateTime.Now
                    };
                    return View("Index", emptyModel);
                }

                // Group by SESSION (UserName + IPAddress + HostName + Location)
                // This ensures different sessions for the same user appear separately
                var sessionGroups = auditRecords
                    .Where(a => !string.IsNullOrEmpty(a.UserName))
                    .GroupBy(a => new {
                        a.UserName,
                        IpAddress = string.IsNullOrEmpty(a.IpAddress) ? "Unknown" : a.IpAddress,
                        HostName = string.IsNullOrEmpty(a.HostName) ? "Unknown" : a.HostName,
                        Location = string.IsNullOrEmpty(a.Location) ? GetLocationFromIp(a.IpAddress) : a.Location
                    })
                    .Select(g => new UserBlock
                    {
                        UserName = g.Key.UserName ?? "Unknown",
                        Location = g.Key.Location ?? "Unknown",
                        IpAddress = g.Key.IpAddress ?? "Unknown",
                        HostName = g.Key.HostName ?? "Unknown",
                        Txns = g.OrderBy(a => a.AuditTime).Select(a => new TransactionItem
                        {
                            TransactionDate = a.AuditTime,
                            Amount = ExtractAmountFromAuditRecord(a),
                            AuditTime = a.AuditTime,
                            ActionDescription = a.ActionDescription ?? a.ActionType,
                            TableName = a.TableName ?? "Unknown"
                        }).ToList()
                    })
                    .OrderBy(g => g.UserName)
                    .ToList();

                var model = new AuditTrailReportViewModel
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    UserBlocks = sessionGroups,
                    TotalRecords = auditRecords.Count,
                    UniqueUsers = sessionGroups.Count, // This is actually unique sessions
                    ReportGeneratedDate = DateTime.Now
                };

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating audit trail report");
                TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
                return View("Index", new AuditTrailReportViewModel
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    UserBlocks = new List<UserBlock>(),
                    TotalRecords = 0,
                    UniqueUsers = 0,
                    ReportGeneratedDate = DateTime.Now
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportToCsv(DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var query = _context.AuditTrails
                    .Where(a => a.CompanyCode == companyCode)
                    .AsQueryable();

                // Apply date filters
                if (startDate.HasValue)
                    query = query.Where(a => a.AuditTime >= startDate.Value.Date);

                if (endDate.HasValue)
                    query = query.Where(a => a.AuditTime <= endDate.Value.Date.AddDays(1).AddSeconds(-1));

                var auditRecords = await query
                    .OrderBy(a => a.AuditTime)
                    .ToListAsync();

                // Group by SESSION for CSV export
                var sessionGroups = auditRecords
                    .Where(a => !string.IsNullOrEmpty(a.UserName))
                    .GroupBy(a => new {
                        a.UserName,
                        IpAddress = string.IsNullOrEmpty(a.IpAddress) ? "Unknown" : a.IpAddress,
                        HostName = string.IsNullOrEmpty(a.HostName) ? "Unknown" : a.HostName,
                        Location = string.IsNullOrEmpty(a.Location) ? GetLocationFromIp(a.IpAddress) : a.Location
                    })
                    .Select(g => new
                    {
                        UserName = g.Key.UserName ?? "Unknown",
                        Location = g.Key.Location ?? "Unknown",
                        IpAddress = g.Key.IpAddress ?? "Unknown",
                        HostName = g.Key.HostName ?? "Unknown",
                        Transactions = g.OrderBy(a => a.AuditTime).Select(a => new
                        {
                            TransactionDate = a.AuditTime?.ToString("dd/MM/yyyy"),
                            Amount = ExtractAmountFromAuditRecord(a),
                            AuditTime = a.AuditTime?.ToString("dd/MM/yyyy HH:mm:ss"),
                            Description = a.ActionDescription ?? a.ActionType,
                            TableName = a.TableName ?? "Unknown"
                        }).ToList()
                    })
                    .OrderBy(g => g.UserName)
                    .ToList();

                var csvContent = new StringBuilder();

                // Header
                csvContent.AppendLine("AUDIT TRAIL REPORT - BY USER SESSION");
                csvContent.AppendLine();
                csvContent.AppendLine($"Date Range: {(startDate?.ToString("dd/MM/yyyy") ?? "All")} - {(endDate?.ToString("dd/MM/yyyy") ?? "All")}");
                csvContent.AppendLine($"Total Records: {auditRecords.Count}");
                csvContent.AppendLine($"Unique Sessions: {sessionGroups.Count}");
                csvContent.AppendLine($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                csvContent.AppendLine();

                foreach (var session in sessionGroups)
                {
                    csvContent.AppendLine($"User Name: {session.UserName}");
                    csvContent.AppendLine($"Location: {session.Location}");
                    csvContent.AppendLine($"IP Address: {session.IpAddress}");
                    csvContent.AppendLine($"Host Name: {session.HostName}");
                    csvContent.AppendLine();
                    csvContent.AppendLine("Transaction Date,Amount,Audit Time,Transaction Description,Transtable");

                    foreach (var txn in session.Transactions)
                    {
                        csvContent.AppendLine($"\"{txn.TransactionDate}\",{txn.Amount:N2},\"{txn.AuditTime}\",\"{txn.Description}\",{txn.TableName}");
                    }

                    csvContent.AppendLine();
                    csvContent.AppendLine(new string('-', 80));
                    csvContent.AppendLine();
                }

                var bytes = Encoding.UTF8.GetBytes(csvContent.ToString());
                var fileName = $"AuditTrailReport_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting audit trail to CSV");
                TempData["ErrorMessage"] = $"Error exporting report: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        private decimal ExtractAmountFromAuditRecord(AuditTrail record)
        {
            decimal amount = 0;

            // Try to extract from ExtraData
            if (!string.IsNullOrEmpty(record.ExtraData))
            {
                try
                {
                    // Try to parse as JSON
                    var amountMatch = System.Text.RegularExpressions.Regex.Match(
                        record.ExtraData,
                        @"amount[\""\s]*:[\""\s]*([0-9,]+\.?[0-9]*)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (amountMatch.Success)
                    {
                        decimal.TryParse(amountMatch.Groups[1].Value.Replace(",", ""), out amount);
                    }
                }
                catch { }
            }

            // Try to extract from NewValue
            if (amount == 0 && !string.IsNullOrEmpty(record.NewValue))
            {
                try
                {
                    var amountMatch = System.Text.RegularExpressions.Regex.Match(
                        record.NewValue,
                        @"\b([0-9,]+\.?[0-9]*)\b");
                    if (amountMatch.Success)
                    {
                        decimal.TryParse(amountMatch.Groups[1].Value.Replace(",", ""), out amount);
                    }
                }
                catch { }
            }

            return amount;
        }

        private string GetLocationFromIp(string? ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return "Unknown";

            // Local addresses
            if (ipAddress == "127.0.0.1" || ipAddress == "::1" || ipAddress == "-1" || ipAddress == "Unknown")
                return "Local Host";

            // Private network ranges
            if (ipAddress.StartsWith("192.168.") || ipAddress.StartsWith("10.") || ipAddress.StartsWith("172."))
                return "Local Network";

            // Kenya IP ranges (based on your image showing Nairobi, Kenya)
            if (ipAddress.StartsWith("197.248.209"))
                return "Nairobi, Kenya";

            if (ipAddress.StartsWith("197.248."))
                return "Kenya";

            if (ipAddress.StartsWith("105.") || ipAddress.StartsWith("41.") || ipAddress.StartsWith("154."))
                return "Kenya";

            return "Unknown";
        }
    }
}