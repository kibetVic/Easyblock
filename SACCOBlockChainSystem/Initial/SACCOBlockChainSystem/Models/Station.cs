using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("Counties")]
    public class County
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(10)]
        [Display(Name = "County Code")]
        public string CountyCode { get; set; } = null!;

        [Required]
        [StringLength(100)]
        [Display(Name = "County Name")]
        public string CountyName { get; set; } = null!;

        [StringLength(100)]
        [Display(Name = "County Headquarters")]
        public string? Headquarters { get; set; }

        [StringLength(100)]
        [Display(Name = "Region")]
        public string? Region { get; set; }

        [StringLength(20)]
        [Display(Name = "Status")]
        public string? Status { get; set; } = "Active";

        [StringLength(50)]
        public string? CreatedBy { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string? ModifiedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
        public virtual ICollection<SubCounty> SubCounties { get; set; } = new List<SubCounty>();
    }
}