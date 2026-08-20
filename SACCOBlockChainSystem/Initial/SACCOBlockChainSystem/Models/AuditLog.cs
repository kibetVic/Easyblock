using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class AuditLog
    {
        [Key]
        public int AuditLogId { get; set; }

        [Required]
        [StringLength(100)]
        public string TableName { get; set; } = null!;

        [StringLength(50)]
        public string? RecordId { get; set; }

        [Required]
        [StringLength(50)]
        public string Action { get; set; } = null!; // "INSERT", "UPDATE", "DELETE"

        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        [StringLength(100)]
        public string? UserId { get; set; }

        [StringLength(100)]
        public string? UserName { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? IPAddress { get; set; }

        [StringLength(255)]
        public string? UserAgent { get; set; }
    }
}