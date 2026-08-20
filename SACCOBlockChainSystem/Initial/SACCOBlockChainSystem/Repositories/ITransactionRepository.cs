using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Repositories
{
    public interface ITransactionRepository
    {
        Task<Transactions2> GetTransactionByIdAsync(int id);
        Task<IEnumerable<Transactions2>> GetMemberTransactionsAsync(string memberNo, DateTime? startDate, DateTime? endDate);
        Task<IEnumerable<Transactions2>> GetRecentTransactionsAsync(int count);
        Task AddAsync(Transactions2 transaction);
        Task UpdateAsync(Transactions2 transaction);
        Task<decimal> GetMemberTotalDepositsAsync(string memberNo);
        Task<decimal> GetMemberTotalWithdrawalsAsync(string memberNo);
    }
}