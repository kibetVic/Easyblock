// Controllers/LookupController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Services;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class LookupController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;

        public LookupController(ApplicationDbContext context, IUserService userService)
        {
            _context = context;
            _userService = userService;
        }

        // Helper method to get current user's company code from claims or session
        private string GetCurrentUserCompanyCode()
        {
            // Try to get from claims first
            var companyCodeClaim = User.FindFirst("CompanyCode")?.Value 
                                   ?? User.FindFirst("companyCode")?.Value;
            
            if (!string.IsNullOrEmpty(companyCodeClaim))
                return companyCodeClaim;

            // Try to get from session
            var sessionCompanyCode = HttpContext.Session.GetString("CompanyCode");
            if (!string.IsNullOrEmpty(sessionCompanyCode))
                return sessionCompanyCode;

            // Fallback: get username and then fetch from database
            var username = User.Identity.Name;
            if (!string.IsNullOrEmpty(username))
            {
                var user = _userService.GetUserByUsernameAsync(username).GetAwaiter().GetResult();
                if (user != null && !string.IsNullOrEmpty(user.CompanyCode))
                    return user.CompanyCode;
            }

            return null;
        }

        // Helper method to get current user ID
        private string GetCurrentUserId()
        {
            // Try to get from claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("UserId")?.Value;

            if (!string.IsNullOrEmpty(userIdClaim))
                return userIdClaim;

            // Try to get from session
            var sessionUserId = HttpContext.Session.GetString("UserId");
            if (!string.IsNullOrEmpty(sessionUserId))
                return sessionUserId;

            // Fallback: return username
            return User.Identity.Name;
        }

        [HttpGet]
        public async Task<JsonResult> GetMembers(string term)
        {
            var companyCode = GetCurrentUserCompanyCode();
            
            if (string.IsNullOrEmpty(companyCode))
            {
                return Json(new List<object>());
            }

            var members = await _context.Members
                .Where(m => m.CompanyCode == companyCode &&
                            (m.MemberNo.Contains(term) ||
                             (m.Surname + " " + m.OtherNames).Contains(term) ||
                             m.Idno.Contains(term)))
                .Take(20)
                .Select(m => new { 
                    label = $"{m.MemberNo} - {m.Surname} {m.OtherNames}",
                    value = m.MemberNo 
                })
                .ToListAsync();

            return Json(members);
        }

        [HttpGet]
        public async Task<JsonResult> GetMembersByPhone(string term)
        {
            var companyCode = GetCurrentUserCompanyCode();
            
            if (string.IsNullOrEmpty(companyCode))
            {
                return Json(new List<object>());
            }

            var members = await _context.Members
                .Where(m => m.CompanyCode == companyCode &&
                            (m.PhoneNo.Contains(term) || m.MobileNo.Contains(term)))
                .Take(20)
                .Select(m => new { 
                    label = $"{m.MemberNo} - {m.Surname} {m.OtherNames} ({m.PhoneNo ?? m.MobileNo})",
                    value = m.MemberNo 
                })
                .ToListAsync();

            return Json(members);
        }

        [HttpGet]
        public async Task<JsonResult> GetMemberByName(string term)
        {
            var companyCode = GetCurrentUserCompanyCode();
            
            if (string.IsNullOrEmpty(companyCode))
            {
                return Json(new List<object>());
            }

            var members = await _context.Members
                .Where(m => m.CompanyCode == companyCode &&
                            (m.Surname + " " + m.OtherNames).Contains(term))
                .Take(20)
                .Select(m => new { 
                    label = $"{m.MemberNo} - {m.Surname} {m.OtherNames}",
                    value = m.MemberNo,
                    name = m.Surname + " " + m.OtherNames
                })
                .ToListAsync();

            return Json(members);
        }
    }
}