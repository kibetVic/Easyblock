using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Contrib
{
    public int Id { get; set; }

    public string MemberNo { get; set; } = null!;

    public string? StaffNo { get; set; }

    public DateTime? ContrDate { get; set; }

    public DateTime? DepositedDate { get; set; }

    public DateTime? ReceiptDate { get; set; }

    public string? RefNo { get; set; }

    public decimal? Amount { get; set; }

    public decimal? ShareBal { get; set; }

    public string? TransBy { get; set; }

    public string? CompanyCode { get; set; }

    public string? ChequeNo { get; set; }

    public string? ReceiptNo { get; set; }

    public string? Locked { get; set; }

    public string? Posted { get; set; }

    public string? Remarks { get; set; }

    public string? AuditId { get; set; }

    public DateTime AuditTime { get; set; }

    public string? Schemecode { get; set; }

    public string? TransferDesc { get; set; }

    public string? MrCleared { get; set; }

    public string? Mrno { get; set; }

    public string? TransNo { get; set; }

    public bool? Offset { get; set; }

    public DateTime? TransDate { get; set; }

    public string? SharesAcc { get; set; }

    public string? ContraAcc { get; set; }

    public DateTime? CashBookdate { get; set; }

    public int? Dregard { get; set; }

    public int? Offs { get; set; }

    public string? Sharescode { get; set; }

    public string? TransactionNo { get; set; }

    public string? ApiKey { get; set; }

    public string? UserName { get; set; }

    public long? Run { get; set; }

    public int? Run2 { get; set; }

    public DateTime? AuditDateTime { get; set; }

    public string? BlockchainTxId { get; set; }

    public virtual Sharetype? SharescodeNavigation { get; set; }
    public virtual Member? MemberNoNavigation { get; set; }
}
