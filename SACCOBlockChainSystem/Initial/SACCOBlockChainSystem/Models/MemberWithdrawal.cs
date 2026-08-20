// Models/MemberWithdrawal.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("MemberWithdrawals")]
    public class MemberWithdrawal
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Withdrawal Reference
        [Required]
        [StringLength(50)]
        [Display(Name = "Withdrawal Number")]
        public string WithdrawalNo { get; set; } = null!;

        // Member Information
        [Required]
        [StringLength(50)]
        [Display(Name = "Member Number")]
        public string MemberNo { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        // Withdrawal Details
        [Required]
        [Column(TypeName = "datetime")]
        [Display(Name = "Withdrawal Date")]
        public DateTime WithdrawalDate { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Withdrawal Type")]
        public string WithdrawalType { get; set; } = null!; // "Voluntary", "Retirement", "Death", "Expulsion", "Transfer"

        [Required]
        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Processing", "Completed", "Rejected", "Cancelled"

        // Financial Details
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Shares Value")]
        public decimal TotalSharesValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Deposits")]
        public decimal TotalDeposits { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Outstanding Loans")]
        public decimal OutstandingLoans { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Net Payable Amount")]
        public decimal NetPayableAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Penalties/Deductions")]
        public decimal PenaltiesAndDeductions { get; set; }

        // Payment Details
        [StringLength(50)]
        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; } // "Bank Transfer", "Cheque", "Cash", "Mobile Money"

        [StringLength(100)]
        [Display(Name = "Bank Name")]
        public string? BankName { get; set; }

        [StringLength(50)]
        [Display(Name = "Bank Account Number")]
        public string? BankAccountNo { get; set; }

        [StringLength(100)]
        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [StringLength(50)]
        [Display(Name = "Cheque Number")]
        public string? ChequeNo { get; set; }

        [StringLength(20)]
        [Phone]
        [Display(Name = "Mobile Number")]
        public string? MobileNo { get; set; }

        [StringLength(200)]
        [Display(Name = "Payment Reference")]
        public string? PaymentReference { get; set; }

        public DateTime? PaymentDate { get; set; }

        // Approval Workflow
        [StringLength(100)]
        [Display(Name = "Approved By")]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovalDate { get; set; }

        [StringLength(500)]
        [Display(Name = "Approval Comments")]
        public string? ApprovalComments { get; set; }

        [StringLength(100)]
        [Display(Name = "Processed By")]
        public string? ProcessedBy { get; set; }

        public DateTime? ProcessedDate { get; set; }

        // Supporting Documents
        [StringLength(500)]
        [Display(Name = "Document Path")]
        public string? DocumentPath { get; set; }

        [StringLength(500)]
        [Display(Name = "Reason/Remarks")]
        public string? Remarks { get; set; }

        // GL Account Information
        [StringLength(30)]
        [Display(Name = "GL Account (Source)")]
        public string? GlAccountNo { get; set; }

        [StringLength(200)]
        [Display(Name = "GL Account Name")]
        public string? GlAccountName { get; set; }

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

        [ForeignKey("MemberId")]
        public virtual Member? Member { get; set; }

        public virtual ICollection<WithdrawalApproval> Approvals { get; set; } = new List<WithdrawalApproval>();
        public virtual ICollection<WithdrawalDocument> Documents { get; set; } = new List<WithdrawalDocument>();
    }

    [Table("WithdrawalApprovals")]
    public class WithdrawalApproval
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int WithdrawalId { get; set; }

        [Required]
        [StringLength(50)]
        public string WithdrawalNo { get; set; } = null!;

        [Required]
        [StringLength(50)]
        public string CompanyCode { get; set; } = null!;

        [Required]
        [StringLength(20)]
        public string ApprovalLevel { get; set; } = null!; // "Level1", "Level2", "Final"

        [Required]
        [StringLength(20)]
        public string ApprovalStatus { get; set; } = null!; // "Pending", "Approved", "Rejected"

        [StringLength(100)]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovalDate { get; set; }

        [StringLength(500)]
        public string? Comments { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("WithdrawalId")]
        public virtual MemberWithdrawal? Withdrawal { get; set; }
    }

    [Table("WithdrawalDocuments")]
    public class WithdrawalDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int WithdrawalId { get; set; }

        [Required]
        [StringLength(200)]
        public string DocumentName { get; set; } = null!;

        [Required]
        [StringLength(500)]
        public string DocumentPath { get; set; } = null!;

        [StringLength(50)]
        public string? DocumentType { get; set; } // "ID Copy", "Application Letter", "Bank Confirmation", etc.

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? UploadedBy { get; set; }

        [ForeignKey("WithdrawalId")]
        public virtual MemberWithdrawal? Withdrawal { get; set; }
    }
}