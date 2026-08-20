using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class MemberReportViewModel
	{
		// Basic Member Info
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public string IdNo { get; set; }
		public string Sex { get; set; }
		public int? Age { get; set; }
		public string MembershipType { get; set; }
		public DateTime? ApplicDate { get; set; }
		public DateTime? EffectDate { get; set; }

		// Financial Info
		public decimal? ShareCapital { get; set; }
		public decimal? SavingsDeposits { get; set; }
		public decimal? RegFee { get; set; }
		public decimal? LoanBalance { get; set; }

		// Contact Info
		public string PhoneNo { get; set; }
		public string Email { get; set; }
		public string Station { get; set; }
		public string Status { get; set; }
	}

	public class ActiveMembersIndexViewModel
	{
		public List<MemberReportViewModel> Members { get; set; } = new List<MemberReportViewModel>();
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }
		public decimal TotalShareCapital { get; set; }
		public decimal TotalSavingsDeposits { get; set; }
		public decimal TotalRegFee { get; set; }
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}

	public class InactiveMembersIndexViewModel
	{
		public List<MemberReportViewModel> Members { get; set; } = new List<MemberReportViewModel>();
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }
		public decimal TotalShareCapital { get; set; }
		public decimal TotalSavingsDeposits { get; set; }
		public decimal TotalRegFee { get; set; }
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}
}