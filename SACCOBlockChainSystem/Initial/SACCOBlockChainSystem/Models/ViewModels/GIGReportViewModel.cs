using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{

    public class GIGReportMemberDetail
    {
        public string MemberNo { get; set; }
        public string Names { get; set; }
        public string Sex { get; set; }
        public string PhoneNo { get; set; }
        public string IDNo { get; set; }
        public int? Age { get; set; }
        public string CIGCode { get; set; }
        public string CIGName { get; set; }
        public decimal ShareCapital { get; set; }
        public decimal ShareDeposits { get; set; }
        public decimal RegFee { get; set; }
        public decimal LoanAmt { get; set; }
        public decimal RecommendedLoanAmt { get; set; } 
    }

	public class GIGReportViewModel
	{
		public string CIGCode { get; set; }
		public string CIGName { get; set; }
		public string CompanyCode { get; set; }
		public string CompanyName { get; set; }
		public int TotalMembers { get; set; }
		public int MaleCount { get; set; }
		public int FemaleCount { get; set; }
		public int OtherCount { get; set; }
		public int YouthCount { get; set; }
		public decimal TotalShareCapital { get; set; }
		public decimal TotalShareDeposits { get; set; }
		public decimal TotalRegFee { get; set; }
		public decimal TotalLoans { get; set; }
		public List<GIGReportMemberDetail> Members { get; set; }
	}

	public class GIGReportIndexViewModel
	{
		public List<GIGReportViewModel> GIGs { get; set; }
		public DateTime StartDate { get; set; }
		public DateTime EndDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
		public int TotalGIGs { get; set; }
		public int TotalGIGMembers { get; set; }
		public int TotalMaleMembers { get; set; }
		public int TotalFemaleMembers { get; set; }
		public int TotalYouthMembers { get; set; }
		public decimal TotalShareCapitalAllGIGs { get; set; }
		public decimal TotalShareDepositsAllGIGs { get; set; }
		public decimal TotalRegFeeAllGIGs { get; set; }
		public decimal TotalLoansAllGIGs { get; set; }
	}
}