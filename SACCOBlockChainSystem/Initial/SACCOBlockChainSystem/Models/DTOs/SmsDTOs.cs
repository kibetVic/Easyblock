// Models/DTOs/SmsDTOs.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class SmsMessageDTO
    {
        public int Id { get; set; }

        [Required]
        [Phone]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = null!;

        [Required]
        [Display(Name = "Recipient Name")]
        public string RecipientName { get; set; } = null!;

        [Required]
        [Display(Name = "Message Content")]
        public string MessageContent { get; set; } = null!;

        [Display(Name = "Message Type")]
        public string? MessageType { get; set; }

        [Display(Name = "Reference")]
        public string? Reference { get; set; }
    }

    public class SmsResponseDTO
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = null!;
        public string PhoneNumber { get; set; } = null!;
        public string RecipientName { get; set; } = null!;
        public string MessageContent { get; set; } = null!;
        public string MessageType { get; set; } = null!;
        public string? Reference { get; set; }
        public string Status { get; set; } = null!;
        public string? ErrorMessage { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public int? RetryCount { get; set; }
        public string? Provider { get; set; }
        public string? ProviderMessageId { get; set; }
        public decimal? Cost { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? BlockchainTxId { get; set; }
    }

    public class SmsTemplateDTO
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TemplateCode { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string TemplateName { get; set; } = null!;

        [Required]
        [StringLength(500)]
        public string TemplateContent { get; set; } = null!;

        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SmsSettingDTO
    {
        [Required]
        [StringLength(50)]
        public string Provider { get; set; } = null!;

        [StringLength(100)]
        public string? ApiKey { get; set; }

        [StringLength(100)]
        public string? ApiSecret { get; set; }

        [StringLength(50)]
        public string? SenderId { get; set; }

        [StringLength(20)]
        public string? Username { get; set; }

        [StringLength(20)]
        public string? ShortCode { get; set; }

        public bool IsEnabled { get; set; } = true;
        public bool SendOnRegistration { get; set; } = true;
        public bool SendOnWithdrawal { get; set; } = true;
        public bool SendOnLoanApproval { get; set; } = true;
        public bool SendOnShareTransfer { get; set; } = true;
        public bool SendOnContribution { get; set; } = true;
        public bool SendOnLoanRepayment { get; set; } = true;
        public bool SendOnAGM { get; set; } = true;
        public bool SendOnDeposits { get; set; } = true;

        [Range(0, 10)]
        public decimal CostPerSms { get; set; } = 0.50m;

        [Url]
        public string? ApiEndpoint { get; set; }
    }

    //public class SmsSettingDTO
    //{
    //    [Required]
    //    [StringLength(50)]
    //    public string Provider { get; set; } = null!;

    //    [StringLength(100)]
    //    public string? ApiKey { get; set; }

    //    [StringLength(100)]
    //    public string? ApiSecret { get; set; }

    //    [StringLength(50)]
    //    public string? SenderId { get; set; }

    //    [StringLength(20)]
    //    public string? Username { get; set; }

    //    [StringLength(20)]
    //    public string? ShortCode { get; set; }

    //    public bool IsEnabled { get; set; } = true;
    //    public bool SendOnRegistration { get; set; } = true;
    //    public bool SendOnWithdrawal { get; set; } = true;
    //    public bool SendOnLoanApproval { get; set; } = true;
    //    public bool SendOnShareTransfer { get; set; } = true;
    //    public bool SendOnContribution { get; set; } = true;

    //    [Range(0, 10)]
    //    public decimal CostPerSms { get; set; } = 0.50m;

    //    [Url]
    //    public string? ApiEndpoint { get; set; }
    //}

    public class SendSmsRequestDTO
    {
        [Required]
        [Phone]
        public string PhoneNumber { get; set; } = null!;

        [Required]
        public string Message { get; set; } = null!;

        public string? RecipientName { get; set; }
        public string? MessageType { get; set; }
        public string? Reference { get; set; }
    }

    public class BulkSmsRequestDTO
    {
        [Required]
        public List<SendSmsRequestDTO> Messages { get; set; } = new List<SendSmsRequestDTO>();
    }

    public class SmsStatisticsDTO
    {
        public int TotalSent { get; set; }
        public int TotalPending { get; set; }
        public int TotalFailed { get; set; }
        public int TotalDelivered { get; set; }
        public decimal TotalCost { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ByStatus { get; set; } = new Dictionary<string, int>();
        public List<SmsResponseDTO> RecentMessages { get; set; } = new List<SmsResponseDTO>();
    }
}