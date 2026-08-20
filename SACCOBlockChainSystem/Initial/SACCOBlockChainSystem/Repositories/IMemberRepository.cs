using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Repositories
{
    public interface IMemberRepository
    {
        Task<Member> GetByIdAsync(int id);
        Task<Member> GetByMemberNoAsync(string memberNo);
        Task<IEnumerable<Member>> GetAllAsync();
        Task<IEnumerable<Member>> SearchAsync(string searchTerm);
        Task AddAsync(Member member);
        Task UpdateAsync(Member member);
        Task DeleteAsync(int id);
        Task<bool> MemberExistsAsync(string memberNo);
        Task<decimal> GetShareBalanceAsync(string memberNo);
    }
}