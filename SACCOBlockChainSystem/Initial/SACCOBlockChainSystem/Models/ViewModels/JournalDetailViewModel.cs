using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class JournalDetailViewModel
    {
        [Required]
        [Display(Name = "Account No")]
        public string AccountNo { get; set; } = string.Empty;

        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [Display(Name = "Narration")]
        public string? Narration { get; set; }

        [Display(Name = "Share Type")]
        public string? ShareType { get; set; }

        [Display(Name = "Debit Amount")]
        public decimal Debit { get; set; }

        [Display(Name = "Credit Amount")]
        public decimal Credit { get; set; }

        [Display(Name = "Posted")]
        public bool Posted { get; set; }

        [Display(Name = "Normal Balance")]
        public string? NormalBalance { get; set; }

        [Display(Name = "Transaction Type")]
        public string? TransType => Debit > 0 ? "DR" : Credit > 0 ? "CR" : "";
    }
}