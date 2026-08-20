using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class ApiTransaction
{
    public int Id { get; set; }

    public string? CompanyCode { get; set; }

    public string? ApiUser { get; set; }

    public int? ShortCode { get; set; }

    public string? TransactionCode { get; set; }

    public string? CheckoutId { get; set; }

    public string? ConversationId { get; set; }

    public decimal? Amount { get; set; }

    public string? Recipient { get; set; }

    public int? StatusCode { get; set; }

    public string? Request { get; set; }

    public string? ResultDescription { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? LoanNo { get; set; }

    public DateTime? AuditDateTime { get; set; }
}
