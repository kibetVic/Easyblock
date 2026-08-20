// Models/ViewModels/JournalsListingViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class JournalsListingViewModel
    {
        public long JlId { get; set; }
        public string? VoucherNo { get; set; }
        public string? AccountNo { get; set; }
        public string? AccountName { get; set; }
        public string? Narration { get; set; }
        public string? MemberNo { get; set; }
        public string? ShareType { get; set; }
        public string? LoanNo { get; set; }
        public decimal? AmountDr { get; set; }
        public decimal? AmountCr { get; set; }
        public decimal? Amount { get; set; }
        public string? TransType { get; set; }
        public string? AuditId { get; set; }
        public DateTime? TransDate { get; set; }
        public DateTime AuditDate { get; set; }
        public bool Posted { get; set; }
        public DateTime PostedDate { get; set; }
        public string? TransactionNo { get; set; }
        public string? CompanyCode { get; set; }

        // Computed properties
        public string DisplayAmount => Amount?.ToString("N2") ?? "0.00";
        public string DisplayAmountDr => AmountDr?.ToString("N2") ?? "0.00";
        public string DisplayAmountCr => AmountCr?.ToString("N2") ?? "0.00";
        public string DisplayDate => TransDate?.ToString("dd/MM/yyyy HH:mm") ?? "";
        public string Status => Posted ? "Posted" : "Draft";
        public string StatusClass => Posted ? "success" : "warning";
    }

    public class JournalsListingSearchViewModel
    {
        [Display(Name = "From Date")]
        [DataType(DataType.Date)]
        public DateTime? FromDate { get; set; }

        [Display(Name = "To Date")]
        [DataType(DataType.Date)]
        public DateTime? ToDate { get; set; }

        [Display(Name = "Voucher No")]
        public string? VoucherNo { get; set; }

        [Display(Name = "Member No")]
        public string? MemberNo { get; set; }

        [Display(Name = "Account No")]
        public string? AccountNo { get; set; }

        [Display(Name = "Posted")]
        public bool? Posted { get; set; }

        public List<JournalsListingViewModel> Results { get; set; } = new List<JournalsListingViewModel>();
    }
}