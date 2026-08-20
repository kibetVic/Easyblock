using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
    [Route("LoanTypePerformance")]
    public class LoanTypePerformanceController : Controller
    {
        private readonly ILoanTypePerformanceService _service;

        public LoanTypePerformanceController(ILoanTypePerformanceService service)
        {
            _service = service;
            ExcelPackage.License.SetNonCommercialPersonal("Amtech Technologies");
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var companyCode = User.FindFirstValue("CompanyCode");
            var filter = new LoanTypePerformanceFilter();
            var vm = await _service.BuildReportAsync(filter, companyCode ?? string.Empty);
            vm.Filter = filter;
            return View("~/Views/Reports/LoanTypePerformance.cshtml", vm);
        }

        [HttpPost("")]
        public async Task<IActionResult> Index([FromForm] LoanTypePerformanceFilter filter)
        {
            var companyCode = User.FindFirstValue("CompanyCode");
            var vm = await _service.BuildReportAsync(filter, companyCode ?? string.Empty);
            vm.Filter = filter;
            return View("~/Views/Reports/LoanTypePerformance.cshtml", vm);
        }

        // Export CSV
        [HttpGet("ExportCsv")]
        public async Task<IActionResult> ExportCsv([FromQuery] DateTime? AsAtDate)
        {
            try
            {
                var companyCode = User.FindFirstValue("CompanyCode");
                var filter = new LoanTypePerformanceFilter { AsAtDate = AsAtDate };
                var vm = await _service.BuildReportAsync(filter, companyCode ?? string.Empty);

                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("Loan Type,Amount Disbursed (KES),Principal Balance (KES),Principal Arrears (KES),PAR (%)");

                foreach (var item in vm.Records)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(item.LoanTypeName),
                        item.TotalDisbursed.ToString("N2"),
                        item.TotalPrincipalBalance.ToString("N2"),
                        item.TotalArrears.ToString("N2"),
                        item.PAR.ToString("N2")
                    ));
                }

                sb.AppendLine(string.Join(",",
                    "GRAND TOTAL",
                    vm.GrandTotalDisbursed.ToString("N2"),
                    vm.GrandTotalPrincipalBalance.ToString("N2"),
                    vm.GrandTotalArrears.ToString("N2"),
                    vm.OverallPAR.ToString("N2")
                ));

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", $"LoanTypePerformance_{AsAtDate:yyyyMMdd}.csv");
            }
            catch (Exception)
            {
                return StatusCode(500, "Error exporting CSV.");
            }
        }

        // Export Excel
        [HttpGet("ExportExcel")]
        public async Task<IActionResult> ExportExcel([FromQuery] DateTime? AsAtDate)
        {
            try
            {
                var companyCode = User.FindFirstValue("CompanyCode");
                var filter = new LoanTypePerformanceFilter { AsAtDate = AsAtDate };
                var vm = await _service.BuildReportAsync(filter, companyCode ?? string.Empty);

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Loan Performance");

                worksheet.Cells[1, 1].Value = "Loan Type";
                worksheet.Cells[1, 2].Value = "Amount Disbursed (KES)";
                worksheet.Cells[1, 3].Value = "Principal Balance (KES)";
                worksheet.Cells[1, 4].Value = "Principal Arrears (KES)";
                worksheet.Cells[1, 5].Value = "PAR (%)";

                using (var range = worksheet.Cells[1, 1, 1, 5])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                int row = 2;
                foreach (var item in vm.Records)
                {
                    worksheet.Cells[row, 1].Value = item.LoanTypeName;
                    worksheet.Cells[row, 2].Value = item.TotalDisbursed;
                    worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 3].Value = item.TotalPrincipalBalance;
                    worksheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 4].Value = item.TotalArrears;
                    worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 5].Value = item.PAR;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    row++;
                }

                worksheet.Cells[row, 1].Value = "GRAND TOTAL";
                worksheet.Cells[row, 1].Style.Font.Bold = true;
                worksheet.Cells[row, 2].Value = vm.GrandTotalDisbursed;
                worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 2].Style.Font.Bold = true;
                worksheet.Cells[row, 3].Value = vm.GrandTotalPrincipalBalance;
                worksheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 3].Style.Font.Bold = true;
                worksheet.Cells[row, 4].Value = vm.GrandTotalArrears;
                worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 4].Style.Font.Bold = true;
                worksheet.Cells[row, 5].Value = vm.OverallPAR;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 5].Style.Font.Bold = true;

                worksheet.Cells.AutoFitColumns();
                var stream = new MemoryStream(package.GetAsByteArray());
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"LoanTypePerformance_{AsAtDate:yyyyMMdd}.xlsx");
            }
            catch (Exception)
            {
                return StatusCode(500, "Error exporting Excel.");
            }
        }

        // Export PDF
        [HttpGet("ExportPdf")]
        public async Task<IActionResult> ExportPdf([FromQuery] DateTime? AsAtDate)
        {
            try
            {
                var companyCode = User.FindFirstValue("CompanyCode");
                var filter = new LoanTypePerformanceFilter { AsAtDate = AsAtDate };
                var vm = await _service.BuildReportAsync(filter, companyCode ?? string.Empty);

                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(1, Unit.Centimetre);
                        page.Header().Text($"Loan Type Performance Report - As At {AsAtDate:dd/MM/yyyy}")
                            .SemiBold().FontSize(16).AlignCenter();

                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Loan Type").Bold();
                                header.Cell().Text("Amount Disbursed").Bold().AlignRight();
                                header.Cell().Text("Principal Balance").Bold().AlignRight();
                                header.Cell().Text("Principal Arrears").Bold().AlignRight();
                                header.Cell().Text("PAR %").Bold().AlignRight();
                            });

                            foreach (var item in vm.Records)
                            {
                                table.Cell().Text(item.LoanTypeName);
                                table.Cell().Text($"{item.TotalDisbursed:N2}").AlignRight();
                                table.Cell().Text($"{item.TotalPrincipalBalance:N2}").AlignRight();
                                table.Cell().Text($"{item.TotalArrears:N2}").AlignRight();
                                table.Cell().Text($"{item.PAR:N2}").AlignRight();
                            }

                            table.Cell().ColumnSpan(4).Text("GRAND TOTAL:").Bold().AlignRight();
                            table.Cell().Text($"{vm.GrandTotalDisbursed:N2}").Bold().AlignRight();
                            table.Cell().Text($"{vm.GrandTotalPrincipalBalance:N2}").Bold().AlignRight();
                            table.Cell().Text($"{vm.GrandTotalArrears:N2}").Bold().AlignRight();
                            table.Cell().Text($"{vm.OverallPAR:N2}").Bold().AlignRight();
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
                return File(stream, "application/pdf", $"LoanTypePerformance_{AsAtDate:yyyyMMdd}.pdf");
            }
            catch (Exception)
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