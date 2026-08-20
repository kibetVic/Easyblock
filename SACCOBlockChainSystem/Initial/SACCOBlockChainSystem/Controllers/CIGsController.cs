using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    public class CIGsController : Controller
    {
        private readonly IGIGsService _gigsService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CIGsController> _logger;
        private readonly ICompanyContextService _companyContextService;

        public CIGsController(
            IGIGsService gigsService,
            ApplicationDbContext context,
            ILogger<CIGsController> logger,
            ICompanyContextService companyContextService)
        {
            _gigsService = gigsService;
            _context = context;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        public async Task<IActionResult> Index(string search = null)
        {
            try
            {
                var gigs = await _gigsService.GetAllGIGsAsync(search);

                // Generate new GIG code for the form using logged-in user's company code
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var newGigCode = await _gigsService.GenerateGIGCodeAsync(currentCompanyCode);
                ViewBag.NewGigCode = newGigCode;
                ViewBag.CurrentSearch = search;
                ViewBag.CompanyName = _companyContextService.GetCurrentUserGroup();

                return View(gigs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading GIGs Index");
                TempData["ErrorMessage"] = $"Error loading GIGs: {ex.Message}";
                return View(new List<GIGsResponseDTO>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] GIGsDTO gigDto)
        {
            try
            {
                _logger.LogInformation("=== CREATE GIG REQUEST ===");

                if (gigDto == null)
                {
                    _logger.LogWarning("gigDto is null - possible JSON deserialization issue");
                    return Json(new { success = false, message = "Invalid request data. Please check the form and try again." });
                }

                _logger.LogInformation($"Received GIG data: GigName={gigDto.GigName}, GigCode={gigDto.GigCode}");

                // Validate required fields
                if (string.IsNullOrWhiteSpace(gigDto.GigName))
                {
                    return Json(new { success = false, message = "GIG Name is required" });
                }

                // Auto-generate GIG code if not provided
                if (string.IsNullOrEmpty(gigDto.GigCode))
                {
                    var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                    gigDto.GigCode = await _gigsService.GenerateGIGCodeAsync(currentCompanyCode);
                    _logger.LogInformation($"Auto-generated GIG code: {gigDto.GigCode}");
                }

                var result = await _gigsService.CreateGIGAsync(gigDto);
                return Json(new { success = true, message = "GIG created successfully", gig = result });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error creating GIG");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating GIG");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromBody] GIGsDTO gigDto)
        {
            try
            {
                _logger.LogInformation($"=== EDIT GIG REQUEST for ID: {id} ===");

                if (gigDto == null)
                {
                    return Json(new { success = false, message = "Invalid request data" });
                }

                if (string.IsNullOrWhiteSpace(gigDto.GigName))
                {
                    return Json(new { success = false, message = "GIG Name is required" });
                }

                var result = await _gigsService.UpdateGIGAsync(id, gigDto);
                return Json(new { success = true, message = "GIG updated successfully", gig = result });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error updating GIG");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating GIG");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                _logger.LogInformation($"=== DELETE GIG REQUEST for ID: {id} ===");

                var result = await _gigsService.DeleteGIGAsync(id);
                return Json(new { success = true, message = "GIG deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error deleting GIG");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting GIG");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetGIGDetails(int id)
        {
            try
            {
                _logger.LogInformation($"=== GET GIG DETAILS for ID: {id} ===");

                var gig = await _gigsService.GetGIGByIdAsync(id);
                if (gig == null)
                {
                    return Json(new { success = false, message = "GIG not found" });
                }
                return Json(new { success = true, gig });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting GIG details");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GenerateGIGCode()
        {
            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var gigCode = await _gigsService.GenerateGIGCodeAsync(currentCompanyCode);
                return Json(new { success = true, gigCode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating GIG code");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}