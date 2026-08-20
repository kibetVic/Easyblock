using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class ApiTable
{
    public int Id { get; set; }

    public string? CompanyCode { get; set; }

    public string? Passkey { get; set; }

    public string? ApiUser { get; set; }

    public string? ApiPassword { get; set; }

    public string? ConsumerKey { get; set; }

    public string? ConsumerSecret { get; set; }

    public int? ShortCode { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? Status { get; set; }

    public string? Country { get; set; }

    public string? Description { get; set; }

    public string? Label { get; set; }

    public int? TotalTransactions { get; set; }

    public string? Channel { get; set; }

    public string? AccountNo { get; set; }

    public DateTime? AuditDateTime { get; set; }
}
