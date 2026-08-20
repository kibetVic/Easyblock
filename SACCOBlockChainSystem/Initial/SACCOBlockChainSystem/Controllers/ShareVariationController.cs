// Controllers/ShareVariationController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class ShareVariationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;

        public ShareVariationController(ApplicationDbContext context, IUserService userService)
        {
            _context = context;
            _userService = userService;
        }

        private async Task<string> GetCurrentUserCompanyCodeAsync()
        {
            var companyCodeClaim = User.FindFirst("CompanyCode")?.Value
                                   ?? User.FindFirst("companyCode")?.Value;

            if (!string.IsNullOrEmpty(companyCodeClaim))
                return companyCodeClaim;

            var username = User.Identity?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                if (user != null && !string.IsNullOrEmpty(user.CompanyCode))
                    return user.CompanyCode;
            }

            return string.Empty;
        }

        // GET: ShareVariation/Index
        public async Task<IActionResult> Index()
        {
            var companyCode = await GetCurrentUserCompanyCodeAsync();

            var shareVariations = await _context.Sharetypes
                .Where(s => s.CompanyCode == companyCode)
                .OrderBy(s => s.Priority)
                .Select(s => new ShareVariationDTO
                {
                    SharesCode = s.SharesCode,
                    SharesType = s.SharesType,
                    IsMainShares = s.IsMainShares,
                    UsedToGuarantee = s.UsedToGuarantee,
                    UsedToOffset = s.UsedToOffset,
                    Withdrawable = s.Withdrawable,
                    MinAmount = s.MinAmount,
                    MaxAmount = s.MaxAmount,
                    Priority = s.Priority,
                    Interest = s.Interest,
                    LoanToShareRatio = s.LoanToShareRatio,
                    TotalMembers = _context.Members.Count(m => m.CompanyCode == companyCode),
                    TotalShares = _context.ContribShares
                        .Where(cs => cs.CompanyCode == companyCode && cs.Sharescode == s.SharesCode)
                        .Sum(cs => (cs.ShareCapitalAmount ?? 0) + (cs.DepositsAmount ?? 0))
                })
                .ToListAsync();

            return View(shareVariations);
        }

        // GET: ShareVariation/Details/{sharesCode}
        public async Task<IActionResult> Details(string sharesCode)
        {
            if (string.IsNullOrEmpty(sharesCode))
            {
                return NotFound();
            }

            var companyCode = await GetCurrentUserCompanyCodeAsync();

            var shareVariation = await _context.Sharetypes
                .FirstOrDefaultAsync(s => s.SharesCode == sharesCode && s.CompanyCode == companyCode);

            if (shareVariation == null)
            {
                return NotFound();
            }

            return View(shareVariation);
        }
    }
}