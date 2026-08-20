using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class LoanApplicationDisbursementReportViewModel
	{
		public string MemberNo { get; set; }
		public string Name { get; set; }
		public string LoanNo { get; set; }
		public string AgeGroup { get; set; }
		public string LoanType { get; set; }
		public decimal AppliedAmount { get; set; }
		public int Period { get; set; }
		public DateTime ApplyDate { get; set; }
		public decimal? AppraisedAmount { get; set; }
		public DateTime? AppraisedDate { get; set; }
		public string AppraisedBy { get; set; }
		public string LoanStatus { get; set; }
		public decimal? DisbursementAmount { get; set; }
		public DateTime? DisbursementDate { get; set; }
		public decimal LoanBalance { get; set; }
		public int GuarantorCount { get; set; }
	}

	public class LoanApplicationDisbursementIndexViewModel
	{
		public List<LoanApplicationDisbursementReportViewModel> Loans { get; set; } = new List<LoanApplicationDisbursementReportViewModel>();
		public int TotalLoanApplications { get; set; }
		public int ApprovedCount { get; set; }
		public int DisbursedCount { get; set; }
		public decimal ApprovedRate { get; set; }
		public decimal DisbursementRate { get; set; }
		public decimal TotalAppliedAmount { get; set; }
		public decimal TotalAppraisedAmount { get; set; }
		public decimal TotalDisbursedAmount { get; set; }
		public decimal TotalLoanBalance { get; set; }
		public DateTime StartDate { get; set; }
		public DateTime EndDate { get; set; }
		public bool HasData { get; set; }
		public string CompanyName { get; set; }
		public string PrintedBy { get; set; }
		public DateTime GeneratedOn { get; set; }
	}
}