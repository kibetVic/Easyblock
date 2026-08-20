// Models/Bank.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("Banks")]
    public class Bank
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Bank Code")]
        public string BankCode { get; set; } = null!;

        [Required]
        [StringLength(200)]
        [Display(Name = "Bank Name")]
        public string BankName { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Account Number")]
        public string AccountNumber { get; set; } = null!;

        [StringLength(100)]
        [Display(Name = "Account Name")]
        public string? AccountName { get; set; }

        [StringLength(500)]
        [Display(Name = "Branch")]
        public string? Branch { get; set; }

        [StringLength(50)]
        [Display(Name = "Swift Code")]
        public string? SwiftCode { get; set; }

        [StringLength(50)]
        [Display(Name = "Sort Code")]
        public string? SortCode { get; set; }

        [Required(ErrorMessage = "GL Account Number is required for bank transactions")]
        [StringLength(30)]
        [Display(Name = "GL Account Number")]
        public string? GlAccountNo { get; set; }

        [StringLength(200)]
        [Display(Name = "GL Account Name")]
        public string? GlAccountName { get; set; }

        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        [StringLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Modified By")]
        public string? ModifiedBy { get; set; }

        [Display(Name = "Modified At")]
        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        [Display(Name = "Blockchain Transaction ID")]
        public string? BlockchainTxId { get; set; }
    }
}