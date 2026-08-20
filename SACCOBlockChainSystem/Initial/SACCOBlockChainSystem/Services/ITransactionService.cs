using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface ITransactionService
    {
        Task<TransactionResponseDTO> ProcessDepositAsync(DepositDTO deposit);
        Task<TransactionResponseDTO> ProcessWithdrawalAsync(WithdrawalDTO withdrawal);
        Task<List<Transaction>> GetMemberTransactionsAsync(string memberNo, DateTime? fromDate, DateTime? toDate);
        Task<IEnumerable<Transactions2>> GetMemberTransactions2Async(string memberNo, DateTime? fromDate, DateTime? toDate);
        Task<Transaction> GetTransactionByIdAsync(int transactionId);
        Task<decimal> GetMemberBalanceAsync(string memberNo);
    }
}