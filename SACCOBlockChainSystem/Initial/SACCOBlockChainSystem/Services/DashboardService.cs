using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Repositories;

namespace SACCOBlockChainSystem.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemberRepository _memberRepository;
        private readonly ITransactionRepository _transactionRepository;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(
            ApplicationDbContext context,
            IMemberRepository memberRepository,
            ITransactionRepository transactionRepository,
            ILogger<DashboardService> logger)
        {
            _context = context;
            _memberRepository = memberRepository;
            _transactionRepository = transactionRepository;
            _logger = logger;
        }

        public async Task<DashboardVM> GetDashboardDataAsync()
        {
            try
            {
                var dashboard = new DashboardVM();

                // Get statistics
                dashboard.TotalMembers = await _context.Members.CountAsync();
                dashboard.ActiveMembers = await _context.Members.CountAsync(m => m.Status == 1);
                dashboard.PendingMembers = await _context.Members.CountAsync(m => m.Status == 0);

                dashboard.TotalShareCapital = await _context.Shares.SumAsync(s => s.TotalShares ?? 0);

                var approvedStatuses = new[] { (int)Status.Approved, (int)Status.Endorsed, (int)Status.Disbursed };
                dashboard.TotalLoansIssued = await _context.Loans
                    .Where(l => approvedStatuses.Contains(l.Status ?? 0))
                    .SumAsync(l => l.LoanAmt ?? 0);

                dashboard.TotalLoanRepayments = await _context.Repay
                    .Where(r => r.Posted == true)
                    .SumAsync(r => r.Amount ?? 0);

                dashboard.TotalDeposits = await _context.Transactions2
                    .Where(t => t.TransactionType == "DEPOSIT" && t.Status == "COMPLETED")
                    .SumAsync(t => t.Amount);

                dashboard.TotalWithdrawals = await _context.Transactions2
                    .Where(t => t.TransactionType == "WITHDRAWAL" && t.Status == "COMPLETED")
                    .SumAsync(t => t.Amount);

                // Blockchain statistics
                dashboard.TotalBlockchainTransactions = await _context.BlockchainTransactions.CountAsync();
                dashboard.BlocksCreatedToday = await _context.Blocks
                    .Where(b => b.Timestamp.Date == DateTime.Today)
                    .CountAsync();
                dashboard.PendingBlockchainTransactions = await _context.BlockchainTransactions
                    .CountAsync(t => t.Status == "PENDING");

                // Chart data
                dashboard.MonthlyTransactions = await GetMonthlyTransactionsDataAsync(); // CHANGED NAME
                dashboard.MemberGrowth = await GetMemberGrowthDataAsync(); // CHANGED NAME
                dashboard.LoanTypeDistribution = await GetLoanTypeDistributionAsync();
                dashboard.ShareTypeDistribution = await GetShareTypeDistributionAsync();

                // Recent activities
                dashboard.RecentTransactions = await GetRecentTransactionsAsync();
                dashboard.RecentLoans = await GetRecentLoansAsync();
                dashboard.PendingLoans = await GetPendingLoansAsync();

                // Quick stats
                dashboard.QuickStats = await GetQuickStatsAsync();

                return dashboard;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data");
                throw;
            }
        }

        public async Task<DashboardVM> GetMemberDashboardAsync(string memberNo)
        {
            var dashboard = new DashboardVM();

            // Member-specific statistics
            var member = await _memberRepository.GetByMemberNoAsync(memberNo);
            if (member != null)
            {
                dashboard.TotalShareCapital = await _memberRepository.GetShareBalanceAsync(memberNo);

                var memberLoans = _context.Loans.Where(l => l.MemberNo == memberNo).ToList();
                dashboard.TotalLoansIssued = memberLoans.Where(l => l.Status == (int)Status.Approved).Sum(l => l.LoanAmt ?? 0);

                var memberTransactions = await _transactionRepository.GetMemberTransactionsAsync(memberNo, null, null);
                dashboard.TotalDeposits = await _transactionRepository.GetMemberTotalDepositsAsync(memberNo);
                dashboard.TotalWithdrawals = await _transactionRepository.GetMemberTotalWithdrawalsAsync(memberNo);

                // Member-specific recent activities
                dashboard.RecentTransactions = (await _context.Transactions2
                    .Where(t => t.MemberNo == memberNo)
                    .OrderByDescending(t => t.ContributionDate)
                    .Take(10)
                    .ToListAsync())
                    .Select(t => new RecentTransaction
                    {
                        TransactionId = t.TransactionNo,
                        MemberName = $"{member.Surname} {member.OtherNames}",
                        Type = t.TransactionType,
                        Amount = t.Amount,
                        Date = t.ContributionDate,
                        Status = t.Status,
                        BlockchainTxId = t.BlockchainTxId ?? "Pending"
                    }).ToList();
            }

            return dashboard;
        }

        // PRIVATE method with different name
        private async Task<List<MonthlyTransactionData>> GetMonthlyTransactionsDataAsync(int months = 6)
        {
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-months);

            var data = new List<MonthlyTransactionData>();

            for (int i = 0; i < months; i++)
            {
                var monthDate = startDate.AddMonths(i);
                var monthName = monthDate.ToString("MMM yyyy");

                var deposits = await _context.Transactions2
                    .Where(t => t.TransactionType == "DEPOSIT" &&
                               t.Status == "COMPLETED" &&
                               t.ContributionDate.Month == monthDate.Month &&
                               t.ContributionDate.Year == monthDate.Year)
                    .SumAsync(t => t.Amount);

                var withdrawals = await _context.Transactions2
                    .Where(t => t.TransactionType == "WITHDRAWAL" &&
                               t.Status == "COMPLETED" &&
                               t.ContributionDate.Month == monthDate.Month &&
                               t.ContributionDate.Year == monthDate.Year)
                    .SumAsync(t => t.Amount);

                var repayments = await _context.Repay
                    .Where(r => r.DateReceived.HasValue &&
                                r.DateReceived.Value.Month == monthDate.Month &&
                                r.DateReceived.Value.Year == monthDate.Year &&
                                r.Posted == true)
                    .SumAsync(r => r.Amount ?? 0);

                data.Add(new MonthlyTransactionData
                {
                    Month = monthName,
                    Deposits = deposits,
                    Withdrawals = withdrawals,
                    LoanRepayments = repayments
                });
            }

            return data;
        }

        // PRIVATE method with different name
        private async Task<List<MemberGrowthData>> GetMemberGrowthDataAsync(int months = 12)
        {
            var data = new List<MemberGrowthData>();
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-months);

            int cumulativeTotal = 0;

            for (int i = 0; i < months; i++)
            {
                var monthDate = startDate.AddMonths(i);
                var monthName = monthDate.ToString("MMM yyyy");

                var newMembers = await _context.Members
                    .CountAsync(m => m.EffectDate.HasValue &&
                                    m.EffectDate.Value.Month == monthDate.Month &&
                                    m.EffectDate.Value.Year == monthDate.Year);

                cumulativeTotal += newMembers;

                data.Add(new MemberGrowthData
                {
                    Period = monthName,
                    NewMembers = newMembers,
                    TotalMembers = cumulativeTotal
                });
            }

            return data;
        }

        private async Task<List<LoanTypeDistribution>> GetLoanTypeDistributionAsync()
        {
            var distribution = await _context.Loans
              .Where(l => l.Status == (int)Status.Approved) // Approved loans
              .GroupBy(l => l.LoanCode ?? "Unknown")
              .Select(g => new LoanTypeDistribution
              {
                  LoanType = g.Key ?? "Unknown",
                  Count = g.Count(),
                  TotalAmount = g.Sum(l => l.LoanAmt ?? 0)
              })
              .ToListAsync();

            // Add colors
            var colors = new[] { "#3498db", "#2ecc71", "#e74c3c", "#f39c12", "#9b59b6", "#1abc9c", "#34495e" };
            for (int i = 0; i < distribution.Count; i++)
            {
                distribution[i].Color = colors[i % colors.Length];
            }

            return distribution;
        }

        private async Task<List<ShareTypeDistribution>> GetShareTypeDistributionAsync()
        {
            var distribution = await _context.Shares
                .GroupBy(s => s.Sharescode)
                .Select(g => new ShareTypeDistribution
                {
                    ShareType = g.Key,
                    Amount = g.Sum(s => s.TotalShares ?? 0),
                    MemberCount = g.Select(s => s.MemberNo).Distinct().Count()
                })
                .ToListAsync();

            // Add colors
            var colors = new[] { "#2ecc71", "#3498db", "#e74c3c", "#f39c12", "#9b59b6", "#1abc9c" };
            for (int i = 0; i < distribution.Count; i++)
            {
                distribution[i].Color = colors[i % colors.Length];
            }

            return distribution;
        }

        private async Task<List<RecentTransaction>> GetRecentTransactionsAsync(int count = 10)
        {
            // Get transactions and join with members manually
            var recentTransactions = await _context.Transactions2
                .Where(t => t.Status == "COMPLETED")
                .OrderByDescending(t => t.ContributionDate)
                .Take(count)
                .ToListAsync();

            var result = new List<RecentTransaction>();

            foreach (var tx in recentTransactions)
            {
                // Get member details separately
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == tx.MemberNo);

                result.Add(new RecentTransaction
                {
                    TransactionId = tx.TransactionNo,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                    Type = tx.TransactionType,
                    Amount = tx.Amount,
                    Date = tx.ContributionDate,
                    Status = tx.Status,
                    BlockchainTxId = tx.BlockchainTxId ?? "Pending"
                });
            }

            return result;
        }

        private async Task<List<RecentLoan>> GetRecentLoansAsync(int count = 5)
        {
            // Get loans and join with members manually
            var recentLoans = await _context.Loans
                .Where(l => l.Status == (int)Status.Approved) // Approved loans
                .OrderByDescending(l => l.ApplicDate)
                .Take(count)
                .ToListAsync();

            var result = new List<RecentLoan>();

            foreach (var loan in recentLoans)
            {
                // Get member details separately
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo);

                result.Add(new RecentLoan
                {
                    LoanNo = loan.LoanNo,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                    Amount = loan.LoanAmt ?? 0,
                    Status = ((Status)(loan.Status ?? 0)).ToString(),
                    ApplicationDate = loan.ApplicDate
                });
            }

            return result;
        }

        private async Task<List<PendingLoan>> GetPendingLoansAsync()
        {
            // Get pending loans and join with members manually
            var pendingLoans = await _context.Loans
                .Where(l => l.Status == (int)Status.Submitted) // Pending loans
                .OrderBy(l => l.ApplicDate)
                .Take(10)
                .ToListAsync();

            var result = new List<PendingLoan>();

            foreach (var loan in pendingLoans)
            {
                // Get member details separately
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo);

                result.Add(new PendingLoan
                {
                    LoanNo = loan.LoanNo,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                    Amount = loan.LoanAmt ?? 0,
                    ApplicationDate = loan.ApplicDate,
                    DaysPending = loan.AuditDateTime.HasValue ? (DateTime.Now - loan.AuditDateTime.Value).Days : (DateTime.Now - loan.ApplicDate).Days
                });
            }

            return result;
        }

        public async Task<DashboardQuickStats> GetQuickStatsAsync()
        {
            var today = DateTime.Today;

            var stats = new DashboardQuickStats
            {
                TransactionsToday = await _context.Transactions2
                    .CountAsync(t => t.ContributionDate.Date == today && t.Status == "COMPLETED"),

                NewMembersToday = await _context.Members
                    .CountAsync(m => m.EffectDate.HasValue && m.EffectDate.Value.Date == today),

                // SIMPLER: Use decimal average directly
                AverageDeposit = await _context.Transactions2
                    .Where(t => t.TransactionType == "DEPOSIT" && t.Status == "COMPLETED")
                    .AverageAsync(t => t.Amount),

                // SIMPLER: Use decimal average directly
                AverageLoan = await _context.Loans
                     .Where(l => l.Status == (int)Status.Approved)
                     .AverageAsync(l => l.LoanAmt ?? 0),
            };

            // Calculate loan approval rate
            var totalLoans = await _context.Loans.CountAsync();
            var approvedStatuses = new[] { (int)Status.Approved, (int)Status.Endorsed, (int)Status.Disbursed };
            var approvedLoans = await _context.Loans.CountAsync(l => approvedStatuses.Contains(l.Status ?? 0));
            stats.LoanApprovalRate = totalLoans > 0 ? (approvedLoans * 100m / totalLoans) : 0;

            // Blockchain uptime (simulated - would come from actual monitoring)
            stats.BlockchainUptime = 99.9m;

            return stats;
        }

        // In DashboardService.cs, update these public methods:

        public async Task<List<MonthlyTransactionData>> GetMonthlyTransactionsAsync(int months = 6)
        {
            try
            {
                var endDate = DateTime.Now;
                var startDate = endDate.AddMonths(-months);

                var data = new List<MonthlyTransactionData>();

                for (int i = 0; i < months; i++)
                {
                    var monthDate = startDate.AddMonths(i);
                    var monthName = monthDate.ToString("MMM yyyy");

                    var deposits = await _context.Transactions2
                        .Where(t => t.TransactionType == "DEPOSIT" &&
                                   t.Status == "COMPLETED" &&
                                   t.ContributionDate.Month == monthDate.Month &&
                                   t.ContributionDate.Year == monthDate.Year)
                        .SumAsync(t => (decimal?)t.Amount) ?? 0;

                    var withdrawals = await _context.Transactions2
                        .Where(t => t.TransactionType == "WITHDRAWAL" &&
                                   t.Status == "COMPLETED" &&
                                   t.ContributionDate.Month == monthDate.Month &&
                                   t.ContributionDate.Year == monthDate.Year)
                        .SumAsync(t => (decimal?)t.Amount) ?? 0;

                    var repayments = await _context.Repay
                        .Where(r => r.AuditTime.HasValue &&
                                    r.AuditTime.Value.Month == monthDate.Month &&
                                    r.AuditTime.Value.Year == monthDate.Year &&
                                    r.Posted == true)
                        .SumAsync(r => r.Amount ?? 0);

                    data.Add(new MonthlyTransactionData
                    {
                        Month = monthName,
                        Deposits = deposits,
                        Withdrawals = withdrawals,
                        LoanRepayments = repayments
                    });
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting monthly transactions");
                return new List<MonthlyTransactionData>();
            }
        }

        public async Task<List<MemberGrowthData>> GetMemberGrowthAsync(int months = 12)
        {
            try
            {
                var data = new List<MemberGrowthData>();
                var endDate = DateTime.Now;
                var startDate = endDate.AddMonths(-months);

                for (int i = 0; i < months; i++)
                {
                    var monthDate = startDate.AddMonths(i);
                    var monthName = monthDate.ToString("MMM yyyy");

                    var newMembers = await _context.Members
                        .CountAsync(m => m.EffectDate.HasValue &&
                                        m.EffectDate.Value.Month == monthDate.Month &&
                                        m.EffectDate.Value.Year == monthDate.Year);

                    var totalMembers = await _context.Members
                        .CountAsync(m => m.EffectDate.HasValue &&
                                        m.EffectDate.Value <= monthDate);

                    data.Add(new MemberGrowthData
                    {
                        Period = monthName,
                        NewMembers = newMembers,
                        TotalMembers = totalMembers
                    });
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member growth data");
                return new List<MemberGrowthData>();
            }
        }

        public async Task<DashboardVM> GetUserGroupDashboardAsync(string userGroup, string userId = null)
        {
            var dashboard = new DashboardVM
            {
                UserGroup = userGroup
            };

            switch (userGroup.ToUpper())
            {
                case "TELLER":
                    // Teller-specific dashboard
                    dashboard.TotalTransactionsToday = await _context.Transactions2
                        .CountAsync(t => t.ContributionDate.Date == DateTime.Today);
                    dashboard.TotalDepositsToday = await _context.Transactions2
                        .Where(t => t.TransactionType == "DEPOSIT" && t.ContributionDate.Date == DateTime.Today)
                        .SumAsync(t => t.Amount);
                    dashboard.TotalWithdrawalsToday = await _context.Transactions2
                        .Where(t => t.TransactionType == "WITHDRAWAL" && t.ContributionDate.Date == DateTime.Today)
                        .SumAsync(t => t.Amount);
                    break;

                case "LOANOFFICER":
                    // Loan officer dashboard
                    dashboard.PendingLoans = await GetPendingLoansAsync();
                    dashboard.RecentLoans = await GetRecentLoansAsync();
                    dashboard.TotalLoansProcessedToday = await _context.Loans
                        .CountAsync(l => l.AuditDateTime == DateTime.Today);
                    break;

                case "AUDITOR":
                    // Auditor dashboard - focus on transactions and verification
                    dashboard.RecentTransactions = await GetRecentTransactionsAsync(20);
                    dashboard.PendingVerifications = await _context.Transactions2
                        .CountAsync(t => t.BlockchainTxId == null || t.BlockchainTxId == "Pending");
                    break;

                case "BOARDMEMBER":
                    // Board member dashboard - high-level overview
                    dashboard.TotalMembers = await _context.Members.CountAsync();
                    dashboard.TotalShareCapital = await _context.Shares.SumAsync(s => s.TotalShares ?? 0);
                    dashboard.TotalLoansIssued = await _context.Loans
                        .Where(l => l.Status == (int)Status.Approved || l.Status == (int)Status.Endorsed || l.Status == (int)Status.Disbursed)
                        .SumAsync(l => l.LoanAmt ?? 0);
                    dashboard.MonthlyTransactions = await GetMonthlyTransactionsDataAsync();
                    break;

                default:
                    // Default to member dashboard if no specific group
                    if (!string.IsNullOrEmpty(userId))
                    {
                        dashboard = await GetMemberDashboardAsync(userId);
                    }
                    break;
            }

            return dashboard;
        }
    }
}