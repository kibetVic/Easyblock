// Models/ShareTransfer.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("ShareTransfers")]
    public class ShareTransfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Transfer Reference
        [Required]
        [StringLength(50)]
        [Display(Name = "Transfer Number")]
        public string TransferNo { get; set; } = null!;

        // Transferor (Seller)
        [Required]
        [StringLength(50)]
        [Display(Name = "Transferor Member Number")]
        public string TransferorMemberNo { get; set; } = null!;

        // Transferee (Buyer)
        [Required]
        [StringLength(50)]
        [Display(Name = "Transferee Member Number")]
        public string TransfereeMemberNo { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        // Share Details
        [Required]
        [StringLength(50)]
        [Display(Name = "Shares Code")]
        public string SharesCode { get; set; } = null!;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Number of Shares")]
        public decimal NumberOfShares { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Price Per Share")]
        public decimal PricePerShare { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Transfer Amount")]
        public decimal TotalAmount { get; set; }

        // Transfer Details
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Transfer Date")]
        public DateTime TransferDate { get; set; }

        [StringLength(20)]
        [Display(Name = "Transfer Type")]
        public string TransferType { get; set; } = "Sale"; // "Sale", "Gift", "Inheritance", "Transfer"

        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Completed", "Rejected", "Cancelled"

        // Transferor Details at time of transfer
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Transferor Share Balance Before")]
        public decimal TransferorBalanceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Transferor Share Balance After")]
        public decimal TransferorBalanceAfter { get; set; }

        // Transferee Details at time of transfer
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Transferee Share Balance Before")]
        public decimal TransfereeBalanceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Transferee Share Balance After")]
        public decimal TransfereeBalanceAfter { get; set; }

        // Payment Details (if sale)
        [StringLength(50)]
        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; } // "Cash", "Bank Transfer", "Cheque", "Deduction from Dividends"

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Payment Amount")]
        public decimal? PaymentAmount { get; set; }

        public DateTime? PaymentDate { get; set; }

        [StringLength(100)]
        [Display(Name = "Payment Reference")]
        public string? PaymentReference { get; set; }

        // Fees and Taxes
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Transfer Fee")]
        public decimal TransferFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Stamp Duty")]
        public decimal StampDuty { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Other Charges")]
        public decimal OtherCharges { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Charges")]
        public decimal TotalCharges { get; set; }

        // GL Account Information for fees
        [StringLength(30)]
        [Display(Name = "Fees GL Account")]
        public string? FeesGlAccountNo { get; set; }

        // Approval Workflow
        [StringLength(100)]
        [Display(Name = "Approved By")]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovalDate { get; set; }

        [StringLength(500)]
        [Display(Name = "Approval Comments")]
        public string? ApprovalComments { get; set; }

        // Supporting Documents
        [StringLength(500)]
        [Display(Name = "Transfer Document Path")]
        public string? TransferDocumentPath { get; set; }

        [StringLength(500)]
        [Display(Name = "Remarks")]
        public string? Remarks { get; set; }

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

        // Navigation Properties
        // [ForeignKey("TransferorMemberNo, CompanyCode")]
        public virtual Member? Transferor { get; set; }

        //[ForeignKey("TransfereeMemberNo, CompanyCode")]
        public virtual Member? Transferee { get; set; }

        //[ForeignKey("SharesCode, CompanyCode")]
        public virtual Sharetype? ShareType { get; set; }

        public virtual ICollection<ShareTransferApproval> Approvals { get; set; } = new List<ShareTransferApproval>();
        public virtual ICollection<ShareTransferDocument> Documents { get; set; } = new List<ShareTransferDocument>();
    }

    [Table("ShareTransferApprovals")]
    public class ShareTransferApproval
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int TransferId { get; set; }

        [Required]
        [StringLength(50)]
        public string TransferNo { get; set; } = null!;

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

        [ForeignKey("TransferId")]
        public virtual ShareTransfer? Transfer { get; set; }
    }

    [Table("ShareTransferDocuments")]
    public class ShareTransferDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int TransferId { get; set; }

        [Required]
        [StringLength(200)]
        public string DocumentName { get; set; } = null!;

        [Required]
        [StringLength(500)]
        public string DocumentPath { get; set; } = null!;

        [StringLength(50)]
        public string? DocumentType { get; set; } // "Transfer Form", "ID Copy", "Consent Letter", etc.

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? UploadedBy { get; set; }

        [ForeignKey("TransferId")]
        public virtual ShareTransfer? Transfer { get; set; }
    }
}