// Models/DTOs/ContributionDTO.cs
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class ContributionDTO
    {
        [Required(ErrorMessage = "Member number is required")]
        public string MemberNo { get; set; } = null!;

        [Required(ErrorMessage = "Transaction date is required")]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Share type is required")]
        public string SharesCode { get; set; } = null!;

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }
        public string? ReceiptNo { get; set; }

        [StringLength(500, ErrorMessage = "Remarks cannot exceed 500 characters")]
        public string? Remarks { get; set; }
        public string? PaymentMethod { get; set; } = "CASH";
        public string? ReferenceNo { get; set; }
        public string CreatedBy { get; set; } = null!;
        public string CompanyCode { get; set; } = null!;
    }

    public class ContributionResponseDTO
    {
        public int Id { get; set; }
        public string MemberNo { get; set; } = null!;
        public string MemberName { get; set; } = null!;
        public DateTime TransactionDate { get; set; }
        public string SharesCode { get; set; } = null!;
        public string ShareTypeName { get; set; } = null!;
        public decimal Amount { get; set; }
        public decimal ShareCapitalAmount { get; set; }
        public decimal DepositsAmount { get; set; }
        public decimal RegFeeAmount { get; set; }
        public decimal Donor { get; set; }
        public decimal LoanAmount { get; set; }
        public decimal PassBookAmount { get; set; }
        public decimal TotalSharesAfter { get; set; }
        public string ReceiptNo { get; set; } = null!;
        public string Remarks { get; set; } = null!;
        public string BlockchainTxId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = null!;
        public string CompanyCode { get; set; } = null!;
    }

    public class ShareTypeDTO
    {
        public string SharesCode { get; set; } = null!;
        public string SharesType { get; set; } = null!;
        public string SharesAcc { get; set; } = null!;
        public bool IsMainShares { get; set; }
        public bool UsedToGuarantee { get; set; }
        public bool Withdrawable { get; set; }
        public decimal MinAmount { get; set; }
        public decimal MaxAmount { get; set; }
        public string CompanyCode { get; set; } = null!;
    }

    public class MemberContributionHistoryDTO
    {
        public string MemberNo { get; set; } = null!;
        public string MemberName { get; set; } = null!;
        public List<ContributionDetailDTO> Contributions { get; set; } = new();
        public decimal TotalContributions { get; set; }
        public decimal CurrentShareBalance { get; set; }
        public string CompanyCode { get; set; } = null!;
    }

    public class ContributionDetailDTO
    {
        public DateTime TransactionDate { get; set; }
        public string SharesCode { get; set; } = null!;
        public string ShareTypeName { get; set; } = null!;
        public decimal Amount { get; set; }
        public string ReceiptNo { get; set; } = null!;
        public string Remarks { get; set; } = null!;
        public string BlockchainTxId { get; set; } = null!;
        public string CreatedBy { get; set; } = null!;
    }
}