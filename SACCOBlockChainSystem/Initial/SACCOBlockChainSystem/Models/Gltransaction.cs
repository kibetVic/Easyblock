using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models;

public partial class Gltransaction
{
    public long Id { get; set; }

    public DateTime TransDate { get; set; }

    public decimal Amount { get; set; }

    public string DrAccNo { get; set; } = null!;

    public string CrAccNo { get; set; } = null!;

    public string Temp { get; set; } = null!;

    public string DocumentNo { get; set; } = null!;

    public string Source { get; set; } = null!;

    public string? CompanyCode { get; set; }

    public string TransDescript { get; set; } = null!;

    public DateTime AuditTime { get; set; }

    public string AuditId { get; set; } = null!;

    public int Cash { get; set; }

    public int DocPosted { get; set; }

    public string? ChequeNo { get; set; }

    public bool? Dregard { get; set; }

    public bool? Recon { get; set; }

    public string? TransactionNo { get; set; }

    public string Module { get; set; } = null!;

    public int ReconId { get; set; }

    public DateTime? AuditDateTime { get; set; }

    [StringLength(255)]
    public string? BlockchainTxId { get; set; }
}
