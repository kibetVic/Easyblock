// Services/ShareTypeService.cs
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Services
{
    public class ShareTypeService : IShareTypeService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ShareTypeService> _logger;
        private readonly IBlockchainService _blockchainService;

        public ShareTypeService(
            ApplicationDbContext context,
            ILogger<ShareTypeService> logger,
            IBlockchainService blockchainService)
        {
            _context = context;
            _logger = logger;
            _blockchainService = blockchainService;
        }


        // Services/ShareTypeService.cs - Updated with proper blockchain integration

        public async Task<ShareTypeResponseDTO> CreateShareTypeAsync(ShareTypeCreateDTO shareTypeDto)
        {
            _logger.LogInformation($"Creating share type: {shareTypeDto.SharesCode} for company: {shareTypeDto.CompanyCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validate DTO
                await ValidateShareTypeAsync(shareTypeDto);

                // Check if share type already exists in the SAME company
                var existingShareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(st => st.SharesCode == shareTypeDto.SharesCode &&
                                              st.CompanyCode == shareTypeDto.CompanyCode);

                if (existingShareType != null)
                {
                    throw new ValidationException($"Share type with code '{shareTypeDto.SharesCode}' already exists in this company");
                }

                // Create new share type
                var shareType = new Sharetype
                {
                    SharesCode = shareTypeDto.SharesCode,
                    SharesType = shareTypeDto.SharesType,
                    SharesAcc = shareTypeDto.SharesAcc,
                    ContraAcc = shareTypeDto.ContraAcc,
                    PlacePeriod = shareTypeDto.PlacePeriod,
                    LoanToShareRatio = shareTypeDto.LoanToShareRatio,
                    Issharecapital = shareTypeDto.Issharecapital,
                    Interest = shareTypeDto.Interest,
                    MaxAmount = shareTypeDto.MaxAmount,
                    Guarantor = shareTypeDto.Guarantor,
                    CompanyCode = shareTypeDto.CompanyCode,
                    IsMainShares = shareTypeDto.IsMainShares,
                    UsedToGuarantee = shareTypeDto.UsedToGuarantee,
                    UsedToOffset = shareTypeDto.UsedToOffset,
                    Withdrawable = shareTypeDto.Withdrawable,
                    Loanquaranto = shareTypeDto.Loanquaranto,
                    Priority = shareTypeDto.Priority,
                    MinAmount = shareTypeDto.MinAmount,
                    Ppacc = string.IsNullOrEmpty(shareTypeDto.Ppacc) ? shareTypeDto.SharesAcc : shareTypeDto.Ppacc,
                    LowerLimit = shareTypeDto.LowerLimit,
                    ElseRatio = shareTypeDto.ElseRatio,
                    AuditId = shareTypeDto.CreatedBy,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now
                };

                _context.Sharetypes.Add(shareType);
                await _context.SaveChangesAsync();

                // ============================================================
                // CREATE BLOCK AND BLOCKCHAIN TRANSACTION (Like MemberService)
                // ============================================================

                // Generate block hash
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                // Create Block record
                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = await GetLastBlockHashAsync(),
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                // Prepare blockchain data
                var blockchainData = new
                {
                    TransactionType = "SHARE_TYPE_CREATE",
                    ShareTypeCode = shareType.SharesCode,
                    ShareTypeName = shareType.SharesType,
                    ShareAccount = shareType.SharesAcc,
                    CompanyCode = shareType.CompanyCode,
                    CreatedBy = shareTypeDto.CreatedBy,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Properties = new
                    {
                        shareType.IsMainShares,
                        shareType.MinAmount,
                        shareType.MaxAmount,
                        shareType.Withdrawable,
                        shareType.UsedToGuarantee,
                        shareType.Priority,
                        shareType.LowerLimit,
                        shareType.ElseRatio
                    },
                    BlockHash = blockHash
                };

                _logger.LogInformation($"Creating blockchain transaction for share type: {shareType.SharesCode}");

                // Generate transaction hash
                string dataHash = await GenerateTransactionHashAsync(blockchainData);

                // Create Blockchain Transaction
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "SHARE_TYPE_CREATE",
                    MemberNo = null, // Not member-specific
                    CompanyCode = shareTypeDto.CompanyCode,
                    Amount = 0,
                    Timestamp = DateTime.Now,
                    DataHash = dataHash,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = shareType.SharesCode,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Update share type with blockchain transaction ID
                shareType.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation($"Share type {shareType.SharesCode} created successfully for company {shareType.CompanyCode} with BlockchainTxId: {shareType.BlockchainTxId}");

                return await GetShareTypeResponseDto(shareType);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error creating share type {shareTypeDto.SharesCode} for company {shareTypeDto.CompanyCode}");
                throw;
            }
        }

        public async Task<ShareTypeResponseDTO> UpdateShareTypeAsync(string sharesCode, ShareTypeUpdateDTO shareTypeDto)
        {
            _logger.LogInformation($"Updating share type: {sharesCode} for company: {shareTypeDto.CompanyCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get existing share type - must match BOTH SharesCode AND CompanyCode
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(st => st.SharesCode == sharesCode &&
                                              st.CompanyCode == shareTypeDto.CompanyCode);

                if (shareType == null)
                {
                    throw new KeyNotFoundException($"Share type '{sharesCode}' not found in this company");
                }

                // Store old values for blockchain
                var oldValues = new
                {
                    shareType.SharesType,
                    shareType.SharesAcc,
                    shareType.ContraAcc,
                    shareType.PlacePeriod,
                    shareType.LoanToShareRatio,
                    shareType.Issharecapital,
                    shareType.Interest,
                    shareType.MaxAmount,
                    shareType.Guarantor,
                    shareType.IsMainShares,
                    shareType.UsedToGuarantee,
                    shareType.UsedToOffset,
                    shareType.Withdrawable,
                    shareType.Loanquaranto,
                    shareType.Priority,
                    shareType.MinAmount,
                    shareType.LowerLimit,
                    shareType.ElseRatio
                };

                // Check if share type is in use (except for some fields)
                var usageCount = await GetShareTypeUsageCountAsync(sharesCode, shareTypeDto.CompanyCode);
                if (usageCount > 0)
                {
                    // Validate that critical fields aren't being changed when in use
                    if (shareType.IsMainShares != shareTypeDto.IsMainShares ||
                        shareType.MinAmount != shareTypeDto.MinAmount ||
                        shareType.Withdrawable != shareTypeDto.Withdrawable)
                    {
                        throw new ValidationException(
                            "Cannot change critical properties when share type is in use by members");
                    }
                }

                // Update fields
                shareType.SharesType = shareTypeDto.SharesType;
                shareType.SharesAcc = shareTypeDto.SharesAcc;
                shareType.ContraAcc = shareTypeDto.ContraAcc;
                shareType.PlacePeriod = shareTypeDto.PlacePeriod;
                shareType.LoanToShareRatio = shareTypeDto.LoanToShareRatio;
                shareType.Issharecapital = shareTypeDto.Issharecapital;
                shareType.Interest = shareTypeDto.Interest;
                shareType.MaxAmount = shareTypeDto.MaxAmount;
                shareType.Guarantor = shareTypeDto.Guarantor;
                shareType.UsedToGuarantee = shareTypeDto.UsedToGuarantee;
                shareType.UsedToOffset = shareTypeDto.UsedToOffset;
                shareType.Loanquaranto = shareTypeDto.Loanquaranto;
                shareType.Priority = shareTypeDto.Priority;
                shareType.Ppacc = string.IsNullOrEmpty(shareTypeDto.Ppacc) ? shareTypeDto.SharesAcc : shareTypeDto.Ppacc;
                shareType.LowerLimit = shareTypeDto.LowerLimit;
                shareType.ElseRatio = shareTypeDto.ElseRatio;
                shareType.AuditId = shareTypeDto.CreatedBy;
                shareType.AuditTime = DateTime.Now;
                shareType.AuditDateTime = DateTime.Now;

                await _context.SaveChangesAsync();

                // ============================================================
                // CREATE BLOCK AND BLOCKCHAIN TRANSACTION FOR UPDATE
                // ============================================================

                // Generate block hash
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                // Get previous block hash
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();
                string previousHash = lastBlock?.BlockHash ?? "0".PadLeft(64, '0');

                // Create Block record
                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = previousHash,
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                // Prepare blockchain data
                var blockchainData = new
                {
                    TransactionType = "SHARE_TYPE_UPDATE",
                    ShareTypeCode = shareType.SharesCode,
                    ShareTypeName = shareType.SharesType,
                    CompanyCode = shareType.CompanyCode,
                    UpdatedBy = shareTypeDto.CreatedBy,
                    UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    OldValues = oldValues,
                    NewValues = new
                    {
                        shareType.SharesType,
                        shareType.SharesAcc,
                        shareType.ContraAcc,
                        shareType.PlacePeriod,
                        shareType.LoanToShareRatio,
                        shareType.Issharecapital,
                        shareType.Interest,
                        shareType.MaxAmount,
                        shareType.Guarantor,
                        shareType.IsMainShares,
                        shareType.UsedToGuarantee,
                        shareType.UsedToOffset,
                        shareType.Withdrawable,
                        shareType.Loanquaranto,
                        shareType.Priority,
                        shareType.MinAmount,
                        shareType.LowerLimit,
                        shareType.ElseRatio
                    },
                    BlockHash = blockHash
                };

                _logger.LogInformation($"Creating blockchain transaction for share type update: {shareType.SharesCode}");

                // Generate transaction hash
                string dataHash = await GenerateTransactionHashAsync(blockchainData);

                // Create Blockchain Transaction
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "SHARE_TYPE_UPDATE",
                    MemberNo = null,
                    CompanyCode = shareTypeDto.CompanyCode,
                    Amount = 0,
                    Timestamp = DateTime.Now,
                    DataHash = dataHash,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = shareType.SharesCode,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Update share type with new blockchain transaction ID
                shareType.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation($"Share type {sharesCode} updated successfully for company {shareTypeDto.CompanyCode} with BlockchainTxId: {shareType.BlockchainTxId}");

                return await GetShareTypeResponseDto(shareType);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating share type {sharesCode} for company {shareTypeDto.CompanyCode}");
                throw;
            }
        }

        public async Task<bool> DeleteShareTypeAsync(string sharesCode, string companyCode)
        {
            _logger.LogInformation($"Deleting share type: {sharesCode} for company: {companyCode}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Find share type with BOTH SharesCode AND CompanyCode
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(st => st.SharesCode == sharesCode &&
                                              st.CompanyCode == companyCode);

                if (shareType == null)
                {
                    throw new KeyNotFoundException($"Share type '{sharesCode}' not found in this company");
                }

                // Check if share type is in use
                var usageCount = await GetShareTypeUsageCountAsync(sharesCode, companyCode);
                if (usageCount > 0)
                {
                    throw new ValidationException(
                        $"Cannot delete share type '{sharesCode}' because it's used by {usageCount} member(s)");
                }

                // Store share type info for blockchain before deletion
                var shareTypeInfo = new
                {
                    shareType.SharesCode,
                    shareType.SharesType,
                    shareType.SharesAcc,
                    shareType.CompanyCode,
                    shareType.IsMainShares,
                    shareType.MinAmount,
                    shareType.MaxAmount,
                    shareType.Priority
                };

                // ============================================================
                // CREATE BLOCK AND BLOCKCHAIN TRANSACTION FOR DELETE
                // ============================================================

                // Generate block hash
                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                // Get previous block hash
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();
                string previousHash = lastBlock?.BlockHash ?? "0".PadLeft(64, '0');

                // Create Block record
                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = previousHash,
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                // Prepare blockchain data
                var blockchainData = new
                {
                    TransactionType = "SHARE_TYPE_DELETE",
                    ShareTypeCode = shareType.SharesCode,
                    ShareTypeName = shareType.SharesType,
                    CompanyCode = companyCode,
                    DeletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    DeletedBy = "SYSTEM",
                    DeletedShareType = shareTypeInfo,
                    BlockHash = blockHash
                };

                _logger.LogInformation($"Creating blockchain transaction for share type delete: {shareType.SharesCode}");

                // Generate transaction hash
                string dataHash = await GenerateTransactionHashAsync(blockchainData);

                // Create Blockchain Transaction
                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "SHARE_TYPE_DELETE",
                    MemberNo = null,
                    CompanyCode = companyCode,
                    Amount = 0,
                    Timestamp = DateTime.Now,
                    DataHash = dataHash,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = shareType.SharesCode,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                // Delete from database
                _context.Sharetypes.Remove(shareType);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation($"Share type {sharesCode} deleted successfully for company {companyCode} with BlockchainTxId: {blockchainTx.TransactionId}");

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error deleting share type {sharesCode} for company {companyCode}");
                throw;
            }
        }

        // Helper method to get last block hash
        private async Task<string> GetLastBlockHashAsync()
        {
            try
            {
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                return lastBlock?.BlockHash ?? "0".PadLeft(64, '0');
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last block hash, using default");
                return "0".PadLeft(64, '0');
            }
        }

        // Helper method to generate transaction hash
        private async Task<string> GenerateTransactionHashAsync(object data)
        {
            try
            {
                var jsonData = System.Text.Json.JsonSerializer.Serialize(data);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToHexString(hash).ToLower();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating transaction hash");
                // Fallback to GUID-based hash
                return Guid.NewGuid().ToString().Replace("-", "");
            }
        }

        public async Task<ShareTypeResponseDTO> GetShareTypeByCodeAsync(string sharesCode, string companyCode)
        {
            // Get share type with BOTH SharesCode AND CompanyCode
            var shareType = await _context.Sharetypes
                .FirstOrDefaultAsync(st => st.SharesCode == sharesCode &&
                                          st.CompanyCode == companyCode);

            if (shareType == null)
            {
                throw new KeyNotFoundException($"Share type '{sharesCode}' not found in this company");
            }

            return await GetShareTypeResponseDto(shareType);
        }

        public async Task<List<ShareTypeResponseDTO>> GetShareTypesByCompanyAsync(string companyCode)
        {
            var shareTypes = await _context.Sharetypes
                .Where(st => st.CompanyCode == companyCode)  // Only get share types for this company
                .OrderBy(st => st.Priority)
                .ThenBy(st => st.SharesType)
                .ToListAsync();

            var result = new List<ShareTypeResponseDTO>();
            foreach (var shareType in shareTypes)
            {
                result.Add(await GetShareTypeResponseDto(shareType));
            }

            return result;
        }

        public async Task<List<ShareTypeSimpleDTO>> GetActiveShareTypesAsync(string companyCode)
        {
            return await _context.Sharetypes
                .Where(st => st.CompanyCode == companyCode)  // Only active share types for this company
                .OrderBy(st => st.Priority)
                .Select(st => new ShareTypeSimpleDTO
                {
                    SharesCode = st.SharesCode,
                    SharesType = st.SharesType,
                    IsMainShares = st.IsMainShares,
                    MinAmount = st.MinAmount,
                    MaxAmount = st.MaxAmount,
                    UsedToGuarantee = st.UsedToGuarantee,
                    Withdrawable = st.Withdrawable,
                    Priority = st.Priority
                })
                .ToListAsync();
        }

        public async Task<List<ShareTypeResponseDTO>> SearchShareTypesAsync(string searchTerm, string companyCode)
        {
            var query = _context.Sharetypes
                .Where(st => st.CompanyCode == companyCode);  // Only search within this company

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(st =>
                    st.SharesCode.Contains(searchTerm) ||
                    st.SharesType.Contains(searchTerm) ||
                    st.SharesAcc.Contains(searchTerm));
            }

            var shareTypes = await query
                .OrderBy(st => st.Priority)
                .ToListAsync();

            var result = new List<ShareTypeResponseDTO>();
            foreach (var shareType in shareTypes)
            {
                result.Add(await GetShareTypeResponseDto(shareType));
            }

            return result;
        }

        public async Task<bool> ValidateShareTypeAsync(ShareTypeCreateDTO shareTypeDto)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(shareTypeDto.SharesCode))
                throw new ValidationException("Share code is required");

            if (string.IsNullOrWhiteSpace(shareTypeDto.SharesType))
                throw new ValidationException("Share type name is required");

            if (string.IsNullOrWhiteSpace(shareTypeDto.SharesAcc))
                throw new ValidationException("Share account is required");

            if (shareTypeDto.MinAmount < 0)
                throw new ValidationException("Minimum amount cannot be negative");

            if (shareTypeDto.MaxAmount.HasValue && shareTypeDto.MaxAmount < shareTypeDto.MinAmount)
                throw new ValidationException("Maximum amount cannot be less than minimum amount");

            if (shareTypeDto.Priority < 1 || shareTypeDto.Priority > 10)
                throw new ValidationException("Priority must be between 1 and 10");

            // Check for duplicate share code ONLY within the SAME company
            // (Different companies can have the same share code)
            var existing = await _context.Sharetypes
                .FirstOrDefaultAsync(st => st.SharesCode == shareTypeDto.SharesCode &&
                                          st.CompanyCode == shareTypeDto.CompanyCode);
            if (existing != null)
            {
                throw new ValidationException($"Share code '{shareTypeDto.SharesCode}' already exists in this company");
            }

            return true;
        }

        public async Task<int> GetShareTypeUsageCountAsync(string sharesCode, string companyCode)
        {
            // Count members using this share type in this company
            var memberCount = await _context.Shares
                .CountAsync(s => s.Sharescode == sharesCode &&
                               s.CompanyCode == companyCode);

            // Count contributions using this share type in this company
            var contribCount = await _context.Contribs
                .CountAsync(c => c.Sharescode == sharesCode &&
                               c.CompanyCode == companyCode);

            return memberCount + contribCount;
        }

        private async Task<ShareTypeResponseDTO> GetShareTypeResponseDto(Sharetype shareType)
        {
            // Get usage statistics for this specific company
            var totalMembers = await _context.Shares
                .CountAsync(s => s.Sharescode == shareType.SharesCode &&
                               s.CompanyCode == shareType.CompanyCode);

            var totalShares = await _context.Shares
                .Where(s => s.Sharescode == shareType.SharesCode &&
                          s.CompanyCode == shareType.CompanyCode)
                .SumAsync(s => s.TotalShares);

            return new ShareTypeResponseDTO
            {
                SharesCode = shareType.SharesCode,
                SharesType = shareType.SharesType,
                SharesAcc = shareType.SharesAcc,
                ContraAcc = shareType.ContraAcc,
                PlacePeriod = shareType.PlacePeriod,
                LoanToShareRatio = shareType.LoanToShareRatio,
                Issharecapital = shareType.Issharecapital,
                Interest = shareType.Interest,
                MaxAmount = shareType.MaxAmount,
                Guarantor = shareType.Guarantor,
                IsMainShares = shareType.IsMainShares,
                UsedToGuarantee = shareType.UsedToGuarantee,
                UsedToOffset = shareType.UsedToOffset,
                Withdrawable = shareType.Withdrawable,
                Loanquaranto = shareType.Loanquaranto,
                Priority = shareType.Priority,
                MinAmount = shareType.MinAmount,
                Ppacc = shareType.Ppacc,
                LowerLimit = shareType.LowerLimit,
                ElseRatio = shareType.ElseRatio,
                CompanyCode = shareType.CompanyCode,
                CreatedAt = shareType.AuditDateTime,
                CreatedBy = shareType.AuditId,
                LastUpdated = shareType.AuditDateTime,
                TotalMembers = totalMembers,
                TotalShares = (decimal)totalShares
            };
        }
    }
}