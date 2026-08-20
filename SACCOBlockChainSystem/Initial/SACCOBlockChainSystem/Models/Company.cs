using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("Companies")] // Explicit table name
    public partial class Company
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Column("CompanyCode")]
        public string CompanyCode { get; set; } = null!;

        [StringLength(200)]
        [Column("CompanyName")]
        public string? CompanyName { get; set; }

        [StringLength(100)]
        [Column("Contactperson")]
        public string? Contactperson { get; set; }

        [StringLength(50)]
        [Column("Telephone")]
        public string? Telephone { get; set; }

        [StringLength(100)]
        [Column("Email")]
        public string? Email { get; set; }

        [StringLength(200)]
        [Column("Address")]
        public string? Address { get; set; }

        [Column("NoEmployees")]
        public int? NoEmployees { get; set; }

        // Display fields
        [StringLength(100)]
        [Column("County")]
        public string? County { get; set; }

        [StringLength(100)]
        [Column("SubCounty")]
        public string? SubCounty { get; set; }

        [StringLength(100)]
        [Column("Ward")]
        public string? Ward { get; set; }

        [StringLength(100)]
        [Column("Village")]
        public string? Village { get; set; }

        // Optional fields
        [StringLength(50)]
        [Column("Cigcode")]
        public string? Cigcode { get; set; }

        [StringLength(50)]
        [Column("CountyCode")]
        public string? CountyCode { get; set; }

        [StringLength(50)]
        [Column("Unitcode")]
        public string? Unitcode { get; set; }

        [StringLength(50)]
        [Column("AccountNo")]
        public string? AccountNo { get; set; }

        [Column("NoYears")]
        public int? NoYears { get; set; }

        [StringLength(200)]
        [Column("Location")]
        public string? Location { get; set; }

        [StringLength(50)]
        [Column("Type")]
        public string? Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        //[Column("Capital")]
        public decimal? Capital { get; set; }

        [Column("Project")]
        public bool Project { get; set; }

        [StringLength(50)]
        [Column("AuditId")]
        public string? AuditId { get; set; }

        [Column("AuditTime")]
        public DateTime? AuditTime { get; set; }

        [StringLength(255)]
        [Column("BlockchainTxId")]
        public string? BlockchainTxId { get; set; }
    }
}