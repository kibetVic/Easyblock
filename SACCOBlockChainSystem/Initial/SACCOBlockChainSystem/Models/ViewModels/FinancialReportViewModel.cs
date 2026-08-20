// Models/ViewModels/FinancialReportViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class FinancialReportViewModel
    {
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = new DateTime(DateTime.Now.Year, 1, 1);

        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; } = DateTime.Now;

        public string ReportType { get; set; } = "TrialBalance";

        // Trial Balance
        public List<TrialBalanceItem> TrialBalance { get; set; } = new();

        // Income Statement
        public IncomeStatementModel IncomeStatement { get; set; } = new();

        // Balance Sheet
        public BalanceSheetModel BalanceSheet { get; set; } = new();

        // Cash Flow
        public CashFlowModel CashFlow { get; set; } = new();

        // Summary
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
        public bool IsBalanced => Math.Abs(TotalDebits - TotalCredits) < 0.01m;

        // ========== BALANCE SHEET SUSPENSE PROPERTIES ==========

        // Balance Sheet validation
        public bool BalanceSheetBalanced { get; set; }
        public decimal BalanceSheetDifference => TotalAssets - (TotalLiabilities + TotalEquity);
        public bool ShowSuspenseOption => !BalanceSheetBalanced && HasSuspenseAccount;

        // Suspense Account (created in GL Setup)
        public bool HasSuspenseAccount { get; set; }
        public string? SuspenseAccountNo { get; set; }
        public string? SuspenseAccountName { get; set; }
        public decimal SuspenseBalance { get; set; }
        public string SuspensePlacement { get; set; } = string.Empty; // "Asset" or "Liability/Equity"

        // Suspense Aging
        public List<SuspenseAgingItem> SuspenseAging { get; set; } = new();

        // Totals for Balance Sheet
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }
    }

    public class TrialBalanceItem
    {
        public string AccountNo { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string NormalBalance { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public bool IsSuspense { get; set; } // Flag for suspense accounts
    }

    public class IncomeStatementModel
    {
        public List<IncomeStatementItem> Revenue { get; set; } = new();
        public List<IncomeStatementItem> Expenses { get; set; } = new();
        public decimal TotalRevenue => Revenue.Sum(x => x.Amount);
        public decimal TotalExpenses => Expenses.Sum(x => x.Amount);
        public decimal NetIncome => TotalRevenue - TotalExpenses;
    }

    public class IncomeStatementItem
    {
        public string AccountNo { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class BalanceSheetModel
    {
        public List<BalanceSheetItem> Assets { get; set; } = new();
        public List<BalanceSheetItem> Liabilities { get; set; } = new();
        public List<BalanceSheetItem> Equity { get; set; } = new();

        public decimal TotalAssets => Assets.Sum(x => x.Amount);
        public decimal TotalLiabilities => Liabilities.Sum(x => x.Amount);
        public decimal TotalEquity => Equity.Sum(x => x.Amount);
        public bool IsBalanced => Math.Abs(TotalAssets - (TotalLiabilities + TotalEquity)) < 0.01m;
    }

    public class BalanceSheetItem
    {
        public string AccountNo { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsSuspense { get; set; } // Flag for suspense accounts


        public BalanceSheetModel BalanceSheet { get; set; } = new();

        private decimal _balanceSheetDifference;
        public decimal BalanceSheetDifference
        {
            get => _balanceSheetDifference;
            set => _balanceSheetDifference = value; // Add setter
        }

        private bool _balanceSheetBalanced;

        public bool BalanceSheetBalanced
        {
            get => _balanceSheetBalanced;
            set => _balanceSheetBalanced = value; // Add setter
        }
    }
}

public class CashFlowModel
{
    public List<CashFlowItem> OperatingActivities { get; set; } = new();
    public List<CashFlowItem> InvestingActivities { get; set; } = new();
    public List<CashFlowItem> FinancingActivities { get; set; } = new();
    public decimal NetOperating => OperatingActivities.Sum(x => x.Amount);
    public decimal NetInvesting => InvestingActivities.Sum(x => x.Amount);
    public decimal NetFinancing => FinancingActivities.Sum(x => x.Amount);
    public decimal NetCashFlow => NetOperating + NetInvesting + NetFinancing;
    public decimal BeginningCash { get; set; }
    public decimal EndingCash { get; set; }
}

public class CashFlowItem
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

// Suspense Aging Item
public class SuspenseAgingItem
{
    public string? VoucherNo { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string? Type { get; set; } // DR or CR
    public int DaysOld { get; set; }
    public string? AgingBucket { get; set; }
}