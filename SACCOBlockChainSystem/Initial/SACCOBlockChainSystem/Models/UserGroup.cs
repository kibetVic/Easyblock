using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class UserGroup
    {
        [Key]
        public int UserGroupId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? OrganizationCode { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime DateCreated { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime DateUpdated { get; set; }
    }
}