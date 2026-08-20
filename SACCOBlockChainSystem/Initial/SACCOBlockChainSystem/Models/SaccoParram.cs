using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("SaccoParram")]
    public class SaccoParram
    {
        public int Id { get; set; }
        public string? SaccoName { get; set; }
        public string? CompanyCode { get; set; }
        public int? NoOfEmployees { get; set; }
        public string? Address { get; set; }
        public string? Town { get; set; }
        public string? Telephone { get; set; }
        public string? Fax { get; set; }
        public string? EmailAddress { get; set; }
        public string? Website { get; set; }
        public string? PhysicalAddress { get; set; }
        public DateTime? CheckOffDate { get; set; }
        public int MembershipMaturityMonths { get; set; }
        public int WithdrawalNoticeDays { get; set; }
        public int DividendProcessingDays { get; set; }
        public int MaxGuarantor { get; set; }
        public int? MinGuarantor { get; set; }
        public string? DefaultCurrency { get; set; }
        public int? DefaultRounding { get; set; }
        public decimal? SignificantLoanBalance { get; set; }
        public string? ActionOnDefaultedInterest { get; set; }
        public string? Suspense { get; set; }
        public string? RetainedEarnings { get; set; }
        public string? Creditors { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
    }
}