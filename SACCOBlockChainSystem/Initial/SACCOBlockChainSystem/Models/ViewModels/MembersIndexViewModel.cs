// Models/ViewModels/MembersIndexViewModel.cs
using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class MembersIndexViewModel
    {
        public List<Member> Members { get; set; } = new List<Member>();
        public List<Member> AllMembers { get; set; } = new List<Member>();
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public decimal TotalShareCapital { get; set; }
        public List<BlockchainTransaction> MemberTransactions { get; set; } = new List<BlockchainTransaction>();
        public string? UserCompanyCode { get; internal set; }
        public int BlockchainVerifiedCount { get; set; }
    }
}