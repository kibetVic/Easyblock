namespace SACCOBlockChainSystem.Models
{
    public class RolePrivilage
    {
        public int UserGroupId { get; set; }
        public UserGroup? UserGroup { get; set; }

        public int PrivilageId { get; set; }
        public Privillage? Privilage { get; set; }
    }
}