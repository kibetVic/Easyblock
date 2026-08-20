using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models;

public partial class GeneralLedger
{
    public int Id { get; set; }

    public DateTime? Transdate { get; set; }

    public string? Source { get; set; }

    public decimal? Debits { get; set; }

    public decimal? Credits { get; set; }

    public decimal? AccBal { get; set; }

    public string? Chequeno { get; set; }

    public string? Description { get; set; }

    public string? Glname { get; set; }

    public string? CompanyCode { get; set; }

    public DateTime? AuditDateTime { get; set; }

    [StringLength(255)]
    public string? BlockchainTxId { get; set; }
}
