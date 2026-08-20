using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockchainDb.Models
{
    [Table("Agents")]
    public class Agent
    {
        [Key]
        [Column("IdNo")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string? IdNo { get; set; } // Primary Key (not auto-generated)

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; } // Identity column (not for replication)

        [Column("RecruitementAgents")]
        [MaxLength(50)]
        public string? RecruitementAgents { get; set; } // dropdown of Staff, agent, Agri-prenour and Board member

        [Required]
        [MaxLength(150)]
        public string? Names { get; set; }
        [Required]
        [MaxLength(50)]
        public string? Gender { get; set; }

        [Column("staffcode")]
        [MaxLength(50)]
        public string? StaffCode { get; set; }

        [MaxLength(50)]
        public string? Occupation { get; set; }

        [Required]
        [MaxLength(50)]
        public string? LandPhone { get; set; }

        [Required]
        [MaxLength(50)]
        public string? MobileNo { get; set; }

        [MaxLength(50)]
        public string? Branchname { get; set; }

        [MaxLength(50)]
        public string? CompanyCode { get; set; }

        [Required]
        [MaxLength(50)]
        public string? HomeAddress { get; set; }

        [Required]
        [MaxLength(50)]
        public string? Town { get; set; } // county

        [Required]
        public DateTime Recruitdate { get; set; }

        [Required]
        [MaxLength(10)]
        public string? AuditId { get; set; } // user logged in

        [Required]
        public DateTime AuditTime { get; set; }

        [MaxLength(100)]
        public string? PIN { get; set; }

        [MaxLength(255)]
        [Column("BlockchainTxId")]
        public string? BlockchainTransactionId { get; set; }
    }
}