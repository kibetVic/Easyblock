using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.ViewModels
{
    public class BlockchainExplorerViewModel
    {
        public List<Block> Blocks { get; set; } = new();
        public List<TransactionSummaryViewModel> RecentTransactions { get; set; } = new();
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalBlocks { get; set; }
        public int TotalPages { get; set; }
        public BlockchainStatusViewModel Status { get; set; } = new();
    }

    public class BlockchainStatusViewModel
    {
        public int TotalBlocks { get; set; }
        public int TotalTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public string? LatestBlockHash { get; set; }
        public DateTime? LatestBlockTimestamp { get; set; }
        public bool IsValid { get; set; }
    }

    public class TransactionSummaryViewModel
    {
        public string TransactionId { get; set; } = null!;
        public string TransactionType { get; set; } = null!;
        public string MemberNo { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = null!;
        public string? BlockHash { get; set; }
    }

    public class BlockDetailsViewModel
    {
        public Block Block { get; set; } = null!;
        public List<BlockchainTransaction> Transactions { get; set; } = new();
        public int TransactionCount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsValid { get; set; }
    }

    public class TransactionDetailsViewModel
    {
        public BlockchainTransaction Transaction { get; set; } = null!;
        public object? RelatedData { get; set; }
        public object? Payload { get; set; }
        public Block? Block { get; set; }
        public bool VerificationStatus { get; set; }
    }

    public class TransactionVerificationViewModel
    {
        public string? TransactionId { get; set; }
        public BlockchainTransaction? Transaction { get; set; }
        public bool IsValid { get; set; }
        public string? Message { get; set; }
        public VerificationDetails? VerificationDetails { get; set; }
    }

    public class VerificationDetails
    {
        public bool FoundInBlock { get; set; }
        public bool BlockConfirmed { get; set; }
        public bool DataIntegrity { get; set; }
        public bool TimestampValid { get; set; }
        public string? CalculatedHash { get; set; }
    }

    public class ReferenceVerificationViewModel
    {
        public string ReferenceId { get; set; } = null!;
        public string ReferenceType { get; set; } = null!;
        public List<BlockchainTransaction> Transactions { get; set; } = new();
        public int TotalTransactions { get; set; }
        public decimal TotalAmount { get; set; }
        public bool AllVerified { get; set; }
    }

    public class MyTransactionsViewModel
    {
        public List<MyTransactionViewModel> Transactions { get; set; } = new();
        public MyTransactionsStatistics Statistics { get; set; } = new();
        public ActivitySummaryViewModel ActivitySummary { get; set; } = new();
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string CurrentFilter { get; set; } = "all";
        public string MemberNo { get; set; } = null!;
        public string UserName { get; set; } = null!;
    }

    public class MyTransactionViewModel
    {
        public string TransactionId { get; set; } = null!;
        public string TransactionType { get; set; } = null!;
        public string MemberNo { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = null!;
        public string? BlockHash { get; set; }
        public string? DataHash { get; set; }
        public bool IsVerified { get; set; }
        public string YourRole { get; set; } = null!;
        public bool CanVerify { get; set; }
    }

    public class MyTransactionsStatistics
    {
        public int TotalTransactions { get; set; }
        public decimal TotalAmount { get; set; }
        public int ConfirmedCount { get; set; }
        public int PendingCount { get; set; }
        public DateTime? LastTransactionDate { get; set; }
        public string MemberNo { get; set; } = null!;
    }

    public class ActivitySummaryViewModel
    {
        public int ThisMonth { get; set; }
        public int LastMonth { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
        public decimal TotalContributions { get; set; }
        public decimal TotalLoans { get; set; }
    }

    public class AuditTrailViewModel
    {
        public string MemberNo { get; set; } = null!;
        public List<BlockchainTransaction> Transactions { get; set; } = new();
        public int TotalCount { get; set; }
        public decimal TotalAmount { get; set; }
        public BlockchainTransaction? FirstTransaction { get; set; }
        public BlockchainTransaction? LastTransaction { get; set; }
        public bool ChainValid { get; set; }
        public Dictionary<string, bool> VerificationStatuses { get; set; } = new();
    }
}