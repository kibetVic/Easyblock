// Models/Sms.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("SmsMessages")]
    public class SmsMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Message Details
        [Required]
        [StringLength(50)]
        [Display(Name = "Message ID")]
        public string MessageId { get; set; } = null!;

        [Required]
        [StringLength(20)]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = null!;

        [Required]
        [StringLength(100)]
        [Display(Name = "Recipient Name")]
        public string RecipientName { get; set; } = null!;

        [Required]
        [StringLength(500)]
        [Display(Name = "Message Content")]
        public string MessageContent { get; set; } = null!;

        [StringLength(50)]
        [Display(Name = "Message Type")]
        public string MessageType { get; set; } = null!; // Registration, Withdrawal, Loan, Transfer, etc.

        [StringLength(50)]
        [Display(Name = "Reference")]
        public string? Reference { get; set; } // MemberNo, WithdrawalNo, LoanNo, etc.

        // Status Tracking
        [Required]
        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed, Delivered

        [StringLength(500)]
        [Display(Name = "Error Message")]
        public string? ErrorMessage { get; set; }

        // Delivery Tracking
        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public int? RetryCount { get; set; } = 0;

        // Provider Details
        [StringLength(50)]
        [Display(Name = "Provider")]
        public string? Provider { get; set; } // Africa's Talking, Twilio, etc.

        [StringLength(100)]
        [Display(Name = "Provider Message ID")]
        public string? ProviderMessageId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Cost")]
        public decimal? Cost { get; set; }

        // Company Information
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        // Audit Fields
        [StringLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Modified By")]
        public string? ModifiedBy { get; set; }

        [Display(Name = "Modified At")]
        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        [Display(Name = "Blockchain Transaction ID")]
        public string? BlockchainTxId { get; set; }
    }

    [Table("SmsTemplates")]
    public class SmsTemplate
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Template Code")]
        public string TemplateCode { get; set; } = null!; // MEMBER_REGISTRATION, WITHDRAWAL_APPROVED, etc.

        [Required]
        [StringLength(100)]
        [Display(Name = "Template Name")]
        public string TemplateName { get; set; } = null!;

        [Required]
        [StringLength(500)]
        [Display(Name = "Template Content")]
        public string TemplateContent { get; set; } = null!;

        [StringLength(200)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; }

        public bool IsActive { get; set; } = true;

        // Audit Fields
        [StringLength(100)]
        [Display(Name = "Created By")]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Modified By")]
        public string? ModifiedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }
    }

    [Table("SmsSettings")]
    public class SmsSetting
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Company Code")]
        public string CompanyCode { get; set; } = null!;

        [Required]
        [StringLength(50)]
        [Display(Name = "Provider")]
        public string Provider { get; set; } = null!; // AfricaTalking, Twilio, etc.

        [StringLength(100)]
        [Display(Name = "API Key")]
        public string? ApiKey { get; set; }

        [StringLength(100)]
        [Display(Name = "API Secret")]
        public string? ApiSecret { get; set; }

        [StringLength(50)]
        [Display(Name = "Sender ID")]
        public string? SenderId { get; set; } // JUHUDI, SACCO, etc.

        [StringLength(20)]
        [Display(Name = "Username")]
        public string? Username { get; set; }

        [StringLength(20)]
        [Display(Name = "Short Code")]
        public string? ShortCode { get; set; }

        public bool IsEnabled { get; set; } 
        public bool SendOnRegistration { get; set; }
        public bool SendOnWithdrawal { get; set; }
        public bool SendOnLoanApproval { get; set; }
        public bool SendOnShareTransfer { get; set; }
        public bool SendOnContribution { get; set; }
        public bool SendOnLoanRepayment { get; set; }
        public bool SendOnAGM{ get; set; }
        public bool SendOnDeposits { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal CostPerSms { get; set; }

        [StringLength(500)]
        public string? ApiEndpoint { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}