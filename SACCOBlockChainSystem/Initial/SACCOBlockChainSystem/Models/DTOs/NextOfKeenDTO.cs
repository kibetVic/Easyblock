// Models/DTOs/NextOfKeenDTOs.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class NextOfKeenDTO
    {
        public int Id { get; set; } // For edit scenarios

        [Required(ErrorMessage = "Member number is required")]
        [StringLength(50)]
        [Display(Name = "Member Number")]
        public string? MemberNo { get; set; } // ADD THIS PROPERTY

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = null!;

        [Required(ErrorMessage = "Relationship is required")]
        [StringLength(50)]
        [Display(Name = "Relationship")]
        public string Relationship { get; set; } = null!;

        [Required(ErrorMessage = "Phone number is required")]
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

        [StringLength(20)]
        [Display(Name = "ID Number")]
        public string? IdNumber { get; set; }

        [StringLength(50)]
        [Display(Name = "Passport Number")]
        public string? PassportNumber { get; set; }

        [StringLength(200)]
        [Display(Name = "Employer")]
        public string? Employer { get; set; }

        [StringLength(100)]
        [Display(Name = "Occupation")]
        public string? Occupation { get; set; }

        [Range(0, 100)]
        [Display(Name = "Benefit Percentage (%)")]
        public decimal? BenefitPercentage { get; set; }

        [Display(Name = "Priority Order")]
        public int? PriorityOrder { get; set; }

        [Display(Name = "Is Primary")]
        public bool IsPrimary { get; set; } = false;

        [StringLength(500)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }
    }

    public class NextOfKeenResponseDTO
    {
        public int Id { get; set; }
        public string MemberNo { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string PhoneNo { get; set; } = null!;
        public string? Email { get; set; }
        public string? PhysicalAddress { get; set; }
        public string? IdNumber { get; set; }
        public string? PassportNumber { get; set; }
        public string? Employer { get; set; }
        public string? Occupation { get; set; }
        public decimal? BenefitPercentage { get; set; }
        public int? PriorityOrder { get; set; }
        public bool IsPrimary { get; set; }
        public string Status { get; set; } = null!;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? BlockchainTxId { get; set; }
    }

    public class NextOfKeenListDTO
    {
        public int Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string PhoneNo { get; set; } = null!;
        public bool IsPrimary { get; set; }
        public int? PriorityOrder { get; set; }
        public decimal? BenefitPercentage { get; set; }
        public string Status { get; set; } = null!;
    }
}