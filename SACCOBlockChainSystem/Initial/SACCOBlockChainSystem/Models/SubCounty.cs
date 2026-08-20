using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("SubCounties")]
    public class SubCounty
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "SubCounty Code")]
        public string SubCountyCode { get; set; } = null!;

        [Required]
        [StringLength(100)]
        [Display(Name = "SubCounty Name")]
        public string SubCountyName { get; set; } = null!;

        [Required]
        public int CountyId { get; set; }

        [StringLength(100)]
        [Display(Name = "Headquarters")]
        public string? Headquarters { get; set; }

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

        // Navigation Properties
        [ForeignKey("CountyId")]  
        public virtual County? County { get; set; }

        public virtual ICollection<Ward> Wards { get; set; } = new List<Ward>();
    }
}