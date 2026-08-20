using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class ShareTypeViewModel
    {
        [Required]
        public string SharesCode { get; set; }

        [Required]
        public string SharesType { get; set; }

        public string SharesAcc { get; set; }

        public decimal MinAmount { get; set; }
        public decimal MaxAmount { get; set; }

        public decimal? LoanToShareRatio { get; set; }

        public bool IsMainShares { get; set; }
        public bool Withdrawable { get; set; }

        public bool UsedToOffset { get; set; }     // ✅ ADDED
        public bool UsedToGuarantee { get; set; }  // ✅ ADDED

        public string CompanyCode { get; set; }
        public string CompanyName { get; set; }

        // 🔥 Optional but useful for UI state
        public bool IsSelected { get; set; } = false;
    }
}