using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface ILoanService
    {
        Task<bool> DeleteLoanAsync(string loanNo, string companyCode, string deletedBy, string reason);

        #region Loan Application
        Task<Loan> ApplyForLoanAsync(LoanApplicationDTO application);
        Task<(bool IsEligible, string Message, bool HasValidShares, decimal TotalEligibleShares, decimal MaxLoanAmount)> CheckMemberEligibilityWithContributionsAsync(string memberNo, string companyCode);
        Task<bool> HasActiveLoansAsync(string memberNo, string companyCode);
        Task<decimal> GetGuarantorTotalGuaranteesAsync(string memberNo, string companyCode);
        Task<(bool HasExistingLoan, string Message, List<LoanSummaryDTO> ExistingLoans)> CheckExistingLoansAsync(string memberNo, string companyCode);
        Task<DateTime?> GetLastRepaymentDateAsync(string loanNo);
        Task<Loan> GetLoanByNoAsync(string loanNo, string companyCode);
        Task<List<LoanSummaryDTO>> GetMemberLoansAsync(string memberNo, string companyCode);
        Task<List<LoanSummaryDTO>> SearchLoansAsync(LoanSearchDTO searchDto);
        Task<LoanDashboardDTO> GetLoanDashboardAsync(string companyCode);
        #endregion

        #region Guarantor Management
        Task<Loan> GetLoanByNoForDisplayAsync(string loanNo, string companyCode);
        Task<Loanguar> AssignGuarantorAsync(string loanNo, GuarantorAssignmentDTO guarantor, string assignedBy);
        Task<List<GuarantorResponseDTO>> GetLoanGuarantorsAsync(string loanNo);
        Task<bool> ReleaseGuarantorAsync(int guarantorId, string releasedBy);
        Task<bool> ValidateGuarantorEligibilityAsync(string memberNo, decimal guaranteeAmount, string companyCode);
        #endregion


        #region Collateral Guarantee Management
        Task<List<MemberCollateralDTO>> GetMemberAvailableCollateralsAsync(string memberNo, string companyCode);
        Task<ColloanGuar> AssignCollateralGuaranteeAsync(CollateralGuaranteeDTO guaranteeDto, string assignedBy);
        Task<List<CollateralGuaranteeResponseDTO>> GetLoanCollateralGuaranteesAsync(string loanNo);
        Task<bool> ReleaseCollateralGuaranteeAsync(long collateralGuaranteeId, string releasedBy, string reason);
        Task<decimal> GetTotalCollateralGuaranteeAmountAsync(string loanNo);
        Task<decimal> GetTotalGuaranteeForLoanAsync(string loanNo, string companyCode);
        Task<(bool IsValid, string Message, AvailableCollateralDTO? Data)> ValidateCollateralForLoanAsync(
            string memberNo, string colCode, string loanNo, string companyCode);

        #endregion

        #region Loan Appraisal
        Task<Appraisal> AppraiseLoanAsync(LoanAppraisalDTO appraisalDto);
        Task<Appraisal?> GetLoanAppraisalAsync(string loanNo);
        #endregion

        #region Loan Approval
        Task<Endmain> ApproveLoanAsync(LoanApprovalDTO approvalDto);
        Task<List<Endmain>> GetLoanApprovalsAsync(string loanNo);
        Task<bool> IsLoanApprovedAsync(string loanNo);
        #endregion

        #region Loan Endorsement/Deduction
        Task<Endmain> CreateEndorsementAsync(LoanEndorsementDTO endorsementDto);
        Task<Endmain> GetEndorsementByLoanNoAsync(string loanNo, string companyCode);
        Task<Endmain> GetEndorsementByMinuteNoAsync(string minuteNo, string companyCode);
        Task<List<Endmain>> GetEndorsementsByLoanNoAsync(string loanNo, string companyCode);
        Task<List<LoanDeductionDTO>> GetAvailableDeductionsAsync(string companyCode);
        Task<decimal> CalculateTotalDeductionsAsync(string loanNo, List<LoanDeductionDTO> deductions);
        Task<bool> HasEndorsementAsync(string loanNo, string companyCode);
        #endregion

        #region Disbursement
        Task<Cheque> DisburseLoanAsync(LoanDisbursementDTO disbursementDto);
        Task<Cheque> GetLoanDisbursementAsync(string loanNo);
        Task<Loanbal?> GetLoanBalanceAsync(string loanNo);
        Task<Loanbal?> GetLoanBalanceAsync(string loanNo, string companyCode);
        #endregion

        #region Schedule Generation
        Task<List<LoanSchedule>> GenerateLoanScheduleAsync(string loanNo);
        Task<List<LoanScheduleDTO>> GetLoanScheduleAsync(string loanNo);
        Task UpdateOverdueStatusesAsync(string companyCode);
        Task<LoanSchedule> GetCurrentInstallmentAsync(string loanNo);
        Task RecalculateRbalScheduleAsync(string loanNo, decimal newOutstandingBalance);
        #endregion

        #region Repayments
        Task<Repay> ProcessRepaymentAsync(LoanRepaymentDTO repaymentDto);
        Task<List<Repay>> GetLoanRepaymentsAsync(string loanNo);
        Task<Repay> ReverseRepaymentAsync(int repaymentId, string reason, string reversedBy);
        #endregion

        #region Loan Offset with Shares
        Task<List<AvailableSharesDTO>> GetAvailableSharesForOffsetAsync(string memberNo, string companyCode);
        Task<decimal> GetSharesLockedForGuaranteeAsync(string memberNo, string sharesCode, string companyCode);
        Task<LoanOffsetResponseDTO> OffsetLoanWithSharesAsync(LoanOffsetDTO offsetDto);
        #endregion

        #region State Management
        Task<bool> UpdateLoanStatusAsync(string loanNo, string newStatus, string performedBy, string? remarks = null);
        Task<bool> CanTransitionAsync(string loanNo, int targetStatus);
        #endregion

        #region Validation
        Task<(bool IsValid, string Message)> ValidateLoanApplicationAsync(LoanApplicationDTO application);
        Task<(bool IsEligible, string Message)> CheckMemberEligibilityAsync(string memberNo, string loanCode, string companyCode);
        Task<decimal> CalculateMaximumLoanAmountAsync(string memberNo, string loanCode, string companyCode);
        Task<bool> RejectGuarantorAsync(int guarantorId, string remarks, string rejectedBy);
        #endregion

        #region Audit
        Task CreateAuditTrailAsync(string loanNo, string? previousStatus, string? newStatus, string action, string description, string performedBy, string companyCode);
        Task<List<AuditTrail>> GetLoanAuditTrailAsync(string loanNo);
        //Task<decimal> GetMemberAvailableDepositsForGuaranteeAsync(string guarantorMemberNo, string companyCode);
        #endregion
    }
}