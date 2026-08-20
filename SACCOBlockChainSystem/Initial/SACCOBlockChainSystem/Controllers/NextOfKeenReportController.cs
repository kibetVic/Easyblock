using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Services;
using SACCOBlockChainSystem.Models.ViewModels;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class NextOfKeenReportController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<NextOfKeenReportController> _logger;

        public NextOfKeenReportController(
            IReportService reportService,
            ICompanyContextService companyContextService,
            ILogger<NextOfKeenReportController> logger)
        {
            _reportService = reportService;
            _companyContextService = companyContextService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var reportData = await _reportService.GetAllMembersNextOfKinReportAsync(companyCode);

                return View(reportData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading all members report");
                TempData["ErrorMessage"] = $"Error loading report: {ex.Message}";
                return RedirectToAction("Index", "NextOfKeen");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var reportData = await _reportService.GetAllMembersNextOfKinReportAsync(companyCode);
                var excelBytes = _reportService.GenerateExcelReport(reportData);

                var fileName = $"AllMembers_NextOfKin_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to Excel");
                return BadRequest($"Error exporting report: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var reportData = await _reportService.GetAllMembersNextOfKinReportAsync(companyCode);
                var pdfBytes = _reportService.GeneratePdfReport(reportData);

                var fileName = $"AllMembers_NextOfKin_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to PDF");
                return BadRequest($"Error exporting report: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Print()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var reportData = await _reportService.GetAllMembersNextOfKinReportAsync(companyCode);

                return View("PrintView", reportData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error printing report");
                TempData["ErrorMessage"] = $"Error printing report: {ex.Message}";
                return RedirectToAction("Index", "NextOfKeen");
            }
        }
    }
}