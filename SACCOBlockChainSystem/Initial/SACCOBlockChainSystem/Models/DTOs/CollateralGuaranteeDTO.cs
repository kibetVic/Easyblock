// Models/DTOs/CollateralGuaranteeDTOs.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class CollateralGuaranteeDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        public string ColCode { get; set; } = null!;

        [Required]
        [StringLength(50)]
        public string DocNo { get; set; } = null!; 

        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal MarketValue { get; set; }

        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal GuaranteeAmount { get; set; } // Amount being used for guarantee

        [StringLength(500)]
        public string? Remarks { get; set; }

        public string CompanyCode { get; set; } = null!;
        public string? CreatedBy { get; set; }
    }

    // DTO for available collateral items
    public class AvailableCollateralDTO
    {
        public string ColCode { get; set; } = null!;
        public string Coldescription { get; set; } = null!;
        public double Percentage { get; set; } // What % of value can be used
        public decimal MaxGuaranteeAmount { get; set; } // MarketValue * (Percentage/100)
        public bool IsAvailable { get; set; }
        public string? ReasonNotAvailable { get; set; }
        public decimal ExistingGuaranteeBalance { get; set; }
        public decimal OriginalMarketValue { get; set; }
    }

    // Response DTO for assigned collateral guarantee
    public class CollateralGuaranteeResponseDTO
    {
        public long Id { get; set; }
        public string ColCode { get; set; } = null!;
        public string Coldescription { get; set; } = null!;
        public string DocNo { get; set; } = null!;
        public decimal MarketValue { get; set; }
        public decimal GuaranteeAmount { get; set; }
        public decimal RemainingBalance { get; set; }
        public DateTime AssignedDate { get; set; }
        public string? BlockchainTxId { get; set; }
        public string Status { get; set; } = "Active";  
        public DateTime? ReleasedDate { get; set; }     
        public string? ReleasedReason { get; set; }     
        public string? ReleasedBy { get; set; }         
    }

    // DTO for getting member's collaterals
    public class MemberCollateralDTO
    {
        public long Id { get; set; }
        public string ColCode { get; set; } = null!;
        public string Coldescription { get; set; } = null!;
        public string DocNo { get; set; } = null!;
        public decimal MarketValue { get; set; }
        public double Percentage { get; set; }
        public decimal MaxGuaranteeAmount { get; set; }
        public decimal CurrentlyUsedAmount { get; set; }
        public decimal AvailableAmount { get; set; }
        public bool IsActive { get; set; }
        public string? LoanNoGuaranteeing { get; set; }
    }
}