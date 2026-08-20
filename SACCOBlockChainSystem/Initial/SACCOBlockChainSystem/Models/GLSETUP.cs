using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SACCOBlockChainSystem.Data;
namespace SACCOBlockChainSystem.Models
{
    [Table("GLSETUP")]
    public class GlSetup
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long GlId { get; set; }

        [StringLength(50)]
        public string? Glcode { get; set; }

        [StringLength(50)]
        public string? Glaccname { get; set; }

        [Required]
        [StringLength(30)]
        public string AccNo { get; set; } = null!;

        [StringLength(50)]
        public string? Glacctype { get; set; }

        [Required]
        [StringLength(50)]
        public string GlAccMainGroup { get; set; } = null!;

        [StringLength(50)]
        public string? Glaccgroup { get; set; }

        [StringLength(50)]
        public string? Normalbal { get; set; }

        [StringLength(50)]
        public string? CompanyCode { get; set; }

        [StringLength(50)]
        public string? Glaccstatus { get; set; }

        [Column(TypeName = "money")]
        public decimal? Bal { get; set; }

        [Column(TypeName = "money")]
        public decimal? CurrCode { get; set; }

        [StringLength(50)]
        public string? AuditOrg { get; set; }

        [StringLength(50)]
        public string? AuditId { get; set; }

        public DateTime? AuditDate { get; set; } = DateTime.Now;

        [StringLength(10)]
        public string? Curr { get; set; }

        [Column(TypeName = "money")]
        public decimal? Actuals { get; set; }

        [Column(TypeName = "money")]
        public decimal? Budgetted { get; set; }

        [Required]
        public DateTime TransDate { get; set; } = DateTime.Now;

        public bool? IsSubLedger { get; set; }

        [StringLength(50)]
        public string? AccCategory { get; set; }

        [StringLength(50)]
        public string? TrialBalance { get; set; }

        public int? GlOrder { get; set; }

        public int? Used { get; set; }

        public int? PrintOrder { get; set; }

        public int? BalanceSheet { get; set; }

        [StringLength(50)]
        public string? GlType { get; set; }

        [Column(TypeName = "money")]
        public decimal? CurrentBal { get; set; }

        [Column(TypeName = "money")]
        public decimal? EoyAmount { get; set; }

        public DateTime? EoyDate { get; set; } = DateTime.Now;

        public bool? Main { get; set; }

        [Required]
        [StringLength(50)]
        public string Type { get; set; } = null!;

        [Required]
        [Column(TypeName = "money")]
        public decimal OpeningBal { get; set; }

        [Required]
        [Column(TypeName = "money")]
        public decimal NewGlOpeningBal { get; set; }

        [Required]
        public DateTime NewGlOpeningBalDate { get; set; }

        [Required]
        [StringLength(50)]
        public string SubType { get; set; } = null!;

        [Required]
        public bool Status { get; set; }

        [Required]
        public bool IsSuspense { get; set; }

        [Required]
        public bool IsREarning { get; set; }

        [StringLength(250)]
        public string? ApiKey { get; set; }

        [StringLength(50)]
        public string? UserName { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
    }
}