using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface IMemberService
    {
        Task<MemberResponseDTO> RegisterMemberAsync(MemberRegistrationDTO registration);
        Task<Member> GetMemberByMemberNoAsync(string memberNo);
        Task<List<Member>> SearchMembersAsync(string searchTerm);
        Task<decimal> GetMemberShareBalanceAsync(string memberNo);
        Task<bool> UpdateMemberAsync(string memberNo, Member updatedMember);
        Task<List<BlockchainTransaction>> GetMemberBlockchainHistoryAsync(string memberNo);
        Task<List<Member>> GetAllMembersAsync();
        Task<decimal> GetShareBalanceAsync(string memberNo);
        Task<MemberDTO> GetMemberDetailsAsync(string memberNo);

        Task<ContributionResponseDTO> AddContributionAsync(ContributionDTO contributionDto);
        Task<List<ContributionResponseDTO>> GetMemberContributionsAsync(string memberNo);
        Task<List<ShareTypeDTO>> GetShareTypesAsync(string companyCode);
        Task<MemberContributionHistoryDTO> GetMemberContributionHistoryAsync(string memberNo);
        Task<List<ContributionResponseDTO>> SearchContributionsAsync(DateTime? fromDate, DateTime? toDate, string? memberNo = null, string? shareType = null);
        Task<MemberResponseDTO> UpdateMemberAsync(string memberNo, MemberUpdateDTO updateDto);
        Task<decimal> GetMemberShareTypeTotalAsync(string memberNo, string shareTypeCode, string companyCode);
    }
}