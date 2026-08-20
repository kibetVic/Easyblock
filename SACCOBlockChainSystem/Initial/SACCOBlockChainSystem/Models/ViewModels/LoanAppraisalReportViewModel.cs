using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class LoanAppraisalReportViewModel
	{
		public string MemberNo { get; set; }
		public string Names { get; set; }
		public string LoanNo { get; set; }
		public string IDNo { get; set; }
		public decimal AmtRecommended { get; set; }
		public DateTime? AppraisDate { get; set; }
		public DateTime? ApplicDate { get; set; }
		public string Status { get; set; }
	}

	public class LoanAppraisalIndexViewModel
	{
		public List<LoanAppraisalReportViewModel> Appraisals { get; set; } = new List<LoanAppraisalReportViewModel>();
		public decimal TotalAmountRecommended { get; set; }
		public int TotalAppraisals { get; set; }
		public int ApprovedCount { get; set; }
		public int DeclinedCount { get; set; }
		public DateTime StartDate { get; set; }
		public DateTime EndDate { get; set; }
		public bool HasData { get; set; }
		public string CompanyName { get; set; }
		public string PrintedBy { get; set; }
		public DateTime GeneratedOn { get; set; }
	}


		public class RejectedLoansReportViewModel
		{
			public string MemberNo { get; set; }
			public string Names { get; set; }
			public string LoanNo { get; set; }
			public decimal AmtRejected { get; set; }
			public DateTime? RejectedDate { get; set; }
			public string Reasons { get; set; }
			public string LoanCode { get; set; }
			public string LoanName { get; set; }
			public string AppraisedBy { get; set; }
		}

		public class RejectedLoansIndexViewModel
		{
			public List<RejectedLoansReportViewModel> RejectedLoans { get; set; } = new List<RejectedLoansReportViewModel>();

			// Totals
			public decimal TotalAmountRejected { get; set; }

			// Statistics
			public int TotalRejectedLoans { get; set; }
			public int UniqueMembers { get; set; }

			// Report Parameters
			public DateTime StartDate { get; set; }
			public DateTime EndDate { get; set; }
			public bool HasData { get; set; }
			public string CompanyName { get; set; }
			public string PrintedBy { get; set; }
			public DateTime GeneratedOn { get; set; }
		}
	
}