using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class MemberNumberCounter
    {
        [Key]
        [StringLength(10)]
        public string CompanyCode { get; set; } = null!;

        public int LastNumber { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
