using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    // Loan Application DTO
    public class LoanApplicationDTO
    {
        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        public string LoanCode { get; set; } = null!;

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Principal amount must be greater than 0")]
        public decimal PrincipalAmount { get; set; }

        [Required]
        [Range(1, 360, ErrorMessage = "Repayment period must be between 1 and 360 months")]
        public int RepayPeriod { get; set; } 

        [Required]
        public string Purpose { get; set; } = null!;

        public string? Remarks { get; set; }

        public List<GuarantorAssignmentDTO>? Guarantors { get; set; }

        public string? CreatedBy { get; set; }

        public string CompanyCode { get; set; } = null!;

        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
        public DateTime ApplicationDate { get; set; } = DateTime.Now;
    }

    // Guarantor Assignment DTO
    public class GuarantorAssignmentDTO
    {
        [Required]
        public string GuarantorMemberNo { get; set; } = null!;

        [Required]
        [Range(1, double.MaxValue)]
        public decimal GuaranteeAmount { get; set; }

        public string? Remarks { get; set; }

        public string CompanyCode { get; set; }
        public object GuarantorName { get; internal set; }
    }

    // LoanAppraisalDTO.cs
    public class LoanAppraisalDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Range(0, double.MaxValue)]
        public decimal RecommendedAmount { get; set; }

        [Range(0, 100)]
        public decimal RecommendedInterestRate { get; set; }

        [Range(1, 360)]
        public int RecommendedPeriod { get; set; }

        [Required]
        public string AppraisalDecision { get; set; } = null!;

        [Required]
        [StringLength(1000)]
        public string AppraisalNotes { get; set; } = null!;

        public string? RiskFactors { get; set; }
        public string? MitigationFactors { get; set; }

        public decimal MemberSharesValue { get; set; }
        public decimal MemberDepositsValue { get; set; }
        public decimal MonthlyIncome { get; set; }
        public decimal ExistingLoanObligations { get; set; }

        public string? AppraisedBy { get; set; }
        public string CompanyCode { get; set; } = null!;
        public decimal AppliedAmount { get; set; }

        public decimal TotalGuarantee { get; set; }
        public bool RequiresGuarantor { get; set; }
    }

    // Loan Approval DTO
    public class LoanApprovalDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Required]
        public string ApprovalStatus { get; set; } = null!;

        public decimal? ApprovedAmount { get; set; }

        public decimal? ApprovedInterestRate { get; set; }

        public int? ApprovedPeriod { get; set; }

        public string? ApprovalComments { get; set; }

        public string? RejectionReason { get; set; }

        public int ApprovalLevel { get; set; }

        public bool IsFinalApproval { get; set; }

        public string? ApprovedBy { get; set; }

        public string CompanyCode { get; set; } = null!;
    }

    public class LoanDisbursementDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Required]
        public DateTime DisbursementDate { get; set; }

        public decimal DisbursedAmount { get; set; }

        public decimal? ProcessingFee { get; set; }

        public decimal? InsuranceFee { get; set; }

        public decimal? LegalFees { get; set; }

        public decimal? OtherFees { get; set; }

        [Required]
        public string DisbursementMethod { get; set; } = null!;

        public int? BankId { get; set; }

        public string? BankName { get; set; }

        public string? BankAccountNo { get; set; }

        public string? ChequeNo { get; set; }

        public string? MobileNo { get; set; }

        public string? Remarks { get; set; }

        public string? DisbursedBy { get; set; }

        public string? AuthorizedBy { get; set; }

        public string CompanyCode { get; set; } = null!;

        public string? GlAccountNo { get; set; }
    }

    public class LoanEndorsementDTO
    {
        public string LoanNo { get; set; } = null!;
        public string CompanyCode { get; set; } = null!;
        public DateTime EndorsementDate { get; set; }
        public string? EndorsedBy { get; set; }
        public string? Remarks { get; set; }
        public List<LoanDeductionDTO> Deductions { get; set; } = new();
        public string? SourceAccountNo { get; set; }

        public decimal TotalDeductions { get; set; }
        public decimal NetDisbursementAmount { get; set; }
        public decimal GrossAmount { get; set; }
    }

    public class LoanDeductionDTO
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(50)]
        public string DeductionCode { get; set; } = null!;

        public string? DeductionName { get; set; }

        public string? GlAccountNo { get; set; }

        [StringLength(200)]
        public string? GlAccountName { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsMandatory { get; set; } = false;
        public bool IsPercentage { get; set; } = false;
        public decimal? PercentageValue { get; set; }
    }

    public class LoanEndorsementResponseDTO
    {
        public int Id { get; set; }
        public string LoanNo { get; set; } = null!;
        public string EndorsementNo { get; set; } = null!;
        public DateTime EndorsementDate { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetAmount { get; set; }
        public string Status { get; set; } = null!;
        public List<LoanDeductionDTO> Deductions { get; set; } = new();
        public string? Remarks { get; set; }
        public string? EndorsedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class LoanRepaymentDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Required]
        public string MemberNo { get; set; } = null!;

        [Required]
        public DateTime PaymentDate { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal AmountPaid { get; set; }

        [Required]
        public string PaymentMethod { get; set; } = null!;

        [Required]
        public string GlAccountNo { get; set; } = null!;

        public string? GlAccountName { get; set; }

        public string? ReferenceNo { get; set; }

        public string? Remarks { get; set; }

        public string? ReceivedBy { get; set; }

        public string CompanyCode { get; set; } = null!;
    }

    public class LoanOffsetDTO
    {
        [Required(ErrorMessage = "Loan number is required")]
        public string LoanNo { get; set; } = null!;

        [Required(ErrorMessage = "Member number is required")]
        public string MemberNo { get; set; } = null!;

        [Required(ErrorMessage = "Share type is required")]
        public string SharesCode { get; set; } = null!;

        [Required(ErrorMessage = "Amount to offset is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal AmountToOffset { get; set; }

        public string? Remarks { get; set; }
        public string? ProcessedBy { get; set; }
        public string CompanyCode { get; set; } = null!;
    }

    public class AvailableSharesDTO
    {
        public string SharesCode { get; set; } = null!;
        public string SharesType { get; set; } = null!;
        public decimal AvailableAmount { get; set; }
        public decimal TotalShares { get; set; }
        public decimal LockedForGuarantee { get; set; }
        public bool IsMainShares { get; set; }
        public bool UsedToOffset { get; set; }
        public bool Withdrawable { get; set; }
        public decimal MinAmount { get; set; }
    }

    public class LoanOffsetResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = null!;
        public string? ReceiptNo { get; set; }
        public decimal PenaltyAllocated { get; set; }
        public decimal InterestAllocated { get; set; }
        public decimal PrincipalAllocated { get; set; }
        public decimal BalanceAfter { get; set; }
        public string? BlockchainTxId { get; set; }
    }

    public class LoanSearchDTO
    {
        public string? MemberNo { get; set; }

        public string? MemberName { get; set; }

        public string? LoanNo { get; set; }

        public string? LoanStatus { get; set; }

        public string? LoanCode { get; set; }

        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public decimal? MinAmount { get; set; }

        public decimal? MaxAmount { get; set; }

        public string CompanyCode { get; set; } = null!;
    }

    // Loan Summary DTO - Updated to match Loan model
    public class LoanSummaryDTO
    {
        public string LoanNo { get; set; } = null!;
        public string MemberNo { get; set; } = null!;
        public string MemberName { get; set; } = null!;
        public string LoanType { get; set; } = null!;
        public decimal PrincipalAmount { get; set; }
        public decimal ApprovedAmount { get; set; }
        public decimal DisbursedAmount { get; set; }
        public decimal OutstandingBalance { get; set; }
        public decimal ArrearsAmount { get; set; }
        public decimal MaxLoanamt { get; set; }
        public decimal TotalGuarantee { get; set; }
        public string LoanStatus { get; set; } = null!;
        public DateTime ApplicationDate { get; set; }
        public DateTime? DisbursementDate { get; set; }
        public DateTime? MaturityDate { get; set; }
        public int DaysOverdue { get; set; }
        public decimal InterestRate { get; set; }
        public decimal MonthlyInstallment { get; set; }
        public int InstallmentsPaid { get; set; }
        public int TotalInstallments { get; set; }
        public int RequiredGuarantors { get; internal set; }
    }

    public class GuarantorResponseDTO
    {
        public int Id { get; set; }
        public string LoanNo { get; set; } = null!;
        public string GuarantorMemberNo { get; set; } = null!;
        public string GuarantorName { get; set; } = null!;
        public string? IdNo { get; set; }        
        public string? PhoneNo { get; set; }
        public decimal GuaranteeAmount { get; set; }
        public decimal AvailableShares { get; set; }
        public string Status { get; set; } = null!;
        public DateTime AssignedDate { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedBy { get; set; }
        public string? Remarks { get; set; }
    }

    public class LoanScheduleDTO
    {
        public int InstallmentNo { get; set; }
        public DateTime DueDate { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal InterestAmount { get; set; }
        public decimal TotalInstallment { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public decimal PenaltyAmount { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? PaidDate { get; set; }
        public string? OutstandingPrincipal { get; set; }
        public string? OutstandingInterest { get; set; }
        public string? OutstandingTotal { get; set; }
        public bool IsFlexible { get; set; }
        public decimal? MinimumPayment { get; set; }
    }

    // Loan Dashboard DTO - Updated to use Status enum
    public class LoanDashboardDTO
    {
        public int TotalLoans { get; set; }
        public decimal TotalLoanAmount { get; set; }
        public decimal TotalDisbursed { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal TotalRepaid { get; set; }
        public decimal TotalArrears { get; set; }
        public int PendingApplications { get; set; }
        public int UnderAppraisal { get; set; }
        public int PendingApproval { get; set; }
        public int PendingFinalApproval { get; set; }
        public int ApprovedPendingDisbursement { get; set; }
        public int ActiveLoans { get; set; }
        public int OverdueLoans { get; set; }
        public int DefaultedLoans { get; set; }
        public List<LoanSummaryDTO> RecentLoans { get; set; } = new();
        public Dictionary<string, int> LoansByStatus { get; set; } = new();
        public Dictionary<string, decimal> LoanPortfolioByType { get; set; } = new();
    }

    public class BatchGuarantorRequestDTO
    {
        [Required]
        public string LoanNo { get; set; } = null!;

        [Required]
        public List<GuarantorAssignmentDTO> Guarantors { get; set; } = new();

        public string? CreatedBy { get; set; }
        public string CompanyCode { get; set; } = null!;
    }

    public class BatchGuarantorResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = null!;
        public List<GuarantorResultDTO> Results { get; set; } = new();
        public int TotalAdded { get; set; }
        public int TotalFailed { get; set; }
    }

    public class GuarantorResultDTO
    {
        public string GuarantorMemberNo { get; set; } = null!;
        public string GuarantorName { get; set; } = null!;
        public decimal GuaranteeAmount { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class GuarantorEligibilityResponseDTO
    {
        public bool IsEligible { get; set; }
        public string? Message { get; set; }
        public GuarantorEligibilityDataDTO? Data { get; set; }
    }

    public class GuarantorEligibilityDataDTO
    {
        public string MemberNo { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string IdNo { get; set; } = null!;
        public decimal AvailableShares { get; set; }
        public decimal ExistingGuarantees { get; set; }
        public decimal MaxGuaranteeAmount { get; set; }
        public bool IsSelfGuarantee { get; set; }
    }
}