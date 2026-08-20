using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class CompanyDTO
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Company Code is required")]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        [Required(ErrorMessage = "Company Name is required")]
        [StringLength(200)]
        [Display(Name = "Company Name")]
        public string? CompanyName { get; set; }

        [Required(ErrorMessage = "Contact Person is required")]
        [StringLength(100)]
        [Display(Name = "Contact Person")]
        public string? Contactperson { get; set; }

        [Required(ErrorMessage = "Telephone Number is required")]
        [Phone]
        [StringLength(50)]
        [Display(Name = "Telephone")]
        public string? Telephone { get; set; }

        [Required(ErrorMessage = "Email Address is required")]
        [EmailAddress]
        [StringLength(100)]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Postal Address is required")]
        [StringLength(200)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Number of Members is required")]
        [Display(Name = "Number of Members")]
        public int? NoEmployees { get; set; }
        public string? County { get; set; }
        public string? SubCounty { get; set; }
        public string? Ward { get; set; }
        public string? Village { get; set; }
        public string? Cigcode { get; set; }
        public string? CountyCode { get; set; }
        public string? Unitcode { get; set; }
        public string? AccountNo { get; set; }
        public int? NoYears { get; set; }
        public string? Location { get; set; }
        public string? Type { get; set; }
        public decimal? Capital { get; set; }
        public bool Project { get; set; } = true;
        public string? BusinessStatus { get; set; }
    }

    public class CompanyResponseDTO
    {
        public int Id { get; set; }
        public string CompanyCode { get; set; } = null!;
        public string? CompanyName { get; set; }
        public string? Contactperson { get; set; }
        public string? Telephone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public int? NoEmployees { get; set; }
        public string? County { get; set; }
        public string? SubCounty { get; set; }
        public string? Ward { get; set; }
        public string? Village { get; set; }
        public string? Cigcode { get; set; }
        public string? CountyCode { get; set; }
        public string? Unitcode { get; set; }
        public string? AccountNo { get; set; }
        public int? NoYears { get; set; }
        public string? Location { get; set; }
        public string? Type { get; set; }
        public decimal? Capital { get; set; }
        public bool Project { get; set; }
        public string? BusinessStatus { get; set; }
        public string? AuditId { get; set; }
        public DateTime? AuditTime { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? BlockchainTxId { get; set; }
    }
}