// InterestListingReportViewModel.cs
using System;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class InterestListingReportViewModel
    {
        public string MemberNo { get; set; }
        public string Names { get; set; }
        public decimal Interest { get; set; }
        public string Description { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string DocumentNo { get; set; }
        public string TransactionNo { get; set; }
        public string LoanNo { get; set; }
    }

    public class InterestListingIndexViewModel
    {
        public List<InterestListingReportViewModel> Interests { get; set; } = new List<InterestListingReportViewModel>();
        public decimal TotalInterest { get; set; }
        public int TotalTransactions { get; set; }
        public int UniqueMembers { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool HasData { get; set; }
        public string CompanyName { get; set; }
        public string PrintedBy { get; set; }
        public DateTime GeneratedOn { get; set; }
    }
}