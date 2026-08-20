// Services/CollateralService.cs
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface ICollateralService
    {
        Task<CollateralResponseDTO> CreateAsync(CollateralDTO dto, string userId);
        Task<CollateralResponseDTO> UpdateAsync(long id, CollateralDTO dto, string userId);
        Task<bool> DeleteAsync(long id, string userId);
        Task<CollateralResponseDTO> GetByIdAsync(long id);
        Task<List<CollateralResponseDTO>> GetAllAsync(string companyCode);
        Task<string> GenerateColCodeAsync(string companyCode);
        Task<bool> IsColCodeUniqueAsync(string colCode, string companyCode, long? excludeId = null);
    }

    public class CollateralService : ICollateralService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<CollateralService> _logger;

        public CollateralService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<CollateralService> logger)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
        }

        public async Task<CollateralResponseDTO> CreateAsync(CollateralDTO dto, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Check if code is unique
                if (!await IsColCodeUniqueAsync(dto.ColCode, dto.CompanyCode))
                {
                    throw new InvalidOperationException($"Collateral code {dto.ColCode} already exists.");
                }

                var collateral = new Collateral
                {
                    ColCode = dto.ColCode,
                    Coldescription = dto.Coldescription,
                    Percentage = dto.Percentage,
                    CompanyCode = dto.CompanyCode
                };

                _context.Collaterals.Add(collateral);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        Action = "CREATE",
                        CollateralId = collateral.Id,
                        ColCode = collateral.ColCode,
                        Coldescription = collateral.Coldescription,
                        Percentage = collateral.Percentage,
                        CreatedBy = userId,
                        CreatedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "COLLATERAL_CREATE",
                        null,
                        dto.CompanyCode,
                        (decimal)collateral.Percentage,
                        collateral.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        collateral.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for collateral");
                }

                await transaction.CommitAsync();

                return new CollateralResponseDTO
                {
                    Id = collateral.Id,
                    ColCode = collateral.ColCode,
                    Coldescription = collateral.Coldescription,
                    Percentage = collateral.Percentage,
                    CompanyCode = collateral.CompanyCode,
                    BlockchainTxId = collateral.BlockchainTxId,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating collateral");
                throw;
            }
        }

        public async Task<CollateralResponseDTO> UpdateAsync(long id, CollateralDTO dto, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var collateral = await _context.Collaterals.FindAsync(id);
                if (collateral == null)
                {
                    throw new InvalidOperationException($"Collateral with ID {id} not found.");
                }

                // Store old values for blockchain
                var oldValues = new
                {
                    collateral.ColCode,
                    collateral.Coldescription,
                    collateral.Percentage
                };

                collateral.ColCode = dto.ColCode;
                collateral.Coldescription = dto.Coldescription;
                collateral.Percentage = dto.Percentage;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "UPDATE",
                        CollateralId = collateral.Id,
                        OldValues = oldValues,
                        NewValues = new
                        {
                            collateral.ColCode,
                            collateral.Coldescription,
                            collateral.Percentage
                        },
                        ModifiedBy = userId,
                        ModifiedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "COLLATERAL_UPDATE",
                        null,
                        collateral.CompanyCode,
                        (decimal)collateral.Percentage,
                        collateral.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        collateral.BlockchainTxId = blockchainTx.TransactionId;
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for collateral update");
                }

                await transaction.CommitAsync();

                return new CollateralResponseDTO
                {
                    Id = collateral.Id,
                    ColCode = collateral.ColCode,
                    Coldescription = collateral.Coldescription,
                    Percentage = collateral.Percentage,
                    CompanyCode = collateral.CompanyCode,
                    BlockchainTxId = collateral.BlockchainTxId,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating collateral");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(long id, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var collateral = await _context.Collaterals.FindAsync(id);
                if (collateral == null)
                {
                    throw new InvalidOperationException($"Collateral with ID {id} not found.");
                }

                // Check if collateral is linked to any loans (if ColloanGuar table exists)
                // This is optional - skip if ColloanGuar doesn't exist yet
                try
                {
                    var linkedLoans = await _context.ColloanGuars
                        .Where(lg => lg.Id == id)
                        .AnyAsync();

                    if (linkedLoans)
                    {
                        throw new InvalidOperationException("Cannot delete collateral because it is linked to one or more loans.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not check loan links - ColloanGuar table may not exist yet");
                }

                var collateralDetails = new
                {
                    collateral.Id,
                    collateral.ColCode,
                    collateral.Coldescription,
                    collateral.Percentage
                };

                _context.Collaterals.Remove(collateral);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "DELETE",
                        CollateralDetails = collateralDetails,
                        DeletedBy = userId,
                        DeletedAt = DateTime.Now
                    };

                    await _blockchainService.CreateAndAddTransactionAsync(
                        "COLLATERAL_DELETE",
                        null,
                        collateral.CompanyCode,
                        (decimal)collateral.Percentage,
                        collateral.Id.ToString(),
                        blockchainData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for collateral deletion");
                }

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting collateral");
                throw;
            }
        }

        public async Task<CollateralResponseDTO> GetByIdAsync(long id)
        {
            var collateral = await _context.Collaterals.FindAsync(id);
            if (collateral == null) return null;

            return new CollateralResponseDTO
            {
                Id = collateral.Id,
                ColCode = collateral.ColCode,
                Coldescription = collateral.Coldescription,
                Percentage = collateral.Percentage,
                CompanyCode = collateral.CompanyCode,
                BlockchainTxId = collateral.BlockchainTxId
            };
        }

        public async Task<List<CollateralResponseDTO>> GetAllAsync(string companyCode)
        {
            var collaterals = await _context.Collaterals
                .Where(c => c.CompanyCode == companyCode)
                .OrderByDescending(c => c.Id)
                .ToListAsync();

            return collaterals.Select(c => new CollateralResponseDTO
            {
                Id = c.Id,
                ColCode = c.ColCode,
                Coldescription = c.Coldescription,
                Percentage = c.Percentage,
                CompanyCode = c.CompanyCode,
                BlockchainTxId = c.BlockchainTxId
            }).ToList();
        }

        public async Task<string> GenerateColCodeAsync(string companyCode)
        {
            var prefix = "COL";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastCollateral = await _context.Collaterals
                .Where(c => c.CompanyCode == companyCode && c.ColCode.StartsWith(prefix + date))
                .OrderByDescending(c => c.ColCode)
                .FirstOrDefaultAsync();

            if (lastCollateral != null && lastCollateral.ColCode.Length >= prefix.Length + date.Length + 3)
            {
                var seqStr = lastCollateral.ColCode.Substring(lastCollateral.ColCode.Length - 3);
                if (int.TryParse(seqStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D3}";
        }

        public async Task<bool> IsColCodeUniqueAsync(string colCode, string companyCode, long? excludeId = null)
        {
            var query = _context.Collaterals
                .Where(c => c.ColCode == colCode && c.CompanyCode == companyCode);

            if (excludeId.HasValue)
            {
                query = query.Where(c => c.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }
    }
}