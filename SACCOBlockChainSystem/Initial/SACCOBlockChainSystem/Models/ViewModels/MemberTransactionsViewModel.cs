// Models/ViewModels/MemberTransactionsViewModel.cs
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class MemberTransactionsViewModel
    {
        public Member? Member { get; set; }
        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAdmin { get; set; }
        public string? MemberNo { get; set; }
        public string? MemberName { get; set; }
        public decimal CurrentBalance { get; set; }
        public int TotalTransactions { get; set; }
        public decimal TotalDeposits { get; set; }
        public decimal TotalWithdrawals { get; set; }
        public decimal TotalShares { get; set; }
        public decimal LoanBalance { get; set; }
        public DateTime? LastTransactionDate { get; set; }
    }

    public class MemberTransactionHistory
    {
        public DateTime Date { get; set; }
        public string TransactionType { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        public string ReceiptNo { get; set; }
        public decimal BalanceAfter { get; set; }
    }
}