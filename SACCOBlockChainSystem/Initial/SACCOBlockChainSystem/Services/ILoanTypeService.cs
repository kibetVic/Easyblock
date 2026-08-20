using SACCOBlockChainSystem.Models.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface ILoanTypeService
    {
        // Basic CRUD operations
        Task<LoanTypeResponseDTO> CreateLoanTypeAsync(LoanTypeCreateDTO loanTypeDto);
        Task<LoanTypeResponseDTO> UpdateLoanTypeAsync(string loanCode, LoanTypeUpdateDTO loanTypeDto);
        Task<bool> DeleteLoanTypeAsync(string loanCode, string companyCode);
        Task<LoanTypeResponseDTO> ApproveLoanTypeAsync(string loanCode, string companyCode, string approvedBy);

        // Retrieval operations
        Task<LoanTypeResponseDTO> GetLoanTypeByCodeAsync(string loanCode, string companyCode);
        Task<List<LoanTypeResponseDTO>> GetLoanTypesByCompanyAsync(string companyCode);
        Task<List<LoanTypeSimpleDTO>> GetActiveLoanTypesAsync(string companyCode);
        Task<List<LoanTypeResponseDTO>> SearchLoanTypesAsync(string searchTerm, string companyCode);
        Task<dynamic> GetAllLoanTypesAsync(string companyCode);
        Task<bool> ValidateLoanTypeAsync(LoanTypeCreateDTO loanTypeDto);
        Task<int> GetLoanTypeUsageCountAsync(string loanCode, string companyCode);
        Task<List<LoanTypeSimpleDTO>> GetLoanTypesForMemberAsync(string memberNo, string companyCode);
        Task<LoanTypeStatisticsDTO> GetLoanTypeStatisticsAsync(string companyCode);

        // Repayment Calculation Methods
        Task<RepaymentScheduleDTO> CalculateRepaymentScheduleAsync(
            string loanCode,
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod,
            string companyCode); 

        Task<decimal> CalculateMonthlyPaymentAsync(
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod);

        Task<decimal> CalculateOutstandingBalanceAsync(
            decimal principal,
            int termMonths,
            decimal annualInterestRate,
            string repaymentMethod,
            int monthsPaid);

        Task<decimal> CalculateInterestForPeriodAsync(
        decimal outstandingBalance,
        decimal annualInterestRate,
        int days);
    }
}