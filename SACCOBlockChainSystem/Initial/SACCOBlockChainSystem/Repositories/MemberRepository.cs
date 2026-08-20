using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Repositories
{
    public class MemberRepository : IMemberRepository
    {
        private readonly ApplicationDbContext _context;

        public MemberRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Member> GetByIdAsync(int id)
        {
            return await _context.Members.FindAsync(id);
        }

        public async Task<Member> GetByMemberNoAsync(string memberNo)
        {
            return await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo);
        }

        public async Task<IEnumerable<Member>> GetAllAsync()
        {
            return await _context.Members
                .Where(m => m.Status == 1)
                .OrderBy(m => m.Surname)
                .ToListAsync();
        }

        public async Task<IEnumerable<Member>> SearchAsync(string searchTerm)
        {
            return await _context.Members
                .Where(m => m.MemberNo.Contains(searchTerm) ||
                           m.Surname.Contains(searchTerm) ||
                           m.OtherNames.Contains(searchTerm) ||
                           m.Idno.Contains(searchTerm) ||
                           m.PhoneNo.Contains(searchTerm))
                .Take(50)
                .ToListAsync();
        }

        public async Task AddAsync(Member member)
        {
            await _context.Members.AddAsync(member);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Member member)
        {
            _context.Members.Update(member);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var member = await GetByIdAsync(id);
            if (member != null)
            {
                member.Status = 0; // Soft delete
                await UpdateAsync(member);
            }
        }

        public async Task<bool> MemberExistsAsync(string memberNo)
        {
            return await _context.Members.AnyAsync(m => m.MemberNo == memberNo);
        }

        public async Task<decimal> GetShareBalanceAsync(string memberNo)
        {
            var share = await _context.Shares
                .FirstOrDefaultAsync(s => s.MemberNo == memberNo);
            return share?.TotalShares ?? 0;
        }
    }
}