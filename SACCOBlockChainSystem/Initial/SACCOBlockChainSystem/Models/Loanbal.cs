using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Loanbal
{
    public int Id { get; set; }

    public string LoanNo { get; set; } = null!;

    public string LoanCode { get; set; } = null!;

    public string MemberNo { get; set; } = null!;

    public decimal Balance { get; set; }

    public decimal IntrOwed { get; set; }

    public decimal? Installments { get; set; }

    public decimal IntrOwed2 { get; set; }

    public DateTime FirstDate { get; set; }

    public decimal RepayRate { get; set; }

    public DateTime LastDate { get; set; }

    public DateTime Duedate { get; set; }

    public decimal IntrCharged { get; set; }

    public decimal Interest { get; set; }

    public string Companycode { get; set; } = null!;

    public decimal Penalty { get; set; }

    public decimal? RepayRate2 { get; set; }

    public string RepayMethod { get; set; } = null!;

    public bool Cleared { get; set; }

    public bool AutoCalc { get; set; }

    public decimal IntrAmount { get; set; }

    public int RepayPeriod { get; set; }

    public string? Remarks { get; set; }

    public string? AuditId { get; set; }

    public DateTime? AuditTime { get; set; }

    public decimal IntBalance { get; set; }

    public string? CategoryCode { get; set; }

    public decimal InterestAccrued { get; set; }

    public string? Defaulter { get; set; }

    public DateTime? Processdate { get; set; }

    public string? Receiptno { get; set; }

    public string? Cease { get; set; }

    public DateTime? Nextduedate { get; set; }

    public string? TransactionNo { get; set; }

    public string? Year { get; set; }

    public string? Month { get; set; }

    public short RepayMode { get; set; }

    public string? Gperiod { get; set; }

    public string? ApiKey { get; set; }

    public string? UserName { get; set; }

    public long? Run { get; set; }

    public string? SerialNo { get; set; }

    public DateTime? AuditDateTime { get; set; }

    public string? BlockchainTxId { get; set; }
}
