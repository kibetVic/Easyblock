using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class PeriodicRegisteredMembersViewModel
	{
		// Member Information
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public string Sex { get; set; }
		public DateTime? RegistrationDate { get; set; }
		public string MobileNo { get; set; }

		// Additional Info (for detailed view)
		public string IdNo { get; set; }
		public string Email { get; set; }
		public string Station { get; set; }
		public string MembershipType { get; set; }
	}

	public class PeriodicRegisteredMembersIndexViewModel
	{
		public List<PeriodicRegisteredMembersViewModel> Members { get; set; } = new List<PeriodicRegisteredMembersViewModel>();

		// Date Range
		public DateTime StartDate { get; set; }
		public DateTime EndDate { get; set; }

		// Statistics
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }

		// Report Information
		public DateTime ReportDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }

		// For display formatting
		public string FormattedStartDate => StartDate.ToString("dd/MM/yyyy");
		public string FormattedEndDate => EndDate.ToString("dd/MM/yyyy");
	}
}