using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class SharesAndLoansReportViewModel
	{
		// Member Information
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public int? Age { get; set; }
		public string CIGName { get; set; } // Common Investment Group (Station/Province/District)

		// Financial Information - Shares
		public decimal ShareCapital { get; set; }
		public decimal Deposits { get; set; }
		public decimal RegFee { get; set; }
		public decimal Passbook { get; set; }

		// Loan Information
		public decimal TotalLoans { get; set; }
		public int ActiveLoansCount { get; set; }
		public decimal OutstandingBalance { get; set; }

		// Registration Date
		public DateTime? DateRegistered { get; set; }
        public string? Sex { get; set; }
    }

	public class SharesAndLoansIndexViewModel
	{
		public List<SharesAndLoansReportViewModel> Members { get; set; } = new List<SharesAndLoansReportViewModel>();

		// Statistics
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }
		public int YouthCount { get; set; }

		// Financial Totals
		public decimal TotalShareCapital { get; set; }
		public decimal TotalDeposits { get; set; }
		public decimal TotalRegFee { get; set; }
		public decimal TotalPassbook { get; set; }
		public decimal TotalLoans { get; set; }
		public decimal TotalOutstandingBalance { get; set; }

		// CIG/Station Statistics
		public Dictionary<string, int> MembersByCIG { get; set; } = new Dictionary<string, int>();
		public Dictionary<string, decimal> ShareCapitalByCIG { get; set; } = new Dictionary<string, decimal>();

		// Report Information
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}
}