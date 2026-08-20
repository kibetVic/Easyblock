using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("CIGs")] // Common Interest Groups / Community Investment Groups
    public class GIGs
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "GIG Code")]
        public string GigCode { get; set; } = null!;

        [Required]
        [StringLength(200)]
        [Display(Name = "GIG Name")]
        public string GigName { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        [Phone]
        [StringLength(50)]
        [Display(Name = "Contact Phone")]
        public string? ContactPhone { get; set; }

        [EmailAddress]
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
        public int? TotalMembers { get; set; }

        [StringLength(20)]
        [Display(Name = "Status")]
        public string? Status { get; set; } = "Active";

        // Audit Fields
        [StringLength(50)]
        public string? CreatedBy { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string? ModifiedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }

        // Navigation Properties
        [ForeignKey("CompanyCode")]
        public virtual Company? Company { get; set; }
    }
}