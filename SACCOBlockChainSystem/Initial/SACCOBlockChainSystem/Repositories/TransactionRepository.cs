using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Repositories
{
    public class TransactionRepository : ITransactionRepository
    {
        private readonly ApplicationDbContext _context;

        public TransactionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Transactions2> GetTransactionByIdAsync(int id)
        {
            return await _context.Transactions2.FindAsync(id);
        }

        public async Task<IEnumerable<Transactions2>> GetMemberTransactionsAsync(string memberNo, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Transactions2
                .Where(t => t.MemberNo == memberNo);

            if (startDate.HasValue)
                query = query.Where(t => t.ContributionDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(t => t.ContributionDate <= endDate.Value);

            return await query
                .OrderByDescending(t => t.ContributionDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Transactions2>> GetRecentTransactionsAsync(int count)
        {
            return await _context.Transactions2
                .OrderByDescending(t => t.ContributionDate)
                .Take(count)
                .ToListAsync();
        }

        public async Task AddAsync(Transactions2 transaction)
        {
            await _context.Transactions2.AddAsync(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Transactions2 transaction)
        {
            _context.Transactions2.Update(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task<decimal> GetMemberTotalDepositsAsync(string memberNo)
        {
            return await _context.Transactions2
                .Where(t => t.MemberNo == memberNo &&
                           t.TransactionType == "DEPOSIT" &&
                           t.Status == "COMPLETED")
                .SumAsync(t => t.Amount);
        }

        public async Task<decimal> GetMemberTotalWithdrawalsAsync(string memberNo)
        {
            return await _context.Transactions2
                .Where(t => t.MemberNo == memberNo &&
                           t.TransactionType == "WITHDRAWAL" &&
                           t.Status == "COMPLETED")
                .SumAsync(t => t.Amount);
        }
    }
}