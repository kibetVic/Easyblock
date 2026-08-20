using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Loanguar
{
    public string MemberNo { get; set; } = null!;

    public string? LoanNo { get; set; }

    public decimal? Amount { get; set; }

    public decimal? Balance { get; set; }

    public string? AuditId { get; set; }

    public DateTime? AuditTime { get; set; }

    public string? Collateral { get; set; }

    public string? Description { get; set; }

    public bool Transfered { get; set; }

    public DateTime? Transdate { get; set; }

    public string? FullNames { get; set; }

    public decimal? Tguaranto { get; set; }

    public string? CompanyCode { get; set; }

    public int Id { get; set; }

    public string? BlockchainTxId { get; set; }
}
