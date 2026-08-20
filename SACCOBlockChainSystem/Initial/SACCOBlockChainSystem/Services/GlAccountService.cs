using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public class GlAccountService : IGlAccountService
    {
        private readonly ApplicationDbContext _context;

        public GlAccountService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<GlSetup>> GetShareGlAccountsAsync(string companyCode)
        {
            if (string.IsNullOrEmpty(companyCode))
                return new List<GlSetup>();

            return await _context.GlSetup
                .Where(g => g.CompanyCode == companyCode)
                .OrderBy(g => g.AccNo)
                .ToListAsync();
        }

        public async Task<GlSetup> GetGlAccountByCodeAsync(string accountCode, string companyCode)
        {
            return await _context.GlSetup
                .FirstOrDefaultAsync(x => x.AccNo == accountCode && x.Status == true);
        }
    }
}