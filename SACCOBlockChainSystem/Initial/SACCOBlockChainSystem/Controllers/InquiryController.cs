// Controllers/InquiryController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class InquiryController : Controller
    {
        private readonly IInquiryService _inquiryService;
        private readonly IUserService _userService;

        public InquiryController(
            IInquiryService inquiryService,
            IUserService userService)
        {
            _inquiryService = inquiryService;
            _userService = userService;
        }

        // Helper method to get current user's company code
        private async Task<string> GetCurrentUserCompanyCodeAsync()
        {
            // Try to get from claims first
            var companyCodeClaim = User.FindFirst("CompanyCode")?.Value
                                   ?? User.FindFirst("companyCode")?.Value;

            if (!string.IsNullOrEmpty(companyCodeClaim))
                return companyCodeClaim;

            // Fallback: get username and then fetch from database using existing service
            var username = User.Identity?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                if (user != null && !string.IsNullOrEmpty(user.CompanyCode))
                    return user.CompanyCode;
            }

            return string.Empty;
        }

        // Helper method to get current user ID
        private string GetCurrentUserId()
        {
            // Try to get from claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("UserId")?.Value;

            if (!string.IsNullOrEmpty(userIdClaim))
                return userIdClaim;

            // Fallback: return username
            return User.Identity?.Name ?? "Unknown";
        }

        // Helper method to get current username
        private string GetCurrentUsername()
        {
            return User.Identity?.Name ?? "Unknown";
        }

        // GET: Inquiry/Index
        public async Task<IActionResult> Index()
        {
            return View();
        }

        // GET: Inquiry/MemberInquiry
        public IActionResult MemberInquiry()
        {
            return View(new MemberSearchDTO());
        }

        // POST: Inquiry/MemberInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MemberInquiry(MemberSearchDTO searchDto)
        {
            if (!ModelState.IsValid)
            {
                return View(searchDto);
            }

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                var result = await _inquiryService.SearchMembersAsync(searchDto, companyCode, userId);
                return View("MemberSearchResults", result);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(searchDto);
            }
        }

        // GET: Inquiry/MemberSearchResults (for pagination)
        public async Task<IActionResult> MemberSearchResults(
            string? memberNo, string? fullName, string? idNo, string? phoneNo,
            string? email, string? department, string? station, short? status,
            DateTime? fromDate, DateTime? toDate, int page = 1)
        {
            var searchDto = new MemberSearchDTO
            {
                MemberNo = memberNo,
                FullName = fullName,
                IdNo = idNo,
                PhoneNo = phoneNo,
                Email = email,
                Department = department,
                Station = station,
                Status = status,
                FromDate = fromDate,
                ToDate = toDate,
                Page = page,
                PageSize = 20
            };

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                var result = await _inquiryService.SearchMembersAsync(searchDto, companyCode, userId);
                return View(result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(MemberInquiry));
            }
        }

        // GET: Inquiry/MemberDetails/{memberNo}
        public async Task<IActionResult> MemberDetails(string memberNo)
        {
            if (string.IsNullOrEmpty(memberNo))
            {
                return RedirectToAction(nameof(MemberInquiry));
            }

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                var result = await _inquiryService.GetMemberInquiryAsync(memberNo, companyCode, userId);
                return View(result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(MemberInquiry));
            }
        }

        // GET: Inquiry/ShareInquiry
        public IActionResult ShareInquiry()
        {
            return View();
        }

        // POST: Inquiry/ShareInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ShareInquiry(string memberNo)
        {
            if (string.IsNullOrEmpty(memberNo))
            {
                TempData["Error"] = "Member Number is required";
                return RedirectToAction(nameof(ShareInquiry));
            }

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                if (string.IsNullOrEmpty(companyCode))
                {
                    TempData["Error"] = "Unable to determine company code";
                    return RedirectToAction(nameof(ShareInquiry));
                }

                var result = await _inquiryService.GetShareInquiryAsync(memberNo, companyCode, userId);

                if (result == null || result.ShareTypeSummaries == null || !result.ShareTypeSummaries.Any())
                {
                    TempData["Error"] = $"No share data found for member {memberNo}";
                    return RedirectToAction(nameof(ShareInquiry));
                }

                return View(result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(ShareInquiry));
            }
        }

        // GET: Inquiry/LoanInquiry
        public IActionResult LoanInquiry()
        {
            return View();
        }

        // POST: Inquiry/LoanInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoanInquiry(string memberNo)
        {
            if (string.IsNullOrEmpty(memberNo))
            {
                TempData["Error"] = "Member Number is required";
                return RedirectToAction(nameof(LoanInquiry));
            }

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                var result = await _inquiryService.GetLoanInquiryAsync(memberNo, companyCode, userId);
                return View("LoanInquiryResult", result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(LoanInquiry));
            }
        }

        // GET: Inquiry/TransactionInquiry
        public IActionResult TransactionInquiry()
        {
            return View();
        }

        // POST: Inquiry/TransactionInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransactionInquiry(string memberNo)
        {
            if (string.IsNullOrEmpty(memberNo))
            {
                TempData["Error"] = "Member Number is required";
                return RedirectToAction(nameof(TransactionInquiry));
            }

            try
            {
                var companyCode = await GetCurrentUserCompanyCodeAsync();
                var userId = GetCurrentUserId();

                var result = await _inquiryService.GetTransactionInquiryAsync(memberNo, companyCode, userId);
                return View("TransactionInquiryResult", result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(TransactionInquiry));
            }
        }
    }
}