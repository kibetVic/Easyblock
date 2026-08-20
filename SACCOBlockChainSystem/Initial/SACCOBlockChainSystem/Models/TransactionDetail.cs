using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class TransactionDetail
{
    public int Id { get; set; }

    public string CompanyCode { get; set; } = null!;

    public string? TransactionId { get; set; }

    public string? TransactionCode { get; set; }

    public int ResultCode { get; set; }

    public string? ResultMessage { get; set; }

    public decimal? Amount { get; set; }

    public string? Status { get; set; }

    public string? ConversationId { get; set; }

    public int ShortCode { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? MemberNo { get; set; }

    public string? Phone { get; set; }

    public string? OriginatorConversationId { get; set; }

    public string? MerchantRequestId { get; set; }

    public string? CheckoutRequestId { get; set; }

    public DateTime? AuditDateTime { get; set; }
}
