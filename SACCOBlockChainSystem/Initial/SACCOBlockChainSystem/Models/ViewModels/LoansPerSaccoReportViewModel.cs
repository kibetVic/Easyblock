using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class LoansPerSaccoReportViewModel
	{
		// Loan Information
		public string MemberNo { get; set; }
		public string LoanNo { get; set; }
		public string FullName { get; set; }
		public string GigCode { get; set; }
		public string LoanCode { get; set; }
		public DateTime? ApplicDate { get; set; }
		public int? RepayPeriod { get; set; }
		public decimal LoanAmt { get; set; }
		public decimal Balance { get; set; }
		public decimal PrincipalPaid { get; set; }
		public decimal InterestPaid { get; set; }
		public decimal TotalPaid { get; set; }
		public DateTime? LastPaymentDate { get; set; }
		public string LoanStatus { get; set; }
	}

	public class LoansPerSaccoIndexViewModel
	{
		// Completed Loans (Zero Balance)
		public List<LoansPerSaccoReportViewModel> CompletedLoans { get; set; } = new List<LoansPerSaccoReportViewModel>();

		// Incomplete Loans (With Balance)
		public List<LoansPerSaccoReportViewModel> IncompleteLoans { get; set; } = new List<LoansPerSaccoReportViewModel>();

		// GIG Groups (for grouping by GIG)
		public List<CIGLoanSummary> GigGroups { get; set; } = new List<CIGLoanSummary>();

		// Statistics
		public int TotalCompletedLoans { get; set; }
		public int TotalIncompleteLoans { get; set; }
		public int TotalLoans { get; set; }

		// Financial Totals
		public decimal TotalCompletedLoanAmount { get; set; }
		public decimal TotalIncompleteLoanAmount { get; set; }
		public decimal TotalOutstandingBalance { get; set; }
		public decimal TotalLoanAmount { get; set; }

		// Report Information
		public DateTime ReportDate { get; set; }
		public DateTime StartDate { get; set; }  // Add this property
		public DateTime EndDate { get; set; }    // Add this property
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}

	public class CIGLoanSummary
	{
		public string GigCode { get; set; }
		public string GigName { get; set; }
		public List<CIGLoanDetail> Loans { get; set; } = new List<CIGLoanDetail>();
		public int CompletedLoanCount { get; set; }
		public int IncompleteLoanCount { get; set; }
		public decimal TotalLoanAmount { get; set; }
		public decimal TotalOutstandingBalance { get; set; }
		public List<CIGLoanDetail> CompletedLoans { get; set; } = new List<CIGLoanDetail>();
		public List<CIGLoanDetail> IncompleteLoans { get; set; } = new List<CIGLoanDetail>();
	}

	public class CIGLoanDetail
	{
		public string LoanNo { get; set; }
		public string MemberNo { get; set; }
		public string MemberName { get; set; }
		public DateTime ApplicationDate { get; set; }
		public decimal LoanAmount { get; set; }
		public decimal OutstandingBalance { get; set; }
		public string Status { get; set; }
	}

	public class LoanExportViewModel
	{
		public string MemberNo { get; set; }
		public string LoanNo { get; set; }
		public string FullName { get; set; }
		public string GigCode { get; set; }
		public string GigName { get; set; }
		public string LoanCode { get; set; }
		public DateTime? ApplicDate { get; set; }
		public int? RepayPeriod { get; set; }
		public decimal LoanAmt { get; set; }
		public decimal Balance { get; set; }
		public string Status { get; set; }
		public string Sex { get; set; }
		public string PhoneNo { get; set; }
		public string IDNo { get; set; }
	}
}