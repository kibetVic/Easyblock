using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class ChequeReceivedReportFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    public class ChequeReceivedRecord
    {
        public string? ReceiptNumber { get; set; }   // TransactionNo or Voucherno
        public string? MemberNumber { get; set; }    // MemberNo
        public string? ChequeNumber { get; set; }    // ChequeNo
        public decimal Amount { get; set; }
        public DateTime? DateDeposited { get; set; } // DateIssued
        public string? SaccoName { get; set; }       // CompanyCode
    }

    public class ChequeSaccoGroup
    {
        public string SaccoName { get; set; } = string.Empty;
        public List<ChequeReceivedRecord> Records { get; set; } = new();
        public decimal Subtotal { get; set; }
    }

    public class ChequeReceivedReportViewModel
    {
        public ChequeReceivedReportFilter Filter { get; set; } = new();
        public List<ChequeSaccoGroup> Groups { get; set; } = new();
        public decimal GrandTotal { get; set; }
        public int TotalRecords { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}