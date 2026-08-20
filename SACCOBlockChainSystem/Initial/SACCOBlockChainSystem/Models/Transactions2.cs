using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Transactions2
{
    public long Id { get; set; }

    public string MemberNo { get; set; } = null!;

    public string? Companycode { get; set; }

    public string TransactionNo { get; set; } = null!;

    public string ReceiptNo { get; set; } = null!;

    public string PaymentMode { get; set; } = null!;

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public DateTime ContributionDate { get; set; }

    public DateTime DepositedDate { get; set; }

    public string AuditId { get; set; } = null!;

    public DateTime AuditTime { get; set; }

    public string Status { get; set; } = null!;

    public int? RunE { get; set; }

    public string? SessionId { get; set; }

    public string? Contact { get; set; }

    public DateTime? AuditDateTime { get; set; }

    public string? BlockchainTxId { get; set; }
}
