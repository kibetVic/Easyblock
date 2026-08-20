using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Share
{
    public string? MemberNo { get; set; }

    public decimal? TotalShares { get; set; }

    public DateTime? TransDate { get; set; }

    public DateTime? LastDivDate { get; set; }

    public string? AuditId { get; set; }

    public DateTime? AuditTime { get; set; }

    public decimal? Loanbal { get; set; }

    public string Sharescode { get; set; } = null!;

    public decimal? Statementshares { get; set; }

    public decimal Initshares { get; set; }

    public string? CompanyCode { get; set; }

    public DateTime? AuditDateTime { get; set; }
}
