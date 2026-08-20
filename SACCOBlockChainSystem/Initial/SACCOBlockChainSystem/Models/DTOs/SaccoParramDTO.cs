// Models/DTOs/SaccoParramDTO.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class SaccoParramDTO
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "SACCO Name")]
        public string? SaccoName { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        [Display(Name = "Number of Employees")]
        public int? NoOfEmployees { get; set; }

        [StringLength(200)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [StringLength(100)]
        [Display(Name = "Town/City")]
        public string? Town { get; set; }

        [Phone]
        [StringLength(20)]
        [Display(Name = "Telephone")]
        public string? Telephone { get; set; }

        [StringLength(20)]
        [Display(Name = "Fax")]
        public string? Fax { get; set; }

        [EmailAddress]
        [StringLength(100)]
        [Display(Name = "Email Address")]
        public string? EmailAddress { get; set; }

        [Url]
        [StringLength(100)]
        [Display(Name = "Website")]
        public string? Website { get; set; }

        [StringLength(200)]
        [Display(Name = "Physical Address")]
        public string? PhysicalAddress { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Check-off Date")]
        public DateTime? CheckOffDate { get; set; }

        [Required]
        [Range(0, 60)]
        [Display(Name = "Membership Maturity (Months)")]
        public int MembershipMaturityMonths { get; set; } = 6;

        [Required]
        [Range(0, 180)]
        [Display(Name = "Withdrawal Notice (Days)")]
        public int WithdrawalNoticeDays { get; set; } = 30;

        [Required]
        [Range(0, 90)]
        [Display(Name = "Dividend Processing (Days)")]
        public int DividendProcessingDays { get; set; } = 14;

        [Required]
        [Range(1, 10)]
        [Display(Name = "Maximum Guarantors")]
        public int MaxGuarantor { get; set; } = 5;

        [Range(1, 10)]
        [Display(Name = "Minimum Guarantors")]
        public int? MinGuarantor { get; set; } = 1;

        [StringLength(10)]
        [Display(Name = "Default Currency")]
        public string? DefaultCurrency { get; set; } = "KES";

        [Range(0, 2)]
        [Display(Name = "Default Rounding")]
        public int? DefaultRounding { get; set; } = 2;

        [DataType(DataType.Currency)]
        [Display(Name = "Significant Loan Balance")]
        public decimal? SignificantLoanBalance { get; set; }

        [StringLength(50)]
        [Display(Name = "Action on Defaulted Interest")]
        public string? ActionOnDefaultedInterest { get; set; }

        [Display(Name = "Suspense GL Account")]
        public string? Suspense { get; set; }

        [Display(Name = "Retained Earnings GL Account")]
        public string? RetainedEarnings { get; set; }

        [Display(Name = "Creditors GL Account")]
        public string? Creditors { get; set; }
    }

    public class SaccoParramListDTO
    {
        public int Id { get; set; }
        public string? SaccoName { get; set; }
        public string? CompanyCode { get; set; }
        public string? Telephone { get; set; }
        public string? EmailAddress { get; set; }
        public int MembershipMaturityMonths { get; set; }
        public int WithdrawalNoticeDays { get; set; }
        public int MaxGuarantor { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? Suspense { get; set; }
        public string? RetainedEarnings { get; set; }
        public string? Creditors { get; set; }
    }
}