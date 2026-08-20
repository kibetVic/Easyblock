// Models/DTOs/AuditTrailDTO.cs
using System;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class AuditTrailDTO
    {
        public long Id { get; set; }
        public string? CompanyCode { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string? ActionDescription { get; set; }
        public string? TableName { get; set; }
        public string? RecordId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? IpAddress { get; set; }
        public string? BrowserAgent { get; set; }
        public DateTime? AuditTime { get; set; }
        public string? CorrelationId { get; set; }
        public string? Module { get; set; }
        public string? ExtraData { get; set; }
        public string? BlockchainTxId { get; set; }
    }
}