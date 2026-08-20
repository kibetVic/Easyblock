using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class DepositDTO
    {
        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        [Range(100, 1000000)]
        public decimal Amount { get; set; }

        [Required]
        public string PaymentMode { get; set; } = "CASH"; // CASH, MPESA, BANK

        public string? ReceiptNo { get; set; }
        public string? Purpose { get; set; }
        public string? ContactInfo { get; set; }
        public string? ProcessedBy { get; set; }
    }

    public class WithdrawalDTO
    {
        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        [Range(100, 1000000)]
        public decimal Amount { get; set; }

        [Required]
        public string PaymentMode { get; set; } = "CASH";

        public string? ReceiptNo { get; set; }
        public string? Purpose { get; set; }
        public string? ContactInfo { get; set; }
        public string? ProcessedBy { get; set; }
    }

    public class TransactionResponseDTO
    {
        public bool Success { get; set; }
        public string TransactionId { get; set; } = null!;
        public string ReceiptNo { get; set; } = null!;
        public decimal Amount { get; set; }
        public decimal NewBalance { get; set; }
        public string BlockchainTxId { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public string? Message { get; set; }
    }
}