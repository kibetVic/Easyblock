using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface IShareService
    {
        Task<SharePurchaseResponseDTO> PurchaseSharesAsync(SharePurchaseDTO purchase);
        Task<List<Share>> GetMemberSharesAsync(string memberNo);
        Task<decimal> GetTotalSharesValueAsync(string memberNo);
        Task<bool> TransferSharesAsync(ShareTransferDTO transfer);
        Task<DividendDistributionResponseDTO> DistributeDividendsAsync(DividendDistributionDTO distribution);
    }
}