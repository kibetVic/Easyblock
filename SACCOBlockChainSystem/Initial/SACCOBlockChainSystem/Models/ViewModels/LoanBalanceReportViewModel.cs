using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class LoanBalanceReportViewModel
	{
		public string MemberNo { get; set; }
		public string Names { get; set; }
		public string LoanNo { get; set; }
		public string LoanType { get; set; }
		public decimal UnpaidInterest { get; set; }
		public decimal PaidInterest { get; set; }
		public decimal AmountIssued { get; set; }
		public decimal LoanBalance { get; set; }
		public decimal AmountPaid { get; set; }
		public decimal Arrears { get; set; }
	}

	public class LoanBalanceIndexViewModel
	{
		public List<LoanBalanceReportViewModel> Loans { get; set; } = new List<LoanBalanceReportViewModel>();

		// Totals
		public decimal TotalUnpaidInterest { get; set; }
		public decimal TotalPaidInterest { get; set; }
		public decimal TotalAmountIssued { get; set; }
		public decimal TotalLoanBalance { get; set; }
		public decimal TotalAmountPaid { get; set; }
		public decimal TotalArrears { get; set; }

		// Statistics
		public int TotalLoans { get; set; }
		public int ActiveLoansCount { get; set; }
		public int ClosedLoansCount { get; set; }

		// Report Parameters
		public DateTime AsAtDate { get; set; }
		public bool HasData { get; set; }
		public string CompanyName { get; set; }
		public string PrintedBy { get; set; }
		public DateTime GeneratedOn { get; set; }
	}


		public class LoanBalancePerLoanReportViewModel
		{
			public string MemberNo { get; set; }
			public string Names { get; set; }
			public string LoanNo { get; set; }
			public string LoanName { get; set; }  // From LoanType1 column in Loantypes
			public string LoanCode { get; set; }
			public decimal UnpaidInterest { get; set; }
			public decimal PaidInterest { get; set; }
			public decimal AmountIssued { get; set; }
			public decimal LoanBalance { get; set; }
			public decimal AmountPaid { get; set; }
			public decimal Arrears { get; set; }
		}

		public class LoanBalancePerLoanIndexViewModel
		{
			public List<LoanBalancePerLoanReportViewModel> Loans { get; set; } = new List<LoanBalancePerLoanReportViewModel>();

			// Totals
			public decimal TotalUnpaidInterest { get; set; }
			public decimal TotalPaidInterest { get; set; }
			public decimal TotalAmountIssued { get; set; }
			public decimal TotalLoanBalance { get; set; }
			public decimal TotalAmountPaid { get; set; }
			public decimal TotalArrears { get; set; }

			// Statistics
			public int TotalLoans { get; set; }
			public int ActiveLoansCount { get; set; }
			public int ClosedLoansCount { get; set; }

			// Report Parameters
			public DateTime AsAtDate { get; set; }
			public bool HasData { get; set; }
			public string CompanyName { get; set; }
			public string PrintedBy { get; set; }
			public DateTime GeneratedOn { get; set; }
	}
	

		public class LoanBalancePerMemberReportViewModel
		{
			public string MemberNo { get; set; }
			public string Names { get; set; }
			public string IDNo { get; set; }
			public string PhoneNo { get; set; }
			public string GigCode { get; set; }
			public string GigName { get; set; }

			// Loan Summary for this member
			public int TotalLoans { get; set; }
			public int ActiveLoans { get; set; }
			public int CompletedLoans { get; set; }

			// Financial Summary
			public decimal TotalAmountIssued { get; set; }
			public decimal TotalLoanBalance { get; set; }
			public decimal TotalAmountPaid { get; set; }
			public decimal TotalUnpaidInterest { get; set; }
			public decimal TotalPaidInterest { get; set; }
			public decimal TotalArrears { get; set; }

			// Individual Loan Details
			public List<MemberLoanDetail> Loans { get; set; } = new List<MemberLoanDetail>();
		}

		public class MemberLoanDetail
		{
			public string LoanNo { get; set; }
			public string LoanName { get; set; }  // From LoanType1
			public string LoanCode { get; set; }
			public DateTime? DateIssued { get; set; }
			public int? RepayPeriod { get; set; }
			public decimal AmountIssued { get; set; }
			public decimal LoanBalance { get; set; }
			public decimal AmountPaid { get; set; }
			public decimal UnpaidInterest { get; set; }
			public decimal PaidInterest { get; set; }
			public decimal Arrears { get; set; }
			public string Status { get; set; }
		}

		public class LoanBalancePerMemberIndexViewModel
		{
			public List<LoanBalancePerMemberReportViewModel> Members { get; set; } = new List<LoanBalancePerMemberReportViewModel>();

			// Overall Totals
			public int TotalMembers { get; set; }
			public int TotalActiveMembers { get; set; }
			public decimal TotalAmountIssued { get; set; }
			public decimal TotalLoanBalance { get; set; }
			public decimal TotalAmountPaid { get; set; }
			public decimal TotalUnpaidInterest { get; set; }
			public decimal TotalPaidInterest { get; set; }
			public decimal TotalArrears { get; set; }

			// Report Parameters
			public DateTime AsAtDate { get; set; }
			public bool HasData { get; set; }
			public string CompanyName { get; set; }
			public string PrintedBy { get; set; }
			public DateTime GeneratedOn { get; set; }
	    }


		public class LoanDueReportViewModel
		{
			public string MemberNo { get; set; }
			public string LoanNo { get; set; }
			public string Names { get; set; }
			public decimal RepayRate { get; set; }
			public decimal IntrOwed { get; set; }
			public decimal Total { get; set; }
			public DateTime DueDate { get; set; }
			public decimal LoanBalance { get; set; }
			public string LoanName { get; set; }
			public decimal Penalty { get; set; }
			public int DaysOverdue { get; set; }
		}

		public class LoanDueIndexViewModel
		{
			public List<LoanDueReportViewModel> Loans { get; set; } = new List<LoanDueReportViewModel>();

			// Totals
			public decimal TotalIntrOwed { get; set; }
			public decimal TotalAmount { get; set; }
			public decimal TotalLoanBalance { get; set; }
			public decimal TotalRepayRate { get; set; }

			// Statistics
			public int TotalLoans { get; set; }
			public int OverdueLoansCount { get; set; }
			public decimal TotalOverdueAmount { get; set; }

			// Report Parameters
			public DateTime StartDate { get; set; }
			public DateTime EndDate { get; set; }
			public bool HasData { get; set; }
			public string CompanyName { get; set; }
			public string PrintedBy { get; set; }
			public DateTime GeneratedOn { get; set; }
		}
	
}



