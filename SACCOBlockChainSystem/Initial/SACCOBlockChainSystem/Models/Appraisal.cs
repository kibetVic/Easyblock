using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("APPRAISAL")]
    public class Appraisal
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public long Id { get; set; }

        [Column("LoanNo")]
        [StringLength(50)]
        public string LoanNo { get; set; }

        [Column("AppraisDate")]
        public DateTime? AppraisDate { get; set; } 

        [Column("Salary")]
        public decimal? Salary { get; set; }

        [Column("Allowances")]
        public decimal? Allowances { get; set; }

        [Column("RepayMethod")]
        public string RepayMethod { get; set; }

        [Column("CompanyCode")]
        [StringLength(50)]
        public string CompanyCode { get; set; }

        [Column("Co-opShares")]
        public decimal? CoopShares { get; set; }

        [Column("Co-opLoans")]
        public decimal? CoopLoans { get; set; }

        [Column("Shares")]
        public decimal? Shares { get; set; }

        [Column("Loans")]
        public decimal? Loans { get; set; }

        [Column("Deductions")]
        public decimal? Deductions { get; set; }

        [Column("AmtRecommended")]
        public decimal? AmtRecommended { get; set; }

        [Column("TotalDeductions")]
        public decimal TotalDeductions { get; set; }

        [Column("Reason")]
        public string Reason { get; set; }

        [Column("AuditID")]
        [StringLength(50)]
        public string AuditID { get; set; }

        [Column("AuditTime")]
        public DateTime? AuditTime { get; set; }

        [Column("memberno")]
        [StringLength(50)]
        public string MemberNo { get; set; }

        [Column("Repayrate")]
        public decimal? RepayRate { get; set; }

        [Column("Tinterest")]
        public decimal? TInterest { get; set; }

        [Column("NoOfLoans")]
        public int? NoOfLoans { get; set; }

        [Column("LoanGuarantor")]
        public decimal? LoanGuarantor { get; set; }

        [Column("NetMonthsalary")]
        public decimal? NetMonthlySalary { get; set; }

        [Column("SocietyPayment")]
        public decimal? SocietyPayment { get; set; }

        [Column("Interest")]
        public decimal? Interest { get; set; }

        [Column("Principal")]
        public decimal? Principal { get; set; }

        [Column("TotalInterest")]
        public decimal? TotalInterest { get; set; }

        [Column("BankLoan")]
        public decimal? BankLoan { get; set; }

        [Column("Nssf")]
        public decimal? Nssf { get; set; }

        [Column("CopLoanded")]
        public decimal? CopLoanded { get; set; }

        [Column("OtherDed")]
        public decimal? OtherDed { get; set; }

        [Column("officernames")]
        [StringLength(50)]
        public string OfficerNames { get; set; }

        [Column("transactionNo")]
        [StringLength(30)]
        public string TransactionNo { get; set; }

        [Column("ExpectedNetsalary")]
        public decimal ExpectedNetSalary { get; set; }

        [Column("DeductionToGross")]
        public decimal DeductionToGross { get; set; }

        [Column("StatutoryDed")]
        public decimal? StatutoryDed { get; set; }

        [Column("StatutoryDedTogross")]
        public decimal? StatutoryDedToGross { get; set; }

        [Column("TotalDedNewLoanToGross")]
        public decimal TotalDedNewLoanToGross { get; set; }

        [Column("NetSalaryToGross")]
        public decimal NetSalaryToGross { get; set; }

        [Column("TotalLoanToGross")]
        public decimal TotalLoanToGross { get; set; }

        [Column("TotalCoopDedToGross")]
        public decimal TotalCoopDedToGross { get; set; }

        [Column("TotalDedToGrossLessstatutory")]
        public decimal? TotalDedToGrossLessStatutory { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
    }
}
