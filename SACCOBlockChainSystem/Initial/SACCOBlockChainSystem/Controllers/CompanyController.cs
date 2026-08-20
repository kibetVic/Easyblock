using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    public class CompanyController : Controller
    {
        private readonly ICompanyService _companyService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CompanyController> _logger;
        private readonly IUserService _userService; // Add this

        public CompanyController(
            ICompanyService companyService,
            ApplicationDbContext context,
            ILogger<CompanyController> logger,
            IUserService userService) // Add this parameter
        {
            _companyService = companyService;
            _context = context;
            _logger = logger;
            _userService = userService; // Initialize it
        }

        private async Task LoadDropdowns()
        {
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
            var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

            // Load User Groups - all groups for Super Admin, filtered for others
            if (currentUserRole == "Super Admin")
            {
                ViewBag.UserGroups = await _userService.GetUserGroupsAsync();
                ViewBag.Companies = await _companyService.GetAllCompaniesForDropdownAsync();
            }
            else
            {
                // Only show current user's company and relevant roles
                var userCompany = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == currentUserCompanyCode);

                ViewBag.UserGroups = new List<string> { "Member", "Teller", "LoanOfficer", "Auditor", "Staff" };
                ViewBag.Companies = userCompany != null
                    ? new List<object> { new { userCompany.CompanyCode, DisplayText = $"{userCompany.CompanyCode} - {userCompany.CompanyName}" } }
                    : new List<object>();
            }

            // Load other dropdowns (these can remain the same)
            ViewBag.SubCounties = await _context.SubCounties
                .Where(s => s.Status == "Active")
                .OrderBy(s => s.SubCountyName)
                .Select(s => new { s.Id, s.SubCountyName })
                .ToListAsync();

            ViewBag.Wards = await _context.Wards
                .Where(w => w.Status == "Active")
                .OrderBy(w => w.WardName)
                .Select(w => new { w.Id, w.WardName, w.SubCountyId })
                .ToListAsync();
        }

        [Authorize]
        public async Task<IActionResult> Index(string search = null)
        {
            // Get current user's role and company
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
            var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

            List<CompanyResponseDTO> companies;  // Changed from List<Company> to List<CompanyResponseDTO>

            // Super Admin sees ALL companies
            if (currentUserRole == "Super Admin")
            {
                companies = await _companyService.GetAllCompaniesAsync(search);
            }
            else
            {
                // Non-Super Admin sees ONLY their own company
                var userCompany = await _companyService.GetCompanyByCodeAsync(currentUserCompanyCode);

                companies = userCompany != null ? new List<CompanyResponseDTO> { userCompany } : new List<CompanyResponseDTO>();

                // If there's a search term, filter the single company if it matches
                if (!string.IsNullOrEmpty(search) && companies.Any())
                {
                    companies = companies.Where(c =>
                        (c.CompanyName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                        (c.CompanyCode?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                    ).ToList();
                }
            }

            // Generate new company code for the form (only show for Super Admin)
            if (currentUserRole == "Super Admin")
            {
                var newCompanyCode = await _companyService.GenerateCompanyCodeAsync();
                ViewBag.NewCompanyCode = newCompanyCode;
            }
            else
            {
                ViewBag.NewCompanyCode = null; // Non-Super Admins cannot create companies
            }

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentUserRole = currentUserRole;
            ViewBag.CurrentUserCompany = currentUserCompanyCode;

            // Load dropdown data from database
            await LoadDropdowns();

            return View(companies);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Create()
        {
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

            // Only Super Admin can create companies
            if (currentUserRole != "Super Admin")
            {
                TempData["ErrorMessage"] = "You don't have permission to create companies. Only Super Administrators can create companies.";
                return RedirectToAction("Index");
            }

            // Generate a new company code
            var newCompanyCode = await _companyService.GenerateCompanyCodeAsync();
            ViewBag.NewCompanyCode = newCompanyCode;

            // Load dropdown data
            await LoadDropdowns();

            return View(new CompanyDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CompanyDTO companyDto)
        {
            try
            {
                // Debug: Log the raw request
                _logger.LogInformation("=== CREATE COMPANY REQUEST ===");

                // Check if companyDto is null
                if (companyDto == null)
                {
                    _logger.LogWarning("companyDto is null - possible JSON deserialization issue");

                    // Try to read the raw request body
                    string rawBody = "";
                    using (var reader = new StreamReader(Request.Body))
                    {
                        Request.Body.Position = 0;
                        rawBody = await reader.ReadToEndAsync();
                        _logger.LogInformation($"Raw request body: {rawBody}");
                    }

                    return Json(new { success = false, message = "Invalid request data. Please check the form and try again." });
                }

                _logger.LogInformation($"Received company data: CompanyName={companyDto.CompanyName}, Email={companyDto.Email}");

                // Validate required fields
                var validationErrors = new System.Text.StringBuilder();

                if (string.IsNullOrWhiteSpace(companyDto.CompanyName))
                    validationErrors.AppendLine("- Company Name is required");

                if (string.IsNullOrWhiteSpace(companyDto.Contactperson))
                    validationErrors.AppendLine("- Contact Person is required");

                if (string.IsNullOrWhiteSpace(companyDto.Telephone))
                    validationErrors.AppendLine("- Telephone Number is required");

                if (string.IsNullOrWhiteSpace(companyDto.Email))
                    validationErrors.AppendLine("- Email Address is required");

                if (string.IsNullOrWhiteSpace(companyDto.Address))
                    validationErrors.AppendLine("- Postal Address is required");

                if (!companyDto.NoEmployees.HasValue || companyDto.NoEmployees.Value <= 0)
                    validationErrors.AppendLine("- Number of Members is required");

                if (validationErrors.Length > 0)
                {
                    return Json(new { success = false, message = validationErrors.ToString() });
                }

                // Auto-generate company code if not provided
                if (string.IsNullOrEmpty(companyDto.CompanyCode))
                {
                    companyDto.CompanyCode = await _companyService.GenerateCompanyCodeAsync();
                }

                var result = await _companyService.CreateCompanyAsync(companyDto);
                return Json(new { success = true, message = "Company created successfully", company = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating company");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Edit(int id, CompanyDTO model)
        {
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
            var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

            var existingCompany = await _companyService.GetCompanyByIdAsync(id);

            if (existingCompany == null)
            {
                TempData["ErrorMessage"] = "Company not found.";
                return RedirectToAction("Index");
            }

            // Check permission: Super Admin OR user from the same company
            if (currentUserRole != "Super Admin" && existingCompany.CompanyCode != currentUserCompanyCode)
            {
                TempData["ErrorMessage"] = "You don't have permission to edit this company.";
                return RedirectToAction("Index");
            }

            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                return View(model);
            }

            try
            {
                var result = await _companyService.UpdateCompanyAsync(id, model);
                TempData["SuccessMessage"] = $"Company '{result.CompanyName}' updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating company");
                ModelState.AddModelError(string.Empty, ex.Message);
                await LoadDropdowns();
                return View(model);
            }
        }

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Edit(int id, [FromBody] CompanyDTO companyDto)
        //{
        //    try
        //    {
        //        // Validate required fields
        //        var validationErrors = new System.Text.StringBuilder();

        //        if (string.IsNullOrWhiteSpace(companyDto.CompanyName))
        //            validationErrors.AppendLine("- Company Name is required");

        //        if (string.IsNullOrWhiteSpace(companyDto.Contactperson))
        //            validationErrors.AppendLine("- Contact Person is required");

        //        if (string.IsNullOrWhiteSpace(companyDto.Telephone))
        //            validationErrors.AppendLine("- Telephone Number is required");

        //        if (string.IsNullOrWhiteSpace(companyDto.Email))
        //            validationErrors.AppendLine("- Email Address is required");

        //        if (string.IsNullOrWhiteSpace(companyDto.Address))
        //            validationErrors.AppendLine("- Postal Address is required");

        //        if (!companyDto.NoEmployees.HasValue || companyDto.NoEmployees.Value <= 0)
        //            validationErrors.AppendLine("- Number of Members is required");

        //        if (validationErrors.Length > 0)
        //        {
        //            return Json(new { success = false, message = validationErrors.ToString() });
        //        }

        //        var result = await _companyService.UpdateCompanyAsync(id, companyDto);
        //        return Json(new { success = true, message = "Company updated successfully", company = result });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error updating company");
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var result = await _companyService.DeleteCompanyAsync(id);
                return Json(new { success = true, message = "Company deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting company");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCompanyDetails(int id)
        {
            try
            {
                var company = await _companyService.GetCompanyByIdAsync(id);
                if (company == null)
                {
                    return Json(new { success = false, message = "Company not found" });
                }
                return Json(new { success = true, company });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting company details");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GenerateCompanyCode()
        {
            try
            {
                var companyCode = await _companyService.GenerateCompanyCodeAsync();
                return Json(new { success = true, companyCode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating company code");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =====================================================
        // LOCATION API ENDPOINTS
        // =====================================================

        [HttpGet]
        public async Task<IActionResult> GetCounties()
        {
            try
            {
                var counties = await _context.Counties
                    .Where(c => c.Status == "Active")
                    .OrderBy(c => c.CountyName)
                    .Select(c => new { value = c.Id, text = c.CountyName })
                    .ToListAsync();

                return Json(counties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting counties");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSubCounties(int countyId)
        {
            try
            {
                var subCounties = await _context.SubCounties
                    .Where(s => s.CountyId == countyId && s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new { value = s.Id, text = s.SubCountyName })
                    .ToListAsync();

                return Json(subCounties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sub counties");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWards(int subCountyId)
        {
            try
            {
                var wards = await _context.Wards
                    .Where(w => w.SubCountyId == subCountyId && w.Status == "Active")
                    .OrderBy(w => w.WardName)
                    .Select(w => new { value = w.Id, text = w.WardName })
                    .ToListAsync();

                return Json(wards);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting wards");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}