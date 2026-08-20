// Models/DTOs/CollateralDTOs.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class CollateralDTO
    {
        public long? Id { get; set; }

        [Required(ErrorMessage = "Collateral Code is required")]
        [StringLength(50)]
        [Display(Name = "Collateral Code")]
        public string ColCode { get; set; } = null!;

        [Required(ErrorMessage = "Collateral Description is required")]
        [StringLength(100)]
        [Display(Name = "Collateral Description")]
        public string Coldescription { get; set; } = null!;

        [Required(ErrorMessage = "Percentage is required")]
        [Range(0, 100, ErrorMessage = "Percentage must be between 0 and 100")]
        [Display(Name = "Percentage (%)")]
        public double Percentage { get; set; }

        [StringLength(50)]
        public string? CompanyCode { get; set; }
    }

    public class CollateralResponseDTO
    {
        public long Id { get; set; }
        public string ColCode { get; set; } = null!;
        public string Coldescription { get; set; } = null!;
        public double Percentage { get; set; }
        public string? CompanyCode { get; set; }
        public string? BlockchainTxId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}