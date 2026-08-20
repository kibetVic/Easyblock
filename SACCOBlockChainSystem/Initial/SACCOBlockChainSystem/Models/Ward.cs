using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("Wards")]
    public class Ward
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Ward Code")]
        public string WardCode { get; set; } = null!;

        [Required]
        [StringLength(100)]
        [Display(Name = "Ward Name")]
        public string WardName { get; set; } = null!;

        [Required]
        public int SubCountyId { get; set; }

        [StringLength(100)]
        [Display(Name = "Constituency")]
        public string? Constituency { get; set; }

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

        [ForeignKey("SubCountyId")]
        public virtual SubCounty? SubCounty { get; set; }
    }
}