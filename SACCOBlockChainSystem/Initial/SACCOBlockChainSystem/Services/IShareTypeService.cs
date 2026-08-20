// Services/IShareTypeService.cs
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface IShareTypeService
    {
        Task<ShareTypeResponseDTO> CreateShareTypeAsync(ShareTypeCreateDTO shareTypeDto);
        Task<ShareTypeResponseDTO> UpdateShareTypeAsync(string sharesCode, ShareTypeUpdateDTO shareTypeDto);
        Task<bool> DeleteShareTypeAsync(string sharesCode, string companyCode);
        Task<ShareTypeResponseDTO> GetShareTypeByCodeAsync(string sharesCode, string companyCode);
        Task<List<ShareTypeResponseDTO>> GetShareTypesByCompanyAsync(string companyCode);
        Task<List<ShareTypeSimpleDTO>> GetActiveShareTypesAsync(string companyCode);
        Task<List<ShareTypeResponseDTO>> SearchShareTypesAsync(string searchTerm, string companyCode);
        Task<bool> ValidateShareTypeAsync(ShareTypeCreateDTO shareTypeDto);
        Task<int> GetShareTypeUsageCountAsync(string sharesCode, string companyCode);
    }
}