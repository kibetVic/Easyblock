using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class SystemUsers
    {
        [Key]
        public int SystemUserId { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        [Compare("Password", ErrorMessage = "Passwords do not match")]
        public string? ConfirmPassword { get; set; }
        public string? OrganizationCode { get; set; }
        public bool Active { get; set; } = false;
        public string? UserGroup { get; set; }
        public string? Branch { get; set; }

    }

    // ViewModel for Sign Up
    public class SignUpViewModel : SystemUsers
    {
        [Display(Name = "I agree to the terms and conditions")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms and conditions")]
        public bool AcceptTerms { get; set; }
    }
}
