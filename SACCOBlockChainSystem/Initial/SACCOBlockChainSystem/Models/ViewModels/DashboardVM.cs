using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class DashboardVM
    {
        // User Information
        public string UserGroup { get; set; } = "Member";
        public List<string> UserRoles { get; set; } = new();

        // Basic Statistics (for all users)
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int DormantMembers { get; set; }
        public int TotalMen { get; set; }
        public int TotalWomen { get; set; }
        public int TotalOthers { get; set; }

        // Active/Dormant by gender
        public int ActiveWomen { get; set; }
        public int DormantWomen { get; set; }
        public int ActiveMen { get; set; }
        public int DormantMen { get; set; }

        // Additional member statistics
        public int ActiveMembersByStatus { get; set; }
        public int WithdrawnMembers { get; set; }
        public int DormantFlaggedMembers { get; set; }
        public int YouthTotal { get; set; }
        public int YouthMale { get; set; }
        public int YouthFemale { get; set; }
        public int PendingMembers { get; set; }

        // Financial Data Properties - Contribution
        public decimal TotalContributions { get; set; }
        public decimal WomenContributions { get; set; }
        public decimal MenContributions { get; set; }
        public decimal OthersContributions { get; set; }

        // Share Capital
        public decimal TotalShareCapital { get; set; }
        public decimal WomenShareCapital { get; set; }
        public decimal MenShareCapital { get; set; }
        public decimal OthersShareCapital { get; set; }

        // Non-Withdrawable Deposits
        public decimal TotalDeposits { get; set; }
        public decimal WomenDeposits { get; set; }
        public decimal MenDeposits { get; set; }
        public decimal OthersDeposits { get; set; }

        // Registration Fees
        public decimal TotalRegistrationFees { get; set; }
        public decimal WomenRegistrationFees { get; set; }
        public decimal MenRegistrationFees { get; set; }
        public decimal OthersRegistrationFees { get; set; }

        // Loans Taken
        public decimal TotalLoansTaken { get; set; }
        public decimal WomenLoansTaken { get; set; }
        public decimal MenLoansTaken { get; set; }
        public decimal OthersLoansTaken { get; set; }

        // Loan Balances (Outstanding)
        public decimal TotalLoanBalances { get; set; }
        public decimal WomenLoanBalances { get; set; }
        public decimal MenLoanBalances { get; set; }
        public decimal OthersLoanBalances { get; set; }

        // Loans Paid
        public decimal TotalLoansPaid { get; set; }
        public decimal WomenLoansPaid { get; set; }
        public decimal MenLoansPaid { get; set; }
        public decimal OthersLoansPaid { get; set; }

        // Total Loanees (distinct members with loans)
        public int TotalLoanees { get; set; }
        public int WomenLoanees { get; set; }
        public int MenLoanees { get; set; }
        public int OthersLoanees { get; set; }
        public decimal InclusionGrantTotal { get; set; }
        public decimal MatchingGrantTotal { get; set; }

        public decimal TotalGrants => InclusionGrantTotal + MatchingGrantTotal;

        public int InclusionGrantCount { get; set; }
        public int MatchingGrantCount { get; set; }

        // ==========================
        // REPAYMENT & RISK PROPERTIES
        // ==========================
        public decimal RepaymentRate { get; set; }  // Current month repayment rate (%)
        public decimal PARPercent { get; set; }     // Portfolio at Risk > 30 Days (%)
        public decimal AmountPastDueRate { get; set; } // Amount past due rate (%)

        // ==========================
        // PROFIT / LOSS PROPERTIES
        // ==========================
        public decimal LastMonthProfitLoss { get; set; }
        public decimal ThisMonthProfitLoss { get; set; }
        public decimal ProfitLossChange { get; set; }

        // ==========================
        // ADDITIONAL METRICS PROPERTIES
        // ==========================
        public decimal OutstandingLoanPortfolio { get; set; }  // Total outstanding loans
        public decimal ArrearsBalance { get; set; }            // Balance of loans with arrears > 30 days
        public decimal TotalArrears { get; set; }              // Total arrears > 30 days

        // ==========================
        // SUMMARY STATS PROPERTIES
        // ==========================
        public decimal WomenParticipationRate { get; set; }    // Women participation percentage
        public string LoanPortfolioHealth { get; set; } = "Good"; // Portfolio health status

        // Legacy properties (for backward compatibility)
        public decimal TotalLoanRepayments { get; set; }
        public decimal TotalWithdrawals { get; set; }
        public decimal TotalLoansIssued { get; set; }

        // Company filtering
        public string SelectedCompanyCode { get; set; } = string.Empty;
        public string SelectedCompanyName { get; set; } = "All Companies";
        public List<CompanyInfo> Companies { get; set; } = new List<CompanyInfo>();

        // Gender Statistics
        public GenderDistribution GenderStats { get; set; } = new GenderDistribution();

        // Age Group Statistics
        public List<AgeGroupData> AgeGroups { get; set; } = new List<AgeGroupData>();

        // Employment Statistics
        public EmploymentStats EmploymentStatistics { get; set; } = new EmploymentStats();

        // Contribution Statistics
        public ContributionStats ContributionStatistics { get; set; } = new ContributionStats();

        // Loan Performance Statistics
        public LoanPerformanceStats LoanPerformance { get; set; } = new LoanPerformanceStats();

        // Blockchain Statistics
        public int TotalBlockchainTransactions { get; set; }
        public int BlocksCreatedToday { get; set; }
        public int PendingBlockchainTransactions { get; set; }
        public string BlockchainStatus { get; set; } = "Active";

        // Teller-Specific Statistics
        public int TotalTransactionsToday { get; set; }
        public decimal TotalDepositsToday { get; set; }
        public decimal TotalWithdrawalsToday { get; set; }
        public int PendingVerifications { get; set; }

        // Loan Officer Specific Statistics
        public int TotalLoansProcessedToday { get; set; }
        public int PendingApplicationsCount { get; set; }

        // Chart Data
        public List<MonthlyTransactionData> MonthlyTransactions { get; set; } = new();
        public List<MemberGrowthData> MemberGrowth { get; set; } = new();
        public List<LoanTypeDistribution> LoanTypeDistribution { get; set; } = new();
        public List<ShareTypeDistribution> ShareTypeDistribution { get; set; } = new();

        // Charts Data
        public List<MonthlyContributionData> MonthlyContributions { get; set; } = new List<MonthlyContributionData>();
        public List<LoanStatusDistribution> LoanStatusData { get; set; } = new List<LoanStatusDistribution>();
        public List<ShareGrowthData> ShareGrowth { get; set; } = new List<ShareGrowthData>();
        public List<MonthlyLoanData> MonthlyLoanIssuance { get; set; } = new List<MonthlyLoanData>();

        // Recent Activities
        public List<RecentTransaction> RecentTransactions { get; set; } = new();
        public List<RecentLoan> RecentLoans { get; set; } = new();
        public List<PendingLoan> PendingLoans { get; set; } = new();

        // Recent Contributions
        public List<RecentContribution> RecentContributions { get; set; } = new List<RecentContribution>();

        // Quick Stats
        public DashboardQuickStats QuickStats { get; set; } = new();

        // Member-Specific Data
        public decimal MemberShareBalance { get; set; }
        public decimal MemberTotalLoans { get; set; }
        public int MemberRecentTransactionCount { get; set; }

        // Blockchain Dashboard Data
        public BlockchainDashboardData BlockchainData { get; set; } = new BlockchainDashboardData();

        // Wallet Information
        public List<WalletInfo> Wallets { get; set; } = new List<WalletInfo>();

        // Recent Blocks
        public List<Models.Block> RecentBlocks { get; set; } = new List<Models.Block>();

        // Blockchain Chain Structure
        public List<BlockchainChain> BlockchainChains { get; set; } = new List<BlockchainChain>();

        // Summary Metrics
        public DashboardSummaryMetrics SummaryMetrics { get; set; } = new DashboardSummaryMetrics();
    }

    // Rest of your existing classes remain the same...
    public class CompanyInfo
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class GenderDistribution
    {
        public int MaleCount { get; set; }
        public int FemaleCount { get; set; }
        public int OtherCount { get; set; }
        public decimal MalePercentage => Total > 0 ? (MaleCount * 100m / Total) : 0;
        public decimal FemalePercentage => Total > 0 ? (FemaleCount * 100m / Total) : 0;
        public decimal OtherPercentage => Total > 0 ? (OtherCount * 100m / Total) : 0;
        public int Total => MaleCount + FemaleCount + OtherCount;
    }

    public class AgeGroupData
    {
        public string AgeGroup { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public decimal Percentage { get; set; }
        public string Color { get; set; } = "#3498db";
    }

    public class EmploymentStats
    {
        public Dictionary<string, int> DepartmentDistribution { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> EmployerDistribution { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> RankDistribution { get; set; } = new Dictionary<string, int>();
    }

    public class ContributionStats
    {
        public decimal AverageMonthlyContribution { get; set; }
        public decimal TotalContributionsThisYear { get; set; }
        public decimal TotalContributionsLastYear { get; set; }
        public decimal GrowthRate => TotalContributionsLastYear > 0
            ? ((TotalContributionsThisYear - TotalContributionsLastYear) * 100m / TotalContributionsLastYear)
            : 0;
        public Dictionary<string, decimal> ContributionByMonth { get; set; } = new Dictionary<string, decimal>();
    }

    public class LoanPerformanceStats
    {
        public decimal AverageLoanAmount { get; set; }
        public decimal AverageRepaymentPeriod { get; set; }
        public decimal DefaultRate { get; set; }
        public decimal RepaymentRate { get; set; }
        public Dictionary<string, int> LoansByStatus { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, decimal> LoanPerformanceByMonth { get; set; } = new Dictionary<string, decimal>();
    }

    public class MonthlyContributionData
    {
        public string Month { get; set; } = string.Empty;
        public decimal ShareCapital { get; set; }
        public decimal Deposits { get; set; }
        public decimal PassBook { get; set; }
        public decimal Total => ShareCapital + Deposits + PassBook;
    }

    public class LoanStatusDistribution
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
        public string Color { get; set; } = "#3498db";
    }

    public class ShareGrowthData
    {
        public string Period { get; set; } = string.Empty;
        public decimal TotalShares { get; set; }
        public decimal NewShares { get; set; }
        public decimal ShareCapitalGrowth { get; set; }
    }

    public class MonthlyLoanData
    {
        public string Month { get; set; } = string.Empty;
        public int LoansIssued { get; set; }
        public decimal TotalAmountIssued { get; set; }
        public decimal AverageLoanAmount => LoansIssued > 0 ? TotalAmountIssued / LoansIssued : 0;
        public int LoansRepaid { get; set; }
        public decimal AmountRepaid { get; set; }
    }

    public class RecentContribution
    {
        public string MemberNo { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string ReceiptNo { get; set; } = string.Empty;
    }

    public class DashboardSummaryMetrics
    {
        public decimal MemberRetentionRate { get; set; }
        public decimal LoanToDepositRatio { get; set; }
        public decimal ShareCapitalGrowthRate { get; set; }
        public decimal AverageMemberAge { get; set; }
        public int ActiveLoanAccounts { get; set; }
        public int DormantAccounts { get; set; }
        public decimal PortfolioAtRisk { get; set; }
    }

    public class MonthlyTransactionData
    {
        public string Month { get; set; } = string.Empty;
        public decimal Deposits { get; set; }
        public decimal Withdrawals { get; set; }
        public decimal LoanRepayments { get; set; }
    }

    public class MemberGrowthData
    {
        public string Period { get; set; } = string.Empty;
        public int NewMembers { get; set; }
        public int TotalMembers { get; set; }
    }

    public class LoanTypeDistribution
    {
        public string LoanType { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
        public string Color { get; set; } = "#3498db";
    }

    public class ShareTypeDistribution
    {
        public string ShareType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int MemberCount { get; set; }
        public string Color { get; set; } = "#2ecc71";
    }

    public class RecentTransaction
    {
        public string TransactionId { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; } = "Completed";
        public string BlockchainTxId { get; set; } = string.Empty;
    }

    public class RecentLoan
    {
        public string LoanNo { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime ApplicationDate { get; set; }
    }

    public class PendingLoan
    {
        public string LoanNo { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime ApplicationDate { get; set; }
        public int DaysPending { get; set; }
    }

    public class DashboardQuickStats
    {
        public decimal AverageDeposit { get; set; }
        public decimal AverageLoan { get; set; }
        public int TransactionsToday { get; set; }
        public int NewMembersToday { get; set; }
        public decimal LoanApprovalRate { get; set; }
        public decimal BlockchainUptime { get; set; } = 99.9m;
    }

    public class BlockchainDashboardData
    {
        public int TotalBlocks { get; set; }
        public int TotalTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public string? LatestBlockHash { get; set; }
        public DateTime? LatestBlockTimestamp { get; set; }
        public int TotalWallets { get; set; }
        public int ActiveWallets { get; set; }
        public int BlockchainHeight { get; set; }
        public bool IsChainValid { get; set; }
    }

    public class WalletInfo
    {
        public string Address { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsActive { get; set; }
    }

    public class BlockchainChain
    {
        public string BlockHash { get; set; } = string.Empty;
        public string PreviousHash { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int TransactionCount { get; set; }
        public long Nonce { get; set; }
        public string MerkleRoot { get; set; } = string.Empty;
    }

}