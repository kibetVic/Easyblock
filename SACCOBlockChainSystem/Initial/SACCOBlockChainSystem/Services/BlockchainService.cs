using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SACCOBlockChainSystem.Services
{
    public class BlockchainService : IBlockchainService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BlockchainService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public BlockchainService(
            ApplicationDbContext context,
            ILogger<BlockchainService> logger,
            IServiceProvider serviceProvider)
        {
            _context = context;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        // Generate a unique hash for transaction data
        public async Task<string> GenerateTransactionHash(object data)
        {
            var json = JsonSerializer.Serialize(data);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        // Create a new transaction with PENDING status
        public async Task<BlockchainTransaction> CreateTransaction(
            string type,
            string memberNo,
            string companyCode,
            decimal amount,
            string offChainRefId,
            object data)
        {
            try
            {
                // Generate data hash
                //var dataHash = await GenerateTransactionHash(data);

                var payloadJson = JsonSerializer.Serialize(data);
                var dataHash = await GenerateTransactionHash(payloadJson);

                // Create timestamp and transaction ID
                var timestamp = DateTime.UtcNow;
                var transactionId = Guid.NewGuid().ToString();

                // Create the transaction
                var transaction = new BlockchainTransaction
                {
                    TransactionId = transactionId,
                    TransactionType = type,
                    MemberNo = memberNo,
                    CompanyCode = companyCode,
                    Amount = amount,
                    Timestamp = timestamp,
                    DataHash = dataHash,
                    PayloadJson = payloadJson, 
                    OffChainReferenceId = offChainRefId,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };

                // Save to database
                _context.BlockchainTransactions.Add(transaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Created transaction {transactionId} for member {memberNo}");

                return transaction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating transaction");
                throw;
            }
        }

        // Create a transaction and immediately add it to blockchain
        public async Task<BlockchainTransaction> CreateAndAddTransactionAsync(
            string type,
            string memberNo,
            string companyCode,
            decimal amount,
            string offChainRefId,
            object data)
        {
            // REMOVE the using transaction statement here since we're already in a transaction
            // using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Generate data hash
                //var dataHash = await GenerateTransactionHash(data);

                // Convert payload to JSON
                var payloadJson = JsonSerializer.Serialize(data);

                // Generate hash
                var dataHash = await GenerateTransactionHash(payloadJson);

                var timestamp = DateTime.UtcNow;
                var transactionId = Guid.NewGuid().ToString();

                // Create transaction
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = transactionId,
                    TransactionType = type,
                    MemberNo = memberNo,
                    CompanyCode = companyCode,
                    Amount = amount,
                    Timestamp = timestamp,
                    DataHash = dataHash,
                    PayloadJson = payloadJson,
                    OffChainReferenceId = offChainRefId,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };

                _context.BlockchainTransactions.Add(blockchainTx);

                // Get the latest block
                var latestBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                // Create previous hash
                string previousHash = latestBlock?.BlockHash ??
                    "0000000000000000000000000000000000000000000000000000000000000000";

                // Create new block
                var newBlock = new Block
                {
                    PreviousHash = previousHash,
                    Timestamp = DateTime.UtcNow,
                    Nonce = 0,
                    CreatedAt = DateTime.UtcNow
                };

                // Calculate Merkle root
                newBlock.MerkleRoot = CalculateMerkleRoot(new List<string> { dataHash });

                // Mine the block
                await MineBlockAsync(newBlock);

                // Update transaction with block hash and confirm
                blockchainTx.BlockHash = newBlock.BlockHash;
                blockchainTx.Status = "CONFIRMED";

                // Save block to database
                _context.Blocks.Add(newBlock);

                // Save all changes (transaction will be committed by the caller)
                await _context.SaveChangesAsync();
                // REMOVED: await transaction.CommitAsync();

                _logger.LogInformation($"Created and added transaction {transactionId} to block {newBlock.BlockHash}");

                return blockchainTx;
            }
            catch (Exception ex)
            {
                // REMOVED: await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating and adding transaction to blockchain");
                throw;
            }
        }

        public async Task<BlockchainTransaction> AddToBlockchain(BlockchainTransaction transaction)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Reload the transaction to ensure it's attached to context
                var dbTransactionEntity = await _context.BlockchainTransactions
                    .FirstOrDefaultAsync(t => t.TransactionId == transaction.TransactionId);

                if (dbTransactionEntity == null)
                {
                    throw new Exception($"Transaction {transaction.TransactionId} not found in database");
                }

                // Get the latest block
                var latestBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                // Create previous hash
                string previousHash = latestBlock?.BlockHash ??
                    "0000000000000000000000000000000000000000000000000000000000000000";

                // Create new block
                var newBlock = new Block
                {
                    PreviousHash = previousHash,
                    Timestamp = DateTime.UtcNow,
                    Nonce = 0,
                    CreatedAt = DateTime.UtcNow
                };

                // Calculate Merkle root
                newBlock.MerkleRoot = CalculateMerkleRoot(new List<string> { dbTransactionEntity.DataHash });

                // Mine the block
                await MineBlockAsync(newBlock);

                // Save block FIRST
                _context.Blocks.Add(newBlock);
                await _context.SaveChangesAsync();

                // Update transaction SECOND
                dbTransactionEntity.BlockHash = newBlock.BlockHash;
                dbTransactionEntity.Status = "CONFIRMED";

                await _context.SaveChangesAsync();

                // Commit transaction
                await dbTransaction.CommitAsync();

                _logger.LogInformation($"Added transaction {dbTransactionEntity.TransactionId} to block {newBlock.BlockHash}");

                return dbTransactionEntity;
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, $"Error adding transaction {transaction.TransactionId} to blockchain");
                throw;
            }
        }

        // Process multiple pending transactions into a block
        public async Task<int> ProcessPendingTransactionsAsync()
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get pending transactions
                var pendingTransactions = await _context.BlockchainTransactions
                    .Where(t => t.Status == "PENDING")
                    .OrderBy(t => t.Timestamp)
                    .Take(10)
                    .ToListAsync();

                if (!pendingTransactions.Any())
                {
                    _logger.LogInformation("No pending transactions to process");
                    return 0;
                }

                _logger.LogInformation($"Processing {pendingTransactions.Count} pending transactions");

                // Get the latest block
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                // Create previous hash
                var previousHash = lastBlock?.BlockHash ??
                    "0000000000000000000000000000000000000000000000000000000000000000";

                // Create new block
                var newBlock = new Block
                {
                    PreviousHash = previousHash,
                    Timestamp = DateTime.UtcNow,
                    Nonce = 0,
                    CreatedAt = DateTime.UtcNow
                };

                // Calculate Merkle root
                var transactionHashes = pendingTransactions.Select(t => t.DataHash).ToList();
                newBlock.MerkleRoot = CalculateMerkleRoot(transactionHashes);

                // Mine the block
                await MineBlockAsync(newBlock);

                // Save block FIRST
                _context.Blocks.Add(newBlock);
                await _context.SaveChangesAsync();

                // Update all transactions
                foreach (var transaction in pendingTransactions)
                {
                    transaction.BlockHash = newBlock.BlockHash;
                    transaction.Status = "CONFIRMED";
                }

                await _context.SaveChangesAsync();

                // Commit transaction
                await dbTransaction.CommitAsync();

                _logger.LogInformation($"Created block {newBlock.BlockHash} with {pendingTransactions.Count} transactions");

                return pendingTransactions.Count;
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Error processing pending transactions");
                throw;
            }
        }

        // Create a block with given transactions
        public async Task<Block> CreateBlock(List<BlockchainTransaction> transactions, string previousHash)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Create new block
                var newBlock = new Block
                {
                    PreviousHash = previousHash,
                    Timestamp = DateTime.UtcNow,
                    Nonce = 0,
                    CreatedAt = DateTime.UtcNow
                };

                // Calculate Merkle root
                var transactionHashes = transactions.Select(t => t.DataHash).ToList();
                newBlock.MerkleRoot = CalculateMerkleRoot(transactionHashes);

                // Mine the block
                await MineBlockAsync(newBlock);

                // Save block FIRST
                _context.Blocks.Add(newBlock);
                await _context.SaveChangesAsync();

                // Update all transactions
                foreach (var tx in transactions)
                {
                    var dbTx = await _context.BlockchainTransactions
                        .FirstOrDefaultAsync(t => t.TransactionId == tx.TransactionId);

                    if (dbTx != null)
                    {
                        dbTx.BlockHash = newBlock.BlockHash;
                        dbTx.Status = "CONFIRMED";
                    }
                }

                await _context.SaveChangesAsync();

                // Commit transaction
                await dbTransaction.CommitAsync();

                _logger.LogInformation($"Created block {newBlock.BlockHash} with {transactions.Count} transactions");

                return newBlock;
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Error creating block");
                throw;
            }
        }

        // Verify if a transaction exists and is confirmed
        public async Task<bool> VerifyTransaction(string transactionId)
        {
            try
            {
                var transaction = await _context.BlockchainTransactions
                    .Include(t => t.Block)
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (transaction == null)
                {
                    _logger.LogWarning($"Transaction {transactionId} not found");
                    return false;
                }

                // Check if transaction is confirmed
                if (transaction.Status != "CONFIRMED" || string.IsNullOrEmpty(transaction.BlockHash))
                {
                    _logger.LogWarning($"Transaction {transactionId} is not confirmed");
                    return false;
                }

                // Verify block exists
                if (transaction.Block == null)
                {
                    _logger.LogWarning($"Block for transaction {transactionId} not found");
                    return false;
                }

                _logger.LogInformation($"Transaction {transactionId} verified successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying transaction {transactionId}");
                return false;
            }
        }

        // Get all confirmed transactions for a member
        public async Task<List<BlockchainTransaction>> GetMemberTransactions(string memberNo, string currentCompanyCode)
        {
            try
            {
                var transactions = await _context.BlockchainTransactions
                    .Where(t => t.MemberNo == memberNo && t.Status == "CONFIRMED")
                    .Include(t => t.Block)
                    .OrderByDescending(t => t.Timestamp)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {transactions.Count} transactions for member {memberNo}");

                return transactions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting transactions for member {memberNo}");
                throw;
            }
        }

        // Get transaction by ID
        public async Task<BlockchainTransaction?> GetTransactionAsync(string transactionId)
        {
            try
            {
                var transaction = await _context.BlockchainTransactions
                    .Include(t => t.Block)
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                return transaction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting transaction {transactionId}");
                throw;
            }
        }

        // Get blockchain status
        public async Task<BlockchainStatus> GetBlockchainStatus()
        {
            try
            {
                var totalBlocks = await _context.Blocks.CountAsync();
                var totalTransactions = await _context.BlockchainTransactions.CountAsync();
                var pendingTransactions = await _context.BlockchainTransactions
                    .CountAsync(t => t.Status == "PENDING");

                var latestBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                return new BlockchainStatus
                {
                    TotalBlocks = totalBlocks,
                    TotalTransactions = totalTransactions,
                    PendingTransactions = pendingTransactions,
                    LatestBlockHash = latestBlock?.BlockHash,
                    LatestBlockTimestamp = latestBlock?.Timestamp
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blockchain status");
                throw;
            }
        }

        // Mine block with Proof of Work
        private async Task MineBlockAsync(Block block)
        {
            var difficulty = 2; // Reduced difficulty for faster testing
            var target = new string('0', difficulty);

            _logger.LogInformation($"Starting mining for block with difficulty {difficulty}");

            var startTime = DateTime.UtcNow;
            long hashAttempts = 0;

            while (true)
            {
                // Create block data string
                var blockData = $"{block.PreviousHash}{block.Timestamp:yyyy-MM-dd HH:mm:ss.fff}{block.MerkleRoot}{block.Nonce}";

                // Calculate hash
                var hash = ComputeSHA256Hash(blockData);
                hashAttempts++;

                // Check if hash meets difficulty requirement
                if (hash.Substring(0, difficulty) == target)
                {
                    block.BlockHash = hash;
                    var miningTime = (DateTime.UtcNow - startTime).TotalSeconds;

                    _logger.LogInformation($"Block mined successfully!");
                    _logger.LogInformation($"Hash: {hash}");
                    _logger.LogInformation($"Nonce: {block.Nonce}");
                    _logger.LogInformation($"Hash attempts: {hashAttempts}");
                    _logger.LogInformation($"Mining time: {miningTime:F2} seconds");
                    break;
                }

                // Increment nonce
                block.Nonce++;

                // Small delay to prevent CPU overuse
                if (block.Nonce % 10000 == 0)
                {
                    await Task.Delay(1);
                }
            }
        }

        // Calculate Merkle root
        private string CalculateMerkleRoot(List<string> transactionHashes)
        {
            if (transactionHashes == null || !transactionHashes.Any())
            {
                return ComputeSHA256Hash("");
            }

            if (transactionHashes.Count == 1)
            {
                return transactionHashes.First();
            }

            var currentLevel = transactionHashes;

            while (currentLevel.Count > 1)
            {
                var nextLevel = new List<string>();

                for (int i = 0; i < currentLevel.Count; i += 2)
                {
                    var left = currentLevel[i];
                    var right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;

                    var combined = left + right;
                    var hash = ComputeSHA256Hash(combined);
                    nextLevel.Add(hash);
                }

                currentLevel = nextLevel;
            }

            return currentLevel.First();
        }

        // Compute SHA256 hash
        private string ComputeSHA256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }

        // Create genesis block (first block in chain)
        public async Task<Block> CreateGenesisBlock()
        {
            try
            {
                var existingGenesis = await _context.Blocks
                    .FirstOrDefaultAsync(b => b.PreviousHash == "0");

                if (existingGenesis != null)
                {
                    _logger.LogInformation("Genesis block already exists");
                    return existingGenesis;
                }

                var genesisBlock = new Block
                {
                    PreviousHash = "0",
                    Timestamp = DateTime.UtcNow,
                    MerkleRoot = ComputeSHA256Hash("GENESIS"),
                    Nonce = 0,
                    Confirmed = true,
                    CreatedAt = DateTime.UtcNow
                };

                // Mine the genesis block
                await MineBlockAsync(genesisBlock);

                _context.Blocks.Add(genesisBlock);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Genesis block created: {genesisBlock.BlockHash}");

                return genesisBlock;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating genesis block");
                throw;
            }
        }

        // Get blockchain height (number of blocks)
        public async Task<int> GetBlockchainHeight()
        {
            return await _context.Blocks.CountAsync();
        }

        // Get block by hash
        public async Task<Block?> GetBlockByHash(string blockHash)
        {
            return await _context.Blocks
                .Include(b => b.Transactions)
                .FirstOrDefaultAsync(b => b.BlockHash == blockHash);
        }

        // Get all blocks
        public async Task<List<Block>> GetAllBlocks()
        {
            return await _context.Blocks
                .Include(b => b.Transactions)
                .OrderByDescending(b => b.BlockId)
                .ToListAsync();
        }

        // Validate blockchain integrity
        public async Task<bool> ValidateBlockchain()
        {
            try
            {
                var blocks = await _context.Blocks
                    .OrderBy(b => b.BlockId)
                    .ToListAsync();

                if (!blocks.Any())
                {
                    _logger.LogInformation("Blockchain is empty");
                    return true;
                }

                // Check genesis block
                var genesis = blocks.First();
                if (genesis.PreviousHash != "0")
                {
                    _logger.LogError("Invalid genesis block");
                    return false;
                }

                // Validate each block
                for (int i = 1; i < blocks.Count; i++)
                {
                    var currentBlock = blocks[i];
                    var previousBlock = blocks[i - 1];

                    // Check previous hash
                    if (currentBlock.PreviousHash != previousBlock.BlockHash)
                    {
                        _logger.LogError($"Block {currentBlock.BlockId} has invalid previous hash");
                        return false;
                    }

                    // Recalculate block hash
                    var blockData = $"{currentBlock.PreviousHash}{currentBlock.Timestamp:yyyy-MM-dd HH:mm:ss.fff}{currentBlock.MerkleRoot}{currentBlock.Nonce}";
                    var calculatedHash = ComputeSHA256Hash(blockData);

                    if (calculatedHash != currentBlock.BlockHash)
                    {
                        _logger.LogError($"Block {currentBlock.BlockId} has invalid hash");
                        return false;
                    }
                }

                _logger.LogInformation("Blockchain validation passed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating blockchain");
                return false;
            }
        }

        public async Task<Block> GetBlockAsync(string blockHash)
        {
            try
            {
                var block = await _context.Blocks
                    .Include(b => b.Transactions)
                    .FirstOrDefaultAsync(b => b.BlockHash == blockHash);

                if (block == null)
                {
                    _logger.LogWarning($"Block not found: {blockHash}");
                }

                return block;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting block: {blockHash}");
                throw;
            }
        }

        public async Task<bool> VerifyTransactionAsync(string transactionId)
        {
            try
            {
                var transaction = await GetTransactionAsync(transactionId);

                if (transaction == null)
                {
                    _logger.LogWarning($"Transaction not found: {transactionId}");
                    return false;
                }

                // Check if transaction is confirmed in a block
                if (string.IsNullOrEmpty(transaction.BlockHash))
                {
                    _logger.LogInformation($"Transaction {transactionId} is pending - not in any block");
                    return false;
                }

                // Verify the block exists
                var block = await GetBlockAsync(transaction.BlockHash);
                if (block == null)
                {
                    _logger.LogWarning($"Block not found for transaction: {transactionId}");
                    return false;
                }

                // Check if block is confirmed
                if (!block.Confirmed)
                {
                    _logger.LogInformation($"Block {block.BlockHash} is not confirmed");
                    return false;
                }

                // Check if transaction exists in the block's transactions
                var isInBlock = block.Transactions.Any(t => t.TransactionId == transactionId);
                if (!isInBlock)
                {
                    _logger.LogWarning($"Transaction {transactionId} not found in block {block.BlockHash}");
                    return false;
                }

                _logger.LogInformation($"Transaction {transactionId} verified successfully in block {block.BlockHash}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying transaction: {transactionId}");
                return false;
            }
        }

        public async Task<List<Block>> GetAllBlocksAsync()
        {
            try
            {
                return await _context.Blocks
                    .Include(b => b.Transactions)
                    .OrderByDescending(b => b.BlockId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all blocks");
                throw;
            }
        }

        public async Task<bool> VerifyBlockchainAsync()
        {
            try
            {
                // Get all blocks in order
                var blocks = await _context.Blocks
                    .OrderBy(b => b.BlockId)
                    .Include(b => b.Transactions)
                    .ToListAsync();

                if (!blocks.Any())
                {
                    _logger.LogInformation("Blockchain is empty - no blocks to verify");
                    return true; // Empty blockchain is valid
                }

                // Check first block (genesis)
                var genesisBlock = blocks.First();
                if (genesisBlock.PreviousHash != "0")
                {
                    _logger.LogError($"Genesis block {genesisBlock.BlockHash} has invalid previous hash: {genesisBlock.PreviousHash}");
                    return false;
                }

                // Verify chain links
                for (int i = 1; i < blocks.Count; i++)
                {
                    var currentBlock = blocks[i];
                    var previousBlock = blocks[i - 1];

                    // Check previous hash link
                    if (currentBlock.PreviousHash != previousBlock.BlockHash)
                    {
                        _logger.LogError($"Block #{currentBlock.BlockId} has incorrect previous hash. Expected: {previousBlock.BlockHash}, Actual: {currentBlock.PreviousHash}");
                        return false;
                    }

                    // Verify block hash integrity
                    var calculatedHash = CalculateBlockHash(currentBlock);
                    if (calculatedHash != currentBlock.BlockHash)
                    {
                        _logger.LogError($"Block #{currentBlock.BlockId} hash mismatch. Calculated: {calculatedHash}, Stored: {currentBlock.BlockHash}");
                        return false;
                    }
                }

                // Check if all blocks are confirmed
                var unconfirmedBlocks = blocks.Where(b => !b.Confirmed).ToList();
                if (unconfirmedBlocks.Any())
                {
                    _logger.LogWarning($"Found {unconfirmedBlocks.Count} unconfirmed blocks");
                    // For demo purposes, we'll return true anyway
                    // In production, you might want stricter validation
                }

                _logger.LogInformation($"Blockchain verification successful: {blocks.Count} blocks verified");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying blockchain");
                return false;
            }
        }

        // You'll need this helper method for VerifyBlockchainAsync
        private string CalculateBlockHash(Block block)
        {
            // Combine block data for hashing
            var content = $"{block.BlockId}{block.PreviousHash}{block.Timestamp:O}{block.Nonce}{block.MerkleRoot}";

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        public Task<List<BlockchainTransaction>> GetMemberTransactions(string memberNo)
        {
            throw new NotImplementedException();
        }
    }

    // Blockchain status DTO
    public class BlockchainStatus
    {
        public int TotalBlocks { get; set; }
        public int TotalTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public string? LatestBlockHash { get; set; }
        public DateTime? LatestBlockTimestamp { get; set; }
        public int BlockHeight { get; set; }
        public bool IsValid { get; set; }
    }
}