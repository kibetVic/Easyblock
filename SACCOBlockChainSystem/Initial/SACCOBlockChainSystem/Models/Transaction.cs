using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Transaction
{
    public string? TransactionNo { get; set; }

    public decimal Amount { get; set; }

    public DateTime TransDate { get; set; }

    public string AuditId { get; set; } = null!;

    public DateTime AuditTime { get; set; }

    public string TransDescription { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? CompanyCode { get; set; }

    public int Id { get; set; }

    public string? Channel { get; set; }

    public DateTime? AuditDateTime { get; set; }
    public String? TransactionType { get; set; }
    public String? ReceiptNo { get; set; }
    public String? BlockchainTxId { get;set; }
}
