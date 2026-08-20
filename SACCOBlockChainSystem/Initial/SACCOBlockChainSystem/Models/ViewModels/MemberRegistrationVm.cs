// Models/ViewModels/MemberRegistrationVm.cs
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class MemberRegistrationVm
    {
        [Required(ErrorMessage = "Surname is required")]
        [StringLength(50, ErrorMessage = "Surname cannot exceed 50 characters")]
        public string Surname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Other names are required")]
        [StringLength(100, ErrorMessage = "Other names cannot exceed 100 characters")]
        public string OtherNames { get; set; } = string.Empty;

        [Required(ErrorMessage = "ID Number is required")]
        [StringLength(20, ErrorMessage = "ID Number cannot exceed 20 characters")]
        public string Idno { get; set; } = string.Empty;

        public string? Sex { get; set; }

        [DataType(DataType.Date)]
        public DateTime? Dob { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [Phone(ErrorMessage = "Invalid phone number")]
        public string PhoneNo { get; set; } = string.Empty;

        public string? MobileNo { get; set; }

        [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
        public string? PresentAddr { get; set; }

        public string? Employer { get; set; }
        public string? Dept { get; set; }

        [Range(0, 1000000, ErrorMessage = "Initial shares must be between 0 and 1,000,000")]
        public decimal? InitShares { get; set; } = 0;

        [Range(0, 5000, ErrorMessage = "Registration fee must be between 0 and 5,000")]
        public decimal? RegFee { get; set; } = 0;

        public bool Mstatus { get; set; } = true;
    }
}