using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    [Table("CHEQUES")]
    public partial class Cheque
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string LoanNo { get; set; } = null!;

        public string? MemberNo { get; set; }

        public string? ChequeNo { get; set; }

        public string? CompanyCode { get; set; }

        public decimal? Amount { get; set; }

        public decimal? BalForward { get; set; }

        public decimal? ProcessingFee { get; set; }

        public decimal? IntAmount { get; set; }

        public decimal IntrOwed { get; set; }

        public string? CollectorId { get; set; }

        public string? CollectorName { get; set; }

        public DateTime? DateIssued { get; set; }

        public string? ClerkStaffNo { get; set; }

        public string? ClerkName { get; set; }

        public string? Status { get; set; }

        public string? Reasons { get; set; }

        public string? AuditId { get; set; }

        public DateTime? AuditTime { get; set; }

        public string? Remarks { get; set; }

        public decimal? AmountIssued { get; set; }

        public DateTime? Firstdate { get; set; }

        public decimal? Premium { get; set; }

        public decimal Offsetamount { get; set; }

        public string Amountinword { get; set; } = null!;

        public string Voucherno { get; set; } = null!;

        public decimal Voucheramount { get; set; }

        public bool Refloan { get; set; }

        public string Paymethod { get; set; } = null!;

        public decimal? Balance { get; set; }

        public string TransactionNo { get; set; } = null!;

        public decimal OrgAmt { get; set; }

        public string LoanAcc { get; set; } = null!;

        public string ContraAcc { get; set; } = null!;

        public string PremiumAcc { get; set; } = null!;

        public int Dregard { get; set; }

        public decimal PaidBf { get; set; }

        public string? ApiKey { get; set; }

        public string? UserName { get; set; }

        public string? SerialNo { get; set; }

        public DateTime? AuditDateTime { get; set; }

        public string? BlockchainTxId { get; set; }
    }
}

