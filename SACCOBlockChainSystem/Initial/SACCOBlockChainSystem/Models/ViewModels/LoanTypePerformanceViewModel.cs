using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class LoanTypePerformanceFilter
    {
        public DateTime? AsAtDate { get; set; }
    }

    public class LoanTypePerformanceRecord
    {
        public string? LoanTypeName { get; set; }
        public decimal TotalDisbursed { get; set; }        // Amount
        public decimal TotalPrincipalBalance { get; set; } // Total PrinBal
        public decimal TotalArrears { get; set; }          // Total PrinBal Arears
        public decimal PAR { get; set; }                   // Portfolio at Risk %
    }

    public class LoanTypePerformanceViewModel
    {
        public LoanTypePerformanceFilter Filter { get; set; } = new();
        public List<LoanTypePerformanceRecord> Records { get; set; } = new();
        public decimal GrandTotalDisbursed { get; set; }
        public decimal GrandTotalPrincipalBalance { get; set; }
        public decimal GrandTotalArrears { get; set; }
        public decimal OverallPAR { get; set; }
        public int TotalRecords { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}