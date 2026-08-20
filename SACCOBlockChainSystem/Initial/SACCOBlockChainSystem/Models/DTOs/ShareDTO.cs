using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class SharePurchaseDTO
    {
        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        [Range(100, 1000000)]
        public decimal Amount { get; set; }

        [Required]
        public string ShareType { get; set; } = "SH01"; // SH01, SH02, etc.

        public string? ReceiptNo { get; set; }
        public string? Remarks { get; set; }
        public string? ProcessedBy { get; set; }
    }

    //public class ShareTransferDTO
    //{
    //    [Required]
    //    public string FromMemberNo { get; set; } = null!;

    //    [Required]
    //    public string ToMemberNo { get; set; } = null!;

    //    [Required]
    //    [Range(100, 1000000)]
    //    public decimal Amount { get; set; }

    //    [Required]
    //    public string ShareType { get; set; } = "SH01";

    //    public string? Reason { get; set; }
    //    public string? ProcessedBy { get; set; }
    //}

    public class DividendDistributionDTO
    {
        [Required]
        [Range(0.1, 50)]
        public decimal DividendRate { get; set; } // Percentage

        public string? ProcessedBy { get; set; }
        public DateTime DistributionDate { get; set; } = DateTime.Now;
    }

    public class SharePurchaseResponseDTO
    {
        public bool Success { get; set; }
        public string ReceiptNo { get; set; } = null!;
        public decimal Amount { get; set; }
        public string ShareType { get; set; } = null!;
        public decimal TotalShares { get; set; }
        public string BlockchainTxId { get; set; } = null!;
        public DateTime PurchaseDate { get; set; }
        public string? Message { get; set; }
    }

    public class DividendDistributionResponseDTO
    {
        public bool Success { get; set; }
        public decimal TotalDividends { get; set; }
        public int MembersProcessed { get; set; }
        public decimal DividendRate { get; set; }
        public DateTime DistributionDate { get; set; }
        public string? Message { get; set; }
    }
}