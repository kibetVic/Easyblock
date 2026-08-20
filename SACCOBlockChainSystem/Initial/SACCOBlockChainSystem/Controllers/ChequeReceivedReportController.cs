using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("ChequeReceivedReport")]
    public class ChequeReceivedReportController : Controller
    {
        private readonly IChequeReceivedReportService _reportService;

        public ChequeReceivedReportController(IChequeReceivedReportService reportService)
        {
            _reportService = reportService;
            ExcelPackage.License.SetNonCommercialPersonal("Amtech Technologies");
        }

        // GET: /ChequeReceivedReport
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var currentCompanyCode = User.FindFirstValue("CompanyCode");
                var filter = new ChequeReceivedReportFilter();
                var vm = await _reportService.BuildReportAsync(filter, currentCompanyCode);
                return View("~/Views/Reports/ChequeReceivedReport.cshtml", vm);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while loading the report.");
            }
        }

        // POST: /ChequeReceivedReport
        [HttpPost("")]
        public async Task<IActionResult> Index([FromForm] ChequeReceivedReportFilter filter)
        {
            try
            {
                if (filter.DateFrom.HasValue && filter.DateTo.HasValue && filter.DateFrom > filter.DateTo)
                {
                    ModelState.AddModelError("", "Start date cannot be later than end date.");
                }

                var currentCompanyCode = User.FindFirstValue("CompanyCode");

                if (!ModelState.IsValid)
                {
                    var emptyVm = await _reportService.BuildReportAsync(new ChequeReceivedReportFilter(), currentCompanyCode);
                    return View("~/Views/Reports/ChequeReceivedReport.cshtml", emptyVm);
                }

                var vm = await _reportService.BuildReportAsync(filter, currentCompanyCode);
                return View("~/Views/Reports/ChequeReceivedReport.cshtml", vm);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while generating the report.");
            }
        }

        // Export CSV
        [HttpGet("ExportCsv")]
        public async Task<IActionResult> ExportCsv([FromQuery] ChequeReceivedReportFilter filter)
        {
            try
            {
                var currentCompanyCode = User.FindFirstValue("CompanyCode");
                var vm = await _reportService.BuildReportAsync(filter, currentCompanyCode);
                var sb = new System.Text.StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("SACCO,Receipt Number,Member Number,Cheque Number,Amount,Date Deposited");

                foreach (var group in vm.Groups)
                {
                    foreach (var rec in group.Records)
                    {
                        sb.AppendLine(string.Join(",",
                            EscapeCsv(group.SaccoName),
                            EscapeCsv(rec.ReceiptNumber),
                            EscapeCsv(rec.MemberNumber),
                            EscapeCsv(rec.ChequeNumber),
                            rec.Amount.ToString("N2"),
                            rec.DateDeposited?.ToString("dd/MM/yyyy")
                        ));
                    }
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", $"ChequeReceivedReport_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error exporting CSV.");
            }
        }

        // Export Excel
        [HttpGet("ExportExcel")]
        public async Task<IActionResult> ExportExcel([FromQuery] ChequeReceivedReportFilter filter)
        {
            try
            {
                var currentCompanyCode = User.FindFirstValue("CompanyCode");
                var vm = await _reportService.BuildReportAsync(filter, currentCompanyCode);

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Cheques Received");

                worksheet.Cells[1, 1].Value = "SACCO";
                worksheet.Cells[1, 2].Value = "Receipt Number";
                worksheet.Cells[1, 3].Value = "Member Number";
                worksheet.Cells[1, 4].Value = "Cheque Number";
                worksheet.Cells[1, 5].Value = "Amount";
                worksheet.Cells[1, 6].Value = "Date Deposited";

                using (var range = worksheet.Cells[1, 1, 1, 6])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                int row = 2;
                foreach (var group in vm.Groups)
                {
                    foreach (var rec in group.Records)
                    {
                        worksheet.Cells[row, 1].Value = group.SaccoName;
                        worksheet.Cells[row, 2].Value = rec.ReceiptNumber;
                        worksheet.Cells[row, 3].Value = rec.MemberNumber;
                        worksheet.Cells[row, 4].Value = rec.ChequeNumber;
                        worksheet.Cells[row, 5].Value = rec.Amount;
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                        worksheet.Cells[row, 6].Value = rec.DateDeposited?.ToString("dd/MM/yyyy");
                        row++;
                    }
                    worksheet.Cells[row, 4].Value = "Subtotal:";
                    worksheet.Cells[row, 4].Style.Font.Bold = true;
                    worksheet.Cells[row, 5].Value = group.Subtotal;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 5].Style.Font.Bold = true;
                    row++;
                }

                worksheet.Cells[row, 4].Value = "GRAND TOTAL:";
                worksheet.Cells[row, 4].Style.Font.Bold = true;
                worksheet.Cells[row, 5].Value = vm.GrandTotal;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 5].Style.Font.Bold = true;

                worksheet.Cells.AutoFitColumns();
                var stream = new MemoryStream(package.GetAsByteArray());
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"ChequeReceivedReport_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error exporting Excel.");
            }
        }

        // Export PDF
        [HttpGet("ExportPdf")]
        public async Task<IActionResult> ExportPdf([FromQuery] ChequeReceivedReportFilter filter)
        {
            try
            {
                var currentCompanyCode = User.FindFirstValue("CompanyCode");
                var vm = await _reportService.BuildReportAsync(filter, currentCompanyCode);

                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(1, Unit.Centimetre);
                        page.Header().Text("Cheques Received Report").SemiBold().FontSize(16).AlignCenter();
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1.5f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("SACCO").Bold();
                                header.Cell().Text("Receipt Number").Bold();
                                header.Cell().Text("Member Number").Bold();
                                header.Cell().Text("Cheque Number").Bold();
                                header.Cell().Text("Amount").Bold().AlignRight();
                                header.Cell().Text("Date Deposited").Bold();
                            });

                            foreach (var group in vm.Groups)
                            {
                                foreach (var rec in group.Records)
                                {
                                    table.Cell().Text(group.SaccoName);
                                    table.Cell().Text(rec.ReceiptNumber ?? "");
                                    table.Cell().Text(rec.MemberNumber ?? "");
                                    table.Cell().Text(rec.ChequeNumber ?? "");
                                    table.Cell().Text($"{rec.Amount:N2}").AlignRight();
                                    table.Cell().Text(rec.DateDeposited?.ToString("dd/MM/yyyy") ?? "");
                                }
                                table.Cell().ColumnSpan(4).Text("Subtotal:").Bold();
                                table.Cell().Text($"{group.Subtotal:N2}").Bold().AlignRight();
                                table.Cell().Text("");
                            }

                            table.Cell().ColumnSpan(4).Text("GRAND TOTAL:").Bold();
                            table.Cell().Text($"{vm.GrandTotal:N2}").Bold().AlignRight();
                            table.Cell().Text("");
                        });
                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.CurrentPageNumber();
                            text.Span(" / ");
                            text.TotalPages();
                        });
                    });
                });

                var stream = new MemoryStream();
                document.GeneratePdf(stream);
                stream.Position = 0;
                return File(stream, "application/pdf", $"ChequeReceivedReport_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error exporting PDF.");
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}