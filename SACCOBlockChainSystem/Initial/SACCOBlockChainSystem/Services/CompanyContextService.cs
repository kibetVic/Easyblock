using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace SACCOBlockChainSystem.Services
{
    public interface ICompanyContextService
    {
        string GetCurrentCompanyCode();
        string GetCurrentUserGroup();
        string GetCurrentUserId();
        string GetCurrentUserName();
        bool IsUserInRole(string role);
        bool IsAdmin();
        bool IsTeller();
        bool IsLoanOfficer();
        bool IsMember();
    }

    public class CompanyContextService : ICompanyContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CompanyContextService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetCurrentCompanyCode()
        {
            var companyCode = _httpContextAccessor.HttpContext?.User?.FindFirstValue("CompanyCode");
            return companyCode ?? "001"; // Default company code
        }

        public string GetCurrentUserGroup()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirstValue("UserGroup") ?? "Member";
        }

        public string GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        }

        public string GetCurrentUserName()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        }

        public bool IsUserInRole(string role)
        {
            return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
        }

        public bool IsAdmin()
        {
            return IsUserInRole("Admin") || IsUserInRole("SuperAdmin");
        }

        public bool IsTeller()
        {
            return IsUserInRole("Teller");
        }

        public bool IsLoanOfficer()
        {
            return IsUserInRole("LoanOfficer");
        }

        public bool IsMember()
        {
            return IsUserInRole("Member");
        }
    }
}