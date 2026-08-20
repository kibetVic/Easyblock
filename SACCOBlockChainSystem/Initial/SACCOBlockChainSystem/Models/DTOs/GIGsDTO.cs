using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class GIGsDTO
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "GIG Code is required")]
        [StringLength(50)]
        [Display(Name = "GIG Code")]
        public string GigCode { get; set; } = null!;

        [Required(ErrorMessage = "GIG Name is required")]
        [StringLength(200)]
        [Display(Name = "GIG Name")]
        public string? GigName { get; set; }

        // CompanyCode will be taken from logged-in user, not from form
        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        [Phone(ErrorMessage = "Invalid phone number")]
        [StringLength(50)]
        [Display(Name = "Contact Phone")]
        public string? ContactPhone { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        [StringLength(100)]
        [Display(Name = "Contact Email")]
        public string? ContactEmail { get; set; }

        [StringLength(100)]
        [Display(Name = "Chairperson")]
        public string? Chairperson { get; set; }

        [Display(Name = "Registration Date")]
        [DataType(DataType.Date)]
        public DateTime? RegistrationDate { get; set; }

        [Display(Name = "Total Members")]
        [Range(0, int.MaxValue, ErrorMessage = "Total Members must be a positive number")]
        public int? TotalMembers { get; set; }

        [StringLength(20)]
        [Display(Name = "Status")]
        public string? Status { get; set; } = "Active";
    }

    public class GIGsResponseDTO
    {
        public int Id { get; set; }
        public string GigCode { get; set; } = null!;
        public string? GigName { get; set; }
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public string? Chairperson { get; set; }
        public DateTime? RegistrationDate { get; set; }
        public int? TotalMembers { get; set; }
        public string? Status { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? BlockchainTxId { get; set; }
    }
}