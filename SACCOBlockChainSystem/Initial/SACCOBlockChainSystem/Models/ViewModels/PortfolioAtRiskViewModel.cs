using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class PortfolioAtRiskFilter
    {
        public DateTime? AsAtDate { get; set; }
    }

    public class PortfolioAtRiskRecord
    {
        public string? LoanTypeName { get; set; }
        public decimal OutstandingPrincipal { get; set; }
        public decimal Arrears { get; set; }
        public decimal PAR { get; set; }  // Portfolio at Risk %
    }

    public class PortfolioAtRiskViewModel
    {
        public PortfolioAtRiskFilter Filter { get; set; } = new();
        public List<PortfolioAtRiskRecord> Records { get; set; } = new();
        public decimal TotalOutstandingPrincipal { get; set; }
        public decimal TotalArrears { get; set; }
        public decimal OverallPAR { get; set; }
        public int TotalRecords { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}