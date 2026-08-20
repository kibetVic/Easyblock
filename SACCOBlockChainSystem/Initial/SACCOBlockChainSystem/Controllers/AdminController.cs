using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> ManageUsers()
        {
            var users = await _context.UserAccounts1
                .OrderBy(u => u.UserName)
                .ToListAsync();

            return View(users);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserGroup(int userId, string userGroup)
        {
            var user = await _context.UserAccounts1.FindAsync(userId);
            if (user != null)
            {
                user.UserGroup = userGroup;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "User group updated successfully." });
            }

            return Json(new { success = false, message = "User not found." });
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int userId)
        {
            var user = await _context.UserAccounts1.FindAsync(userId);
            if (user != null)
            {
                user.ApprovalStatus = "Active";
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "User approved successfully." });
            }

            return Json(new { success = false, message = "User not found." });
        }
    }
}
