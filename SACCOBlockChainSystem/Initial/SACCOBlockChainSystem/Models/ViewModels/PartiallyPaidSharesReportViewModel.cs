using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class PartiallyPaidSharesReportViewModel
	{
		// Member Information
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public string Sex { get; set; }

		// Financial Information - What they have paid
		public decimal ShareCapital { get; set; }
		public decimal SavingsDeposits { get; set; }
		public decimal RegistrationFee { get; set; }

		// Payment Status Flags (used internally, not displayed)
		public bool HasPaidShareCapital { get; set; }
		public bool HasPaidRegistrationFee { get; set; }
		public bool HasSavingsDeposits { get; set; }

		// Which requirements are missing (used internally)
		public bool MissingShareCapital { get; set; }
		public bool MissingRegistrationFee { get; set; }
		public bool MissingSavingsDeposits { get; set; }
	}

	public class PartiallyPaidSharesIndexViewModel
	{
		public List<PartiallyPaidSharesReportViewModel> Members { get; set; } = new List<PartiallyPaidSharesReportViewModel>();

		// Statistics
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }

		// Financial Totals
		public decimal TotalShareCapital { get; set; }
		public decimal TotalSavingsDeposits { get; set; }
		public decimal TotalRegistrationFee { get; set; }

		// Report Information
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }

		// Counts of missing requirements (used internally)
		public int MembersMissingShareCapital { get; set; }
		public int MembersMissingRegistrationFee { get; set; }
		public int MembersMissingSavings { get; set; }
	}
}