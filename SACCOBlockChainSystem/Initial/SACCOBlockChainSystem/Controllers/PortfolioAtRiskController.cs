using System;
using System.IO;
using System.Linq;
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
    [Route("PortfolioAtRisk")]
    public class PortfolioAtRiskController : Controller
    {
        private readonly IPortfolioAtRiskService _service;

        public PortfolioAtRiskController(IPortfolioAtRiskService service)
        {
            _service = service;
            ExcelPackage.License.SetNonCommercialPersonal("Amtech Technologies");
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var filter = new PortfolioAtRiskFilter();
            var vm = await _service.BuildReportAsync(filter);
            vm.Filter = filter;
            return View("~/Views/Reports/PortfolioAtRisk.cshtml", vm);
        }

        [HttpPost("")]
        public async Task<IActionResult> Index([FromForm] PortfolioAtRiskFilter filter)
        {
            var vm = await _service.BuildReportAsync(filter);
            vm.Filter = filter;
            return View("~/Views/Reports/PortfolioAtRisk.cshtml", vm);
        }

        // Export Excel
        [HttpGet("ExportExcel")]
        public async Task<IActionResult> ExportExcel([FromQuery] DateTime? AsAtDate)
        {
            try
            {
                var filter = new PortfolioAtRiskFilter { AsAtDate = AsAtDate };
                var vm = await _service.BuildReportAsync(filter);

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Portfolio at Risk");

                // Headers
                worksheet.Cells[1, 1].Value = "Loan Type";
                worksheet.Cells[1, 2].Value = "Outstanding Principal (KES)";
                worksheet.Cells[1, 3].Value = "Arrears (KES)";
                worksheet.Cells[1, 4].Value = "PAR (%)";

                using (var range = worksheet.Cells[1, 1, 1, 4])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                int row = 2;
                foreach (var item in vm.Records)
                {
                    worksheet.Cells[row, 1].Value = item.LoanTypeName;
                    worksheet.Cells[row, 2].Value = item.OutstandingPrincipal;
                    worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 3].Value = item.Arrears;
                    worksheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 4].Value = item.PAR;
                    worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                    row++;
                }

                // Total row
                worksheet.Cells[row, 1].Value = "TOTAL";
                worksheet.Cells[row, 1].Style.Font.Bold = true;
                worksheet.Cells[row, 2].Value = vm.TotalOutstandingPrincipal;
                worksheet.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 2].Style.Font.Bold = true;
                worksheet.Cells[row, 3].Value = vm.TotalArrears;
                worksheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 3].Style.Font.Bold = true;
                worksheet.Cells[row, 4].Value = vm.OverallPAR;
                worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 4].Style.Font.Bold = true;

                worksheet.Cells.AutoFitColumns();
                var stream = new MemoryStream(package.GetAsByteArray());
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"PortfolioAtRisk_{AsAtDate:yyyyMMdd}.xlsx");
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
                var filter = new PortfolioAtRiskFilter { AsAtDate = AsAtDate };
                var vm = await _service.BuildReportAsync(filter);

                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(1, Unit.Centimetre);
                        page.Header().Text($"Portfolio at Risk (PAR) Report - As At {AsAtDate:dd/MM/yyyy}")
                            .SemiBold().FontSize(16).AlignCenter();

                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Loan Type").Bold();
                                header.Cell().Text("Outstanding Principal").Bold().AlignRight();
                                header.Cell().Text("Arrears").Bold().AlignRight();
                                header.Cell().Text("PAR %").Bold().AlignRight();
                            });

                            foreach (var item in vm.Records)
                            {
                                table.Cell().Text(item.LoanTypeName);
                                table.Cell().Text($"{item.OutstandingPrincipal:N2}").AlignRight();
                                table.Cell().Text($"{item.Arrears:N2}").AlignRight();
                                table.Cell().Text($"{item.PAR:N2}%").AlignRight();
                            }

                            table.Cell().ColumnSpan(3).Text("TOTAL:").Bold().AlignRight();
                            table.Cell().Text($"{vm.TotalOutstandingPrincipal:N2}").Bold().AlignRight();
                            table.Cell().Text($"{vm.TotalArrears:N2}").Bold().AlignRight();
                            table.Cell().Text($"{vm.OverallPAR:N2}%").Bold().AlignRight();
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
                return File(stream, "application/pdf", $"PortfolioAtRisk_{AsAtDate:yyyyMMdd}.pdf");
            }
            catch (Exception)
            {
                return StatusCode(500, "Error exporting PDF.");
            }
        }
    }
}