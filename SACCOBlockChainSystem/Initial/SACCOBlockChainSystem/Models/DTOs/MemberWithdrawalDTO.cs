// Models/DTOs/WithdrawalDTOs.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class MemberWithdrawalDTO
    {
        [Required]
        [Display(Name = "Withdrawal Type")]
        public string WithdrawalType { get; set; } = null!;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Withdrawal Date")]
        public DateTime WithdrawalDate { get; set; } = DateTime.Now;

        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; }

        [Display(Name = "Bank Name")]
        public string? BankName { get; set; }

        [Display(Name = "Bank Account Number")]
        public string? BankAccountNo { get; set; }

        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [Display(Name = "Cheque Number")]
        public string? ChequeNo { get; set; }

        [Phone]
        [Display(Name = "Mobile Number")]
        public string? MobileNo { get; set; }

        [Display(Name = "Remarks")]
        public string? Remarks { get; set; }

        [Display(Name = "Document Path")]
        public string? DocumentPath { get; set; }
        public DateTime WithdrawalNoticeDate { get; internal set; }
    }
    public class WithdrawalCalculationDTO
    {
        public decimal TotalSharesValue { get; set; }
        public decimal TotalDeposits { get; set; }
        public decimal OutstandingLoans { get; set; }
        public decimal PenaltiesAndDeductions { get; set; }
        public decimal NetPayableAmount { get; set; }
        public bool HasOutstandingLoans { get; set; }
        public bool IsEligibleForWithdrawal { get; set; }
        public string? EligibilityMessage { get; set; }
    }

    public class WithdrawalResponseDTO
    {
        public int Id { get; set; }
        public string WithdrawalNo { get; set; } = null!;
        public string MemberNo { get; set; } = null!;
        public string MemberName { get; set; } = null!;
        public DateTime WithdrawalDate { get; set; }
        public string WithdrawalType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public decimal TotalSharesValue { get; set; }
        public decimal OutstandingLoans { get; set; }
        public decimal NetPayableAmount { get; set; }
        public string? PaymentMethod { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? BlockchainTxId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShareTransferDTO
    {
        [Required]
        [Display(Name = "Transferor Member Number")]
        public string TransferorMemberNo { get; set; } = null!;

        [Required]
        [Display(Name = "Transferee Member Number")]
        public string TransfereeMemberNo { get; set; } = null!;

        [Required]
        [Display(Name = "Shares Code")]
        public string SharesCode { get; set; } = null!;

        [Required]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Number of Shares")]
        public decimal NumberOfShares { get; set; }

        [Required]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Price Per Share")]
        public decimal PricePerShare { get; set; }

        [Display(Name = "Transfer Type")]
        public string TransferType { get; set; } = "Sale";

        [DataType(DataType.Date)]
        [Display(Name = "Transfer Date")]
        public DateTime TransferDate { get; set; } = DateTime.Now;

        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; }

        [Display(Name = "Payment Reference")]
        public string? PaymentReference { get; set; }

        [Display(Name = "Transfer Document Path")]
        public string? TransferDocumentPath { get; set; }

        [Display(Name = "Remarks")]
        public string? Remarks { get; set; }
        public string? ProcessedBy { get; set; }
    }

    public class ShareTransferResponseDTO
    {
        public int Id { get; set; }
        public string TransferNo { get; set; } = null!;
        public string TransferorMemberNo { get; set; } = null!;
        public string TransferorName { get; set; } = null!;
        public string TransfereeMemberNo { get; set; } = null!;
        public string TransfereeName { get; set; } = null!;
        public string SharesCode { get; set; } = null!;
        public string SharesType { get; set; } = null!;
        public decimal NumberOfShares { get; set; }
        public decimal PricePerShare { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime TransferDate { get; set; }
        public string TransferType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public decimal TransferFee { get; set; }
        public decimal StampDuty { get; set; }
        public decimal TotalCharges { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? BlockchainTxId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShareTransferCalculationDTO
    {
        public decimal TransferorBalanceBefore { get; set; }
        public decimal TransferorBalanceAfter { get; set; }
        public decimal TransfereeBalanceBefore { get; set; }
        public decimal TransfereeBalanceAfter { get; set; }
        public decimal TransferFee { get; set; }
        public decimal StampDuty { get; set; }
        public decimal TotalCharges { get; set; }
        public bool IsTransferorEligible { get; set; }
        public bool IsTransfereeEligible { get; set; }
        public string? EligibilityMessage { get; set; }
        public decimal OtherCharges { get; set; }
        public decimal TotalAmount { get; set; }
    }
}