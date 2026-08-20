// Models/NextOfKeen.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("NextOfKeens")]
    public class NextOfKeen
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Member relationship
        [Required]
        [StringLength(50)]
        [Display(Name = "Member Number")]
        public string? MemberNo { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        // Personal Information
        [Required]
        [StringLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Relationship")]
        public string Relationship { get; set; } = null!; // Spouse, Child, Parent, Sibling, etc.

        // Contact Information
        [Required]
        [StringLength(20)]
        [Phone]
        [Display(Name = "Phone Number")]
        public string PhoneNo { get; set; } = null!;

        [StringLength(100)]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string? Email { get; set; }

        [StringLength(200)]
        [Display(Name = "Physical Address")]
        public string? PhysicalAddress { get; set; }

        // Identification
        [StringLength(20)]
        [Display(Name = "ID Number")]
        public string? IdNumber { get; set; }

        [StringLength(50)]
        [Display(Name = "Passport Number")]
        public string? PassportNumber { get; set; }

        // Employment Information
        [StringLength(200)]
        [Display(Name = "Employer")]
        public string? Employer { get; set; }

        [StringLength(100)]
        [Display(Name = "Occupation")]
        public string? Occupation { get; set; }

        // Percentage Allocation (for inheritance/benefits)
        [Column(TypeName = "decimal(5,2)")]
        [Range(0, 100)]
        [Display(Name = "Benefit Percentage (%)")]
        public decimal? BenefitPercentage { get; set; }

        // Priority/Order (if multiple next of kin)
        [Display(Name = "Priority Order")]
        public int? PriorityOrder { get; set; }

        // Is Primary Next of Kin
        [Display(Name = "Is Primary")]
        public bool IsPrimary { get; set; } = false;

        // Status
        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Active"; // Active, Inactive, Removed

        // Additional Notes
        [StringLength(500)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        // Audit Fields
        [StringLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Modified By")]
        public string? ModifiedBy { get; set; }

        [Display(Name = "Modified At")]
        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        [Display(Name = "Blockchain Transaction ID")]
        public string? BlockchainTxId { get; set; }
        public virtual Member? Member { get; set; }
    }
}