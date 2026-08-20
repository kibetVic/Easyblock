// Models/ViewModels/ProfileVm.cs
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class ProfileVm
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string UserLoginId { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string MemberNo { get; set; }
        public string Department { get; set; }
        public string SubCounty { get; set; }
        public string Ward { get; set; }
        public string UserGroup { get; set; }
        public string Status { get; set; }
        public DateTime? DateCreated { get; set; }
    }

    public class EditUserVm
    {
        public int UserId { get; set; }

        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
        [Display(Name = "Username")]
        public string? UserName { get; set; }

        [Display(Name = "Login ID")]
        public string? UserLoginId { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Phone(ErrorMessage = "Invalid phone number")]
        [Display(Name = "Phone")]
        public string? Phone { get; set; }

        [Display(Name = "Department")]
        public string? Department { get; set; }

        [Display(Name = "Sub-County")]
        public string? SubCounty { get; set; }

        [Display(Name = "Ward")]
        public string? Ward { get; set; }

        // For dropdown selections (IDs)
        public int? SubCountyId { get; set; }
        public int? WardId { get; set; }

        [Display(Name = "User Group/Role")]
        public string? UserGroup { get; set; }

        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; }

        [Display(Name = "Is Locked")]
        public bool IsLocked { get; set; }

        [Display(Name = "Date Created")]
        public DateTime? DateCreated { get; set; }

        // Available options for dropdowns
        public List<string> AvailableStatuses { get; set; } = new List<string>
        {
            "Active",
            "Inactive",
            "Pending",
            "Locked"
        };
    }
    public class UserDetailsVm
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserLoginId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Department { get; set; }
        public string? SubCounty { get; set; }
        public string? Ward { get; set; }
        public string? UserGroup { get; set; }
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public string? Status { get; set; }
        public string? Userstatus { get; set; }
        public string? ApprovalStatus { get; set; }
        public bool IsLocked { get; set; }
        public int FailedAttempts { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }
        public string? ModifiedBy { get; set; }
        public string? PasswordStatus { get; set; }
        public string? PassExpire { get; set; }
    }
}