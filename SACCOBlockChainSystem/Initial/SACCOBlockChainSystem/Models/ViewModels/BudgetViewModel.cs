//// Models/ViewModels/BudgetViewModel.cs
//using System;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;

//namespace SACCOBlockChainSystem.Models.ViewModels
//{
//    public class BudgetViewModel
//    {
//        //public BudgetHeader? BudgetHeader { get; set; }
//        public List<BudgetEntryViewModel> BudgetEntries { get; set; } = new List<BudgetEntryViewModel>();
//        public List<AccountDropdownModel> Accounts { get; set; } = new List<AccountDropdownModel>();

//        public decimal TotalAllocated => BudgetEntries?.Sum(x => x.BudgetAmount) ?? 0;
//        public decimal RemainingBudget => (BudgetHeader?.TotalBudget ?? 0) - TotalAllocated;
//    }

//    public class BudgetEntryViewModel
//    {
//        public long EntryId { get; set; }

//        [Required(ErrorMessage = "Account is required")]
//        public string AccountNo { get; set; } = string.Empty;

//        public string? AccountName { get; set; }

//        [Required(ErrorMessage = "Budget amount is required")]
//        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
//        public decimal BudgetAmount { get; set; }

//        public decimal OpeningBalance { get; set; }
//        public decimal CurrentUtilization { get; set; }
//        public decimal RemainingBalance => OpeningBalance - CurrentUtilization;
//        public string? Remarks { get; set; }
//    }

//    public class AccountDropdownModel
//    {
//        public string AccountNo { get; set; } = string.Empty;
//        public string AccountName { get; set; } = string.Empty;
//        public string DisplayText => $"{AccountNo} - {AccountName}";
//        public decimal OpeningBalance { get; set; }
//    }
//}