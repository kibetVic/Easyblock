using SACCOBlockChainSystem.Models.ViewModels;

namespace SACCOBlockChainSystem.Services
{
    public interface IDashboardService
    {
        Task<DashboardVM> GetDashboardDataAsync();
        Task<DashboardVM> GetMemberDashboardAsync(string memberNo);
        Task<DashboardVM> GetUserGroupDashboardAsync(string userGroup, string userId = null); // NEW
        Task<List<MonthlyTransactionData>> GetMonthlyTransactionsAsync(int months = 6);
        Task<List<MemberGrowthData>> GetMemberGrowthAsync(int months = 12);
        Task<DashboardQuickStats> GetQuickStatsAsync();
    }
}