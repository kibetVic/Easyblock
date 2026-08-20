using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
	[Table("AuditTrail")]
	public class AuditTrail
	{
		[Key]
		public long AuditId { get; set; }

		[StringLength(50)]
		public string? CompanyCode { get; set; }

		[Required]
		[StringLength(50)]
		public string UserId { get; set; } = string.Empty;

		[StringLength(150)]
		public string? UserName { get; set; }

		[Required]
		[StringLength(50)]
		public string ActionType { get; set; } = string.Empty;

		public string? ActionDescription { get; set; }

		[StringLength(100)]
		public string? TableName { get; set; }

		[StringLength(100)]
		public string? RecordId { get; set; }

		public string? OldValue { get; set; }

		public string? NewValue { get; set; }

		[StringLength(45)]
		public string? IpAddress { get; set; }

		public string? BrowserAgent { get; set; }

		public DateTime? AuditTime { get; set; }

		[StringLength(100)]
		public string? CorrelationId { get; set; }

		[StringLength(100)]
		public string? Module { get; set; }

		public string? ExtraData { get; set; }

		[StringLength(255)]
		public string? BlockchainTxId { get; set; }

		// Add these if you need HostName and Location storage
		[StringLength(255)]
		public string? HostName { get; set; }

		[StringLength(255)]
		public string? Location { get; set; }
	}
}