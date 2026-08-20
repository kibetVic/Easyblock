using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class WicciClient
{
    public int Id { get; set; }

    public string? Surname { get; set; }

    public string? Othername { get; set; }

    public string? Idno { get; set; }

    public string? PhoneNo { get; set; }

    public string? Pin { get; set; }

    public string? PinStatus { get; set; }

    public string? SecretWord { get; set; }

    public int? Unsubscribe { get; set; }

    public string? UserId { get; set; }

    public string? AuditTime { get; set; }

    public string? Acs { get; set; }

    public string? CompanyCode { get; set; }

    public string? Subscription { get; set; }

    public string UserId1 { get; set; } = null!;

    public string? Email { get; set; }

    public string? UserRole { get; set; }

    public string? Username { get; set; }
}
