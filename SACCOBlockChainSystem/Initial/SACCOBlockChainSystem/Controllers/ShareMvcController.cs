using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize(Policy = "MemberOnly")]
    public class ShareMvcController : Controller
    {
        private readonly IShareService _shareService;

        public ShareMvcController(IShareService shareService)
        {
            _shareService = shareService;
        }

        public IActionResult Purchase()
        {
            return View();
        }

        public async Task<IActionResult> MyShares()
        {
            var memberNo = User.FindFirst("MemberNo")?.Value;
            if (string.IsNullOrEmpty(memberNo))
                return RedirectToAction("Index", "Home");

            var shares = await _shareService.GetMemberSharesAsync(memberNo);
            return View(shares);
        }

        public IActionResult Transfer()
        {
            return View();
        }

        [Authorize(Policy = "AdminOnly")]
        public IActionResult Dividends()
        {
            return View();
        }
    }
}