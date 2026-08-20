using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class LoanIssuedPerProductReportViewModel
	{
		public string LoanType { get; set; }
		public string LoanCode { get; set; }
		public int No { get; set; }
		public string MemberNo { get; set; }
		public string LoanNo { get; set; }
		public string Name { get; set; }
		public DateTime? ApplicationDate { get; set; }
		public DateTime? AppraisalDate { get; set; }
		public DateTime? EndorsementDate { get; set; }
		public DateTime? DateIssued { get; set; }
		public int LoanPeriodMonths { get; set; }
		public decimal LoanApplied { get; set; }
		public decimal ApprovedAmount { get; set; }
		public decimal? InterestRate { get; set; }
	}

	public class LoanIssuedPerProductGroupViewModel
	{
		public string ValueChain { get; set; }  // Value chain from Loantype table
		public string LoanType { get; set; }
		public string LoanCode { get; set; }
		public List<LoanIssuedPerProductReportViewModel> Loans { get; set; } = new List<LoanIssuedPerProductReportViewModel>();

		// Group Totals
		public int Count { get; set; }
		public decimal TotalLoanApplied { get; set; }
		public decimal TotalApprovedAmount { get; set; }
	}

	public class ValueChainSummaryViewModel
	{
		public string ValueChain { get; set; }
		public List<LoanIssuedPerProductGroupViewModel> LoanProducts { get; set; } = new List<LoanIssuedPerProductGroupViewModel>();
		public int TotalLoans { get; set; }
		public decimal TotalLoanApplied { get; set; }
		public decimal TotalApprovedAmount { get; set; }
	}

	public class LoanIssuedPerProductIndexViewModel
	{
		public List<LoanIssuedPerProductGroupViewModel> Groups { get; set; } = new List<LoanIssuedPerProductGroupViewModel>();
		public List<ValueChainSummaryViewModel> ValueChainGroups { get; set; } = new List<ValueChainSummaryViewModel>();

		// Overall Totals
		public int TotalLoans { get; set; }
		public decimal TotalLoanApplied { get; set; }
		public decimal TotalApprovedAmount { get; set; }
		public int TotalLoanTypes { get; set; }
		public int TotalValueChains { get; set; }

		// Report Parameters
		public DateTime StartDate { get; set; }
		public DateTime EndDate { get; set; }
		public bool HasData { get; set; }
		public string CompanyName { get; set; }
		public string PrintedBy { get; set; }
		public DateTime GeneratedOn { get; set; }
	}
}