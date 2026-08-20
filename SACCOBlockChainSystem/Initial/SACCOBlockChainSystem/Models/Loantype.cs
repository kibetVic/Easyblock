using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models;

public partial class Loantype
{
    public string? LoanCode { get; set; }

    public string? LoanType1 { get; set; }

    public string? ValueChain { get; set; }

    public string? LoanProduct { get; set; }

    public string? CompanyCode { get; set; }

    public short MinimumPaidForBridging { get; set; }

    public short MinimumPaidForTopup { get; set; }

    public string LoanAcc { get; set; } = null!;

    public string? InterestAcc { get; set; }

    public string? OverpaymentAcc { get; set; }

    public string? LoanOverpaymentAcc { get; set; }

    public string? PenaltyAcc { get; set; }

    public string? SchemeCode { get; set; }

    public int? RepayPeriod { get; set; }

    public string? Interest { get; set; }

    public decimal? MaxAmount { get; set; }

    public string? Guarantor { get; set; }

    public string? AuditId { get; set; }

    public DateTime? AuditTime { get; set; }

    public bool? UseintRange { get; set; }

    public string? Accno { get; set; }

    public string? IntAccno { get; set; }

    public decimal? EarningRation { get; set; }

    public int Penalty { get; set; }

    public string? DefaultLoanno { get; set; }

    public decimal? Nssf { get; set; }

    public decimal? Bankloan { get; set; }

    public decimal? OtherDeduct { get; set; }

    public int? Priority { get; set; }

    public int? MaxLoans { get; set; }

    public string? ContraAccount { get; set; }

    public int Bridging { get; set; }

    public decimal? Processingfee { get; set; }

    public string? ContraAcc { get; set; }

    public int GracePeriod { get; set; }

    public string? Repaymethod { get; set; }

    public string? PremiumAcc { get; set; }

    public string? PremiumContraAcc { get; set; }

    public double? Bridgefees { get; set; }

    public int? Periodrepaid { get; set; }

    public string? WaitingPeriod { get; set; }

    public int Id { get; set; }

    public string AccruedAcc { get; set; } = null!;

    public string? Ppacc { get; set; }

    public short Mdtei { get; set; }

    public string Intrecovery { get; set; } = null!;

    public bool IsMain { get; set; }

    public bool? SelfGuarantee { get; set; }

    public string ReceivableAcc { get; set; } = null!;

    public string? ApiKey { get; set; }

    public string? UserName { get; set; }

    public bool? IsFimg { get; set; }

    public string? ApprovalStatus { get; set; }

    public bool? MobileLoan { get; set; }

    public DateTime? MobileCreatedOn { get; set; }

    public string? MobileCreatedBy { get; set; }

    public DateTime? AuditDateTime { get; set; }

    [NotMapped]
    public string? Insurances { get; set; }
}
