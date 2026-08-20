using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class JournalEntryViewModel
    {
        public string? VoucherNo { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime VoucherDate { get; set; } = DateTime.Now;

        public string? Description { get; set; }

        public string? MemberNo { get; set; }

        public string? LoanNo { get; set; }

        public string? CompanyCode { get; set; }

        public string? TransactionNo { get; set; }

        public string? AuditId { get; set; }

        public DateTime AuditDate { get; set; }

        public bool Posted { get; set; }

        public DateTime PostedDate { get; set; }

        public List<JournalDetailViewModel> Details { get; set; } = new List<JournalDetailViewModel>();

        // Summary properties
        public decimal TotalDebit => Details?.Sum(x => x.Debit) ?? 0;
        public decimal TotalCredit => Details?.Sum(x => x.Credit) ?? 0;
        public decimal Difference => TotalDebit - TotalCredit;
        public bool IsBalanced => Math.Abs(Difference) < 0.01m;
        public int EntryCount => Details?.Count ?? 0;
    }
}