using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class WithdrawnMembersViewModel
	{
		// Member Information
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public DateTime? WithdrawalDate { get; set; }

		// Financial Information - What they had contributed
		public decimal ShareCapital { get; set; }
		public decimal SavingsDeposits { get; set; }
		public decimal RegistrationFee { get; set; }
		public decimal PassbookAmount { get; set; }

		// Total Amount to be refunded/withdrawn
		public decimal TotalAmount { get; set; }

		// Additional Info
		public string IdNo { get; set; }
		public string Sex { get; set; }
		public string PhoneNo { get; set; }
		public DateTime? DateJoined { get; set; }
		public int? MembershipDuration { get; set; } // In months
	}

	public class WithdrawnMembersIndexViewModel
	{
		public List<WithdrawnMembersViewModel> Members { get; set; } = new List<WithdrawnMembersViewModel>();

		// Date Range (optional)
		public DateTime? StartDate { get; set; }
		public DateTime? EndDate { get; set; }

		// Statistics
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }

		// Financial Totals
		public decimal TotalShareCapital { get; set; }
		public decimal TotalSavingsDeposits { get; set; }
		public decimal TotalRegistrationFee { get; set; }
		public decimal TotalPassbookAmount { get; set; }
		public decimal GrandTotalAmount { get; set; }

		// Report Information
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}
}