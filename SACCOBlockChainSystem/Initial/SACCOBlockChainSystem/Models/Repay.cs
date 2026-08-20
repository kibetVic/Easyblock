using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models;

public partial class Repay
{
    public int Id { get; set; }

    public string LoanNo { get; set; } = null!;

    public string? MemberNo { get; set; }

    public string? CompanyCode { get; set; }

    public string? SerialNo { get; set; }

    public DateTime? DateReceived { get; set; }

    public int? PaymentNo { get; set; }

    public decimal? Amount { get; set; }

    public decimal? Principal { get; set; }  // appraise amount

    public decimal? Interest { get; set; } //  interest charged per payment

    public decimal? IntrCharged { get; set; }

    public decimal? IntrOwed { get; set; }

    public decimal? IntrAccrued { get; set; }

    public decimal? Penalty { get; set; } // if penalty is 

    public decimal? LoanBalance { get; set; } // loan plus interest after each payment

    public string? ReceiptNo { get; set; } // generate

    public string? Chequeno { get; set; } //generate 

    public decimal? RepayRate { get; set; } 

    public bool? Locked { get; set; }

    public bool? Posted { get; set; }

    public bool? Accrued { get; set; }

    public string? Remarks { get; set; }

    public string? AuditId { get; set; }

    public string? Ch { get; set; }

    public DateTime? Nextduedate { get; set; }

    public DateTime? AuditTime { get; set; }

    public long? RepayId { get; set; }

    public string? Transby { get; set; }

    public decimal? IntBalance { get; set; }

    public string? Loancode { get; set; }

    public decimal? Interestaccrued { get; set; }

    public string? Mrno { get; set; }

    public string? Mrcleared { get; set; }

    public string? Transno { get; set; }

    public DateTime? TransDate { get; set; }

    public decimal? BridgeInterest { get; set; }

    public string? BrgLoan { get; set; }

    public DateTime? Statementdate { get; set; }

    public string? LoanAcc { get; set; }

    public string? InterestAcc { get; set; }

    public string? ContraAcc { get; set; }

    public int? Cash { get; set; }

    public DateTime? CashBookDate { get; set; }

    public int Dregard { get; set; }

    public int Offs { get; set; }

    public string? TransactionNo { get; set; }

    public string? ApiKey { get; set; }

    public string? UserName { get; set; }
    public long? Run { get; set; }
    public DateTime? AuditDateTime { get; set; }
    public string? BlockchainTxId { get; set; }
}
