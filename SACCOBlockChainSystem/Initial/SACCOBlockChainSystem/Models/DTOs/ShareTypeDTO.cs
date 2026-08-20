// Models/DTOs/ShareTypeDTO.cs
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class ShareTypeCreateDTO
    {
        [Required(ErrorMessage = "Share code is required")]
        [StringLength(20, ErrorMessage = "Share code cannot exceed 20 characters")]
        public string SharesCode { get; set; } = null!;

        [Required(ErrorMessage = "Share type name is required")]
        [StringLength(100, ErrorMessage = "Share type name cannot exceed 100 characters")]
        public string SharesType { get; set; } = null!;

        [Required(ErrorMessage = "Share account is required")]
        [StringLength(50, ErrorMessage = "Share account cannot exceed 50 characters")]
        public string SharesAcc { get; set; } = null!;

        public string? ContraAcc { get; set; }

        [Range(0, 100, ErrorMessage = "Place period must be between 0 and 100")]
        public int? PlacePeriod { get; set; }

        [Range(0, 10, ErrorMessage = "Loan to share ratio must be between 0 and 10")]
        public float? LoanToShareRatio { get; set; }

        public int? Issharecapital { get; set; } = 1;

        [Range(0, 100, ErrorMessage = "Interest rate must be between 0 and 100")]
        public decimal? Interest { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Maximum amount must be positive")]
        public decimal? MaxAmount { get; set; }

        public string? Guarantor { get; set; }

        public bool IsMainShares { get; set; } = true;

        public bool UsedToGuarantee { get; set; } = false;

        public bool UsedToOffset { get; set; } = false;

        public bool Withdrawable { get; set; } = false;

        public bool Loanquaranto { get; set; } = false;

        [Range(1, 10, ErrorMessage = "Priority must be between 1 and 10")]
        public int Priority { get; set; } = 1;

        [Range(0, double.MaxValue, ErrorMessage = "Minimum amount must be positive")]
        public decimal MinAmount { get; set; } = 0;

        // Make Ppacc optional with a default value
        public string? Ppacc { get; set; } = null;

        [Range(0, double.MaxValue, ErrorMessage = "Lower limit must be positive")]
        public decimal LowerLimit { get; set; } = 0;

        [Range(0, 10, ErrorMessage = "Else ratio must be between 0 and 10")]
        public decimal ElseRatio { get; set; } = 0;

        [Required(ErrorMessage = "Created by is required")]
        public string CreatedBy { get; set; } = null!;

        [Required(ErrorMessage = "Company code is required")]
        public string CompanyCode { get; set; } = null!;
    }

    public class ShareTypeUpdateDTO : ShareTypeCreateDTO
    {
        // Inherits all properties from CreateDTO
    }

    public class ShareTypeResponseDTO
    {
        public string SharesCode { get; set; } = null!;
        public string SharesType { get; set; } = null!;
        public string SharesAcc { get; set; } = null!;
        public string? ContraAcc { get; set; }
        public int? PlacePeriod { get; set; }
        public float? LoanToShareRatio { get; set; }
        public int? Issharecapital { get; set; }
        public decimal? Interest { get; set; }
        public decimal? MaxAmount { get; set; }
        public string? Guarantor { get; set; }
        public bool IsMainShares { get; set; }
        public bool UsedToGuarantee { get; set; }
        public bool UsedToOffset { get; set; }
        public bool Withdrawable { get; set; }
        public bool Loanquaranto { get; set; }
        public int Priority { get; set; }
        public decimal MinAmount { get; set; }
        public string Ppacc { get; set; } = null!;
        public decimal LowerLimit { get; set; }
        public decimal ElseRatio { get; set; }
        public string CompanyCode { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? LastUpdated { get; set; }
        public int TotalMembers { get; set; }
        public decimal TotalShares { get; set; }
    }

    public class ShareTypeSimpleDTO
    {
        public string SharesCode { get; set; } = null!;
        public string SharesType { get; set; } = null!;
        public bool IsMainShares { get; set; }
        public decimal MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public bool UsedToGuarantee { get; set; }
        public bool Withdrawable { get; set; }
        public int Priority { get; set; }
    }
}