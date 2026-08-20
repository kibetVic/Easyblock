using SACCOBlockChainSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface IGlAccountService
    {
        Task<List<GlSetup>> GetShareGlAccountsAsync(string companyCode);
        Task<GlSetup> GetGlAccountByCodeAsync(string accountCode, string companyCode);
    }
}