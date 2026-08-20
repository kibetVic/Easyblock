using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("COLLOANGUAR")]
    public class ColloanGuar
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ColCode { get; set; } = null!;

        [Required]
        [StringLength(20)]
        public string MemberNo { get; set; } = null!;

        [Required]
        [StringLength(50)]
        public string DocNo { get; set; } = null!;

        [Required]
        [Column(TypeName = "money")]
        public decimal Mktvalue { get; set; }

        [Required]
        [StringLength(50)]
        public string LoanNo { get; set; } = null!;

        [Required]
        [Column(TypeName = "money")]
        public decimal Balance { get; set; }

        [Required]
        [StringLength(50)]
        public string AuditId { get; set; } = null!;

        [StringLength(50)]
        public string? CompanyCode { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }

        // Navigation property to BlockchainTransaction
        [ForeignKey("BlockchainTxId")]
        public virtual BlockchainTransaction? BlockchainTransaction { get; set; }
    }
}