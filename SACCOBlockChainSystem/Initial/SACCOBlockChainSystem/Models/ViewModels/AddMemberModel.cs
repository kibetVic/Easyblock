namespace SACCOBlockChainSystem.Models
{
    public class AddMemberModel
    {
        public int MemberId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
