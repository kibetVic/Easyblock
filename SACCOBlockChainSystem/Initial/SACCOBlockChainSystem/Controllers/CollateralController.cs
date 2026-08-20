// Controllers/CollateralController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class CollateralController : Controller
    {
        private readonly ICollateralService _collateralService;
        private readonly IUserService _userService;

        public CollateralController(ICollateralService collateralService, IUserService userService)
        {
            _collateralService = collateralService;
            _userService = userService;
        }

        private async Task<string> GetCurrentUserCompanyCodeAsync()
        {
            var claim = User.FindFirst("CompanyCode")?.Value ?? User.FindFirst("companyCode")?.Value;
            if (!string.IsNullOrEmpty(claim)) return claim;

            var username = User.Identity?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                if (user != null && !string.IsNullOrEmpty(user.CompanyCode))
                    return user.CompanyCode;
            }
            return string.Empty;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.Identity?.Name ?? "Unknown";
        }

        public async Task<IActionResult> Index()
        {
            var companyCode = await GetCurrentUserCompanyCodeAsync();
            var collaterals = await _collateralService.GetAllAsync(companyCode);
            ViewBag.NewColCode = await _collateralService.GenerateColCodeAsync(companyCode);
            return View(collaterals);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CollateralDTO dto)
        {
            try
            {
                if (dto == null)
                    return Json(new { success = false, message = "Invalid data" });

                dto.CompanyCode = await GetCurrentUserCompanyCodeAsync();
                var result = await _collateralService.CreateAsync(dto, GetCurrentUserId());
                return Json(new { success = true, message = "Collateral created successfully", collateral = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(long id, [FromBody] CollateralDTO dto)
        {
            try
            {
                var result = await _collateralService.UpdateAsync(id, dto, GetCurrentUserId());
                return Json(new { success = true, message = "Collateral updated successfully", collateral = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(long id)
        {
            try
            {
                await _collateralService.DeleteAsync(id, GetCurrentUserId());
                return Json(new { success = true, message = "Collateral deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetails(long id)
        {
            try
            {
                var collateral = await _collateralService.GetByIdAsync(id);
                if (collateral == null)
                    return Json(new { success = false, message = "Collateral not found" });
                return Json(new { success = true, collateral });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GenerateCode()
        {
            try
            {
                var code = await _collateralService.GenerateColCodeAsync(await GetCurrentUserCompanyCodeAsync());
                return Json(new { success = true, colCode = code });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}