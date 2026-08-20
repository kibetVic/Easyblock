// Models/JournalsListing.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("JournalsListing")]
    public class JournalsListing
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("JLID")]
        public long JlId { get; set; }

        [Column("VNO")]
        [StringLength(50)]
        public string? VoucherNo { get; set; }

        [Column("ACCNO")]
        [StringLength(15)]
        [Display(Name = "Account No")]
        public string? AccountNo { get; set; }

        [Column("NAME")]
        [StringLength(60)]
        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [Column("NARATION")]
        [StringLength(250)]
        [Display(Name = "Narration")]
        public string? Narration { get; set; }

        [Required]
        [Column("MEMBERNO")]
        [StringLength(20)]
        [Display(Name = "Member No")]
        public string MemberNo { get; set; } = string.Empty;

        [Required]
        [Column("SHARETYPE")]
        [StringLength(20)]
        [Display(Name = "Share Type")]
        public string ShareType { get; set; } = string.Empty;

        [Required]
        [Column("Loanno")]
        [StringLength(20)]
        [Display(Name = "Loan No")]
        public string LoanNo { get; set; } = string.Empty;

        [Column("AMOUNT_DR")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount DR")]
        public decimal? AmountDr { get; set; }

        [Column("AMOUNT_CR")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount CR")]
        public decimal? AmountCr { get; set; }

        [Column("AMOUNT")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount")]
        public decimal? Amount { get; set; }

        [Column("TRANSTYPE")]
        [StringLength(3)]
        [Display(Name = "Transaction Type")]
        public string? TransType { get; set; }

        [Column("AUDITID")]
        [StringLength(50)]
        [Display(Name = "Audit ID")]
        public string? AuditId { get; set; }

        [Column("TRANSDATE")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Transaction Date")]
        public DateTime? TransDate { get; set; }

        [Required]
        [Column("AUDITDATE")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Audit Date")]
        public DateTime AuditDate { get; set; }

        [Required]
        [Column("POSTED")]
        [Display(Name = "Posted")]
        public bool Posted { get; set; }

        [Required]
        [Column("POSTEDDATE")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Posted Date")]
        public DateTime PostedDate { get; set; }

        [Column("Transactionno")]
        [StringLength(50)]
        [Display(Name = "Transaction No")]
        public string? TransactionNo { get; set; }

        [Column("CompanyCode")]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        // Navigation Properties
        [ForeignKey("AccountNo")]
        [NotMapped]
        public virtual GlSetup? GlAccount { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
    }
}