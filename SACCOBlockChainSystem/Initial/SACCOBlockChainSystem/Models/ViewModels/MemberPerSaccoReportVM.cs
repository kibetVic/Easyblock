using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class MemberPerSaccoReportVM
	{
		// Member Information
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public string Sex { get; set; }
		public string PhoneNo { get; set; }
		public string IDNo { get; set; }
		public DateTime? ApplicDate { get; set; }
		public DateTime? EffectDate { get; set; }

		// Additional Fields
		public string MembershipType { get; set; }
		public string Station { get; set; }
		public int? Age { get; set; }
		public string Status { get; set; }
		public string SaccoName { get; set; }

		// Statistics
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int YouthCount { get; set; }
	}

	public class MembersPerSaccoIndexViewModel
	{
		public List<MemberPerSaccoReportVM> Members { get; set; } = new List<MemberPerSaccoReportVM>();
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int YouthCount { get; set; }
		public string SaccoName { get; set; }
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
	}
}