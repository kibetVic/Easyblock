using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Services
{
    public interface IBlockchainService
    {
        Task<string> GenerateTransactionHash(object data);
        Task<Block> CreateBlock(List<BlockchainTransaction> transactions, string previousHash);
        //Task<BlockchainTransaction> CreateTransaction(string type, string memberNo, string companyCode,
        //    decimal amount, string offChainRefId, object data);
        Task<bool> VerifyTransaction(string transactionId);
        Task<List<BlockchainTransaction>> GetMemberTransactions(string memberNo, string currentCompanyCode);
        Task<BlockchainTransaction> AddToBlockchain(BlockchainTransaction transaction);
        Task<int> ProcessPendingTransactionsAsync();
        Task<BlockchainTransaction?> GetTransactionAsync(string transactionId);
        //Task<BlockchainTransaction> CreateAndAddTransactionAsync(string type, string memberNo, string companyCode,
        //    decimal amount, string offChainRefId, object data);
        Task<BlockchainStatus> GetBlockchainStatus();
        Task<Block> CreateGenesisBlock();
        Task<int> GetBlockchainHeight();
        Task<Block?> GetBlockByHash(string blockHash);
        Task<List<Block>> GetAllBlocks();
        Task<bool> ValidateBlockchain();
        Task<Block> GetBlockAsync(string blockHash);
        Task<bool> VerifyTransactionAsync(string transactionId);
        Task<List<Block>> GetAllBlocksAsync();
        Task<bool> VerifyBlockchainAsync();
        Task<BlockchainTransaction> CreateAndAddTransactionAsync(
            string transactionType,
            string memberNo,
            string companyCode,
            decimal amount,
            string reference,
            object data);

        Task<BlockchainTransaction> CreateTransaction(
            string transactionType,
            string memberNo,
            string companyCode,
            decimal amount,
            string reference,
            object data);

        Task<List<BlockchainTransaction>> GetMemberTransactions(string memberNo);
    }
}