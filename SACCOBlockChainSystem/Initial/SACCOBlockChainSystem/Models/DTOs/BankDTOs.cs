// Models/DTOs/BankDTOs.cs
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    // Bank DTOs
    public class BankDTO
    {
        [Required]
        [StringLength(50)]
        [Display(Name = "Bank Code")]
        public string BankCode { get; set; } = null!;

        [Required]
        [StringLength(200)]
        [Display(Name = "Bank Name")]
        public string BankName { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Account Number")]
        public string AccountNumber { get; set; } = null!;

        [StringLength(100)]
        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [StringLength(500)]
        [Display(Name = "Branch")]
        public string? Branch { get; set; }

        [StringLength(50)]
        [Display(Name = "Swift Code")]
        public string? SwiftCode { get; set; }

        [StringLength(50)]
        [Display(Name = "Sort Code")]
        public string? SortCode { get; set; }

        // NEW: GL Account Association
        [StringLength(30)]
        [Display(Name = "GL Account Number")]
        public string? GlAccountNo { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }
    }

    public class BankResponseDTO
    {
        public int Id { get; set; }
        public string BankCode { get; set; } = null!;
        public string BankName { get; set; } = null!;
        public string AccountNumber { get; set; } = null!;
        public string? AccountName { get; set; }
        public string? Branch { get; set; }
        public string? SwiftCode { get; set; }
        public string? SortCode { get; set; }
        public string? GlAccountNo { get; set; }
        public string? GlAccountName { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? BlockchainTxId { get; set; }
    }

    // NEW: DTO for GL Account dropdown
    public class GlAccountDropdownDTO
    {
        public string AccNo { get; set; } = null!;
        public string Glaccname { get; set; } = null!;
        public string? Glacctype { get; set; }
        public string DisplayText => $"{AccNo} - {Glaccname} {(Glacctype != null ? $"({Glacctype})" : "")}";
    }
}