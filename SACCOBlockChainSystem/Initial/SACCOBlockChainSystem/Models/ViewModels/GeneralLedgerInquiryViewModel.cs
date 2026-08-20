// Models/ViewModels/GeneralLedgerInquiryViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class GeneralLedgerInquiryViewModel
    {
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.Now.AddMonths(-1);

        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; } = DateTime.Now;

        [Display(Name = "Select Account")]
        public string? SelectedAccountNo { get; set; }

        public string? SelectedAccountName { get; set; }

        [Display(Name = "Current Account")]
        public string? CurrentAccount { get; set; }

        [Display(Name = "Book Balance")]
        public decimal BookBalance { get; set; }

        [Display(Name = "Opening Balance")]
        public decimal OpeningBalance { get; set; }

        public List<GeneralLedgerTransaction> Transactions { get; set; } = new List<GeneralLedgerTransaction>();

        public List<AccountDropdownViewModel> Accounts { get; set; } = new List<AccountDropdownViewModel>();
    }

    public class GeneralLedgerTransaction
    {
        public string? AuditId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string? TransactionNo { get; set; }
        public string? TransactionRemarks { get; set; }
        public string? MemberNo { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RunningBalance { get; set; }
        public string? DocumentNo { get; set; }
        public string? Source { get; set; }
        public bool Posted { get; set; }
        public DateTime? PostedDate { get; set; }
        public string? Reference { get; set; }

        // Computed properties for display
        public string PostedStatus => Posted ? "Posted" : "Draft";
        public string PostedBadgeClass => Posted ? "bg-success" : "bg-warning";
        public string DisplayAmount => DebitAmount > 0 ? DebitAmount.ToString("N2") : CreditAmount.ToString("N2");
        public string DisplayType => DebitAmount > 0 ? "DR" : "CR";
    }
}

public class AccountDropdownViewModel
{
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string DisplayText => $"{AccountNo} - {AccountName}";
    public string? NormalBalance { get; set; }
    public bool IsActive { get; set; }
}