using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("Journals")]
    public class Journal
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long JVID { get; set; }

        [StringLength(50)]
        public string? VNO { get; set; }

        [StringLength(15)]
        public string? ACCNO { get; set; }

        [StringLength(60)]
        public string? NAME { get; set; }

        [StringLength(250)]
        public string? NARATION { get; set; }

        [Required]
        [StringLength(20)]
        public string MEMBERNO { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string SHARETYPE { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Loanno { get; set; } = string.Empty;

        [Column(TypeName = "money")]
        public decimal? AMOUNT { get; set; }

        [StringLength(3)]
        public string? TRANSTYPE { get; set; }

        [StringLength(50)]
        public string? AUDITID { get; set; }

        public DateTime? TRANSDATE { get; set; }

        [Required]
        public DateTime AUDITDATE { get; set; }

        [Required]
        public bool POSTED { get; set; }

        [Required]
        public DateTime? POSTEDDATE { get; set; }

        [StringLength(50)]
        public string? Transactionno { get; set; }

        [StringLength(50)]
        public string? CompanyCode { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
    }
}