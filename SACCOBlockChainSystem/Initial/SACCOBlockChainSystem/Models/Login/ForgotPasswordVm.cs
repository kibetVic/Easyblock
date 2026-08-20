using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class ForgotPasswordVm
    {
        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string? Username { get; set; }
    }

    public class VerifyCodeVm
    {
        [Required(ErrorMessage = "Username is required")]
        public string? Username { get; set; }

        [Required(ErrorMessage = "Verification code is required")]
        [Display(Name = "Verification Code")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits")]
        public string? Code { get; set; }
    }

    public class ResetPasswordVm
    {
        [Required(ErrorMessage = "Username is required")]
        public string? Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long")]
        [Display(Name = "New Password")]
        public string? NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your password")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string? ConfirmPassword { get; set; }
    }
}