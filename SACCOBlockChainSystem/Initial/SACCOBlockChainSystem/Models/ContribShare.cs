using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class ContribShare
{
    public int Id { get; set; }

    public int? LocalId { get; set; }

    public string MemberNo { get; set; } = null!;

    public string? LoanNo { get; set; }

    public DateTime? ContrDate { get; set; }

    public DateTime? DepositedDate { get; set; }

    public DateTime? ReceiptDate { get; set; }

    public decimal? ShareCapitalAmount { get; set; }

    public decimal? DepositsAmount { get; set; }

    public decimal? PassBookAmount { get; set; }

    public decimal? Donor { get; set; }

    public decimal? LoanAmount { get; set; }

    public decimal? RegFeeAmount { get; set; }

    public string? CompanyCode { get; set; }

    public string? ReceiptNo { get; set; }

    public string? Remarks { get; set; }

    public string? AuditId { get; set; }

    public DateTime AuditTime { get; set; }

    public string? Sharescode { get; set; }

    public string? TransactionNo { get; set; }

    public DateTime? AuditDateTime { get; set; }
    public string? BlockchainTxId { get; set; }

    public virtual Sharetype? SharescodeNavigation { get; set; }
}
