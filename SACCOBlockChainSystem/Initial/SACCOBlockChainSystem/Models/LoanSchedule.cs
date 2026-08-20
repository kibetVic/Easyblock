using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class LoanSchedule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string LoanNo { get; set; } = null!; 

        [Required]
        [StringLength(50)]
        public string CompanyCode { get; set; } = null!;

        public int InstallmentNo { get; set; }

        public DateTime DueDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrincipalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal InterestAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalInstallment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalancePrincipal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceInterest { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidPrincipal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidInterest { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OutstandingPrincipal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OutstandingInterest { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OutstandingTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PenaltyAmount { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Using string to match SQL

        public DateTime? PaidDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MinimumPayment { get; set; }

        public bool IsFlexible { get; set; } = false;

        [StringLength(50)]
        public string? PaymentReference { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }

        public int DaysOverdue { get; set; }

        [ForeignKey(nameof(LoanNo))]
        public virtual Loan? Loan { get; set; }
    }
}