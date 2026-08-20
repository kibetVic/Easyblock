using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface INextOfKeenService
    {
        Task<NextOfKeen> CreateNextOfKeenAsync(string memberNo, NextOfKeenDTO dto, string createdBy);
        Task<NextOfKeen> UpdateNextOfKeenAsync(int id, NextOfKeenDTO dto, string modifiedBy);
        Task<bool> DeleteNextOfKeenAsync(int id);
        Task<NextOfKeen> GetNextOfKeenByIdAsync(int id);
        Task<List<NextOfKeenResponseDTO>> GetNextOfKeensByMemberAsync(string memberNo, string companyCode);
        Task<NextOfKeen> GetPrimaryNextOfKeenAsync(string memberNo, string companyCode);
        Task<bool> SetPrimaryNextOfKeenAsync(int id, string memberNo, string companyCode);
        Task<bool> UpdateBenefitPercentagesAsync(string memberNo, List<BenefitPercentageUpdateDTO> updates);
        Task<decimal> GetTotalBenefitPercentageAsync(string memberNo, string companyCode, int? excludeId = null);
    }

    public class BenefitPercentageUpdateDTO
    {
        public int Id { get; set; }
        public decimal? BenefitPercentage { get; set; }
    }

    public class NextOfKeenService : INextOfKeenService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<NextOfKeenService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public NextOfKeenService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<NextOfKeenService> logger,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        /// <summary>
        /// Gets the total benefit percentage for a member
        /// </summary>
        public async Task<decimal> GetTotalBenefitPercentageAsync(string memberNo, string companyCode, int? excludeId = null)
        {
            var query = _context.NextOfKeens
                .Where(n => n.MemberNo == memberNo &&
                           n.CompanyCode == companyCode &&
                           n.Status == "Active");

            if (excludeId.HasValue)
            {
                query = query.Where(n => n.Id != excludeId.Value);
            }

            var total = await query.SumAsync(n => n.BenefitPercentage ?? 0);
            return total;
        }

        /// <summary>
        /// Validates if adding/updating would exceed 100% benefit allocation
        /// </summary>
        private async Task ValidateBenefitPercentageAsync(string memberNo, string companyCode, decimal newPercentage, int? excludeId = null)
        {
            var currentTotal = await GetTotalBenefitPercentageAsync(memberNo, companyCode, excludeId);
            var newTotal = currentTotal + newPercentage;

            if (newTotal > 100)
            {
                throw new InvalidOperationException(
                    $"Cannot save. Total benefit percentage would be {newTotal:F2}%, which exceeds the 100% limit. " +
                    $"Current total (excluding this record) is {currentTotal:F2}%. Please reduce the percentage.");
            }
        }

        public async Task<NextOfKeen> CreateNextOfKeenAsync(string memberNo, NextOfKeenDTO dto, string createdBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Verify member exists
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

                if (member == null)
                {
                    throw new InvalidOperationException($"Member {memberNo} not found");
                }

                // Validate benefit percentage doesn't exceed 100%
                var benefitPercentage = dto.BenefitPercentage ?? 0;
                await ValidateBenefitPercentageAsync(memberNo, companyCode, benefitPercentage);

                // If this is primary, clear existing primary
                if (dto.IsPrimary)
                {
                    var existingPrimary = await _context.NextOfKeens
                        .FirstOrDefaultAsync(n => n.MemberNo == memberNo &&
                                                  n.CompanyCode == companyCode &&
                                                  n.IsPrimary == true);
                    if (existingPrimary != null)
                    {
                        existingPrimary.IsPrimary = false;
                        existingPrimary.ModifiedBy = createdBy;
                        existingPrimary.ModifiedAt = DateTime.Now;
                    }
                }

                var nextOfKeen = new NextOfKeen
                {
                    MemberNo = memberNo,
                    CompanyCode = companyCode,
                    FullName = dto.FullName,
                    Relationship = dto.Relationship,
                    PhoneNo = dto.PhoneNo,
                    Email = dto.Email,
                    PhysicalAddress = dto.PhysicalAddress,
                    IdNumber = dto.IdNumber,
                    PassportNumber = dto.PassportNumber,
                    Employer = dto.Employer,
                    Occupation = dto.Occupation,
                    BenefitPercentage = benefitPercentage,
                    PriorityOrder = dto.PriorityOrder ?? 1,
                    IsPrimary = dto.IsPrimary,
                    Status = "Active",
                    Notes = dto.Notes,
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.Now
                };

                _context.NextOfKeens.Add(nextOfKeen);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(nextOfKeen, "CREATE", createdBy);

                await transaction.CommitAsync();

                _logger.LogInformation($"Next of kin {nextOfKeen.FullName} created for member {memberNo}");
                return nextOfKeen;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error creating next of kin for member {memberNo}");
                throw;
            }
        }

        public async Task<NextOfKeen> UpdateNextOfKeenAsync(int id, NextOfKeenDTO dto, string modifiedBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var nextOfKeen = await _context.NextOfKeens.FindAsync(id);
                if (nextOfKeen == null)
                {
                    throw new InvalidOperationException($"Next of kin with ID {id} not found");
                }

                // Validate benefit percentage doesn't exceed 100% (excluding current record)
                var newBenefitPercentage = dto.BenefitPercentage ?? 0;
                await ValidateBenefitPercentageAsync(nextOfKeen.MemberNo, nextOfKeen.CompanyCode, newBenefitPercentage, id);

                // If this is primary, clear existing primary for this member (excluding current)
                if (dto.IsPrimary && !nextOfKeen.IsPrimary)
                {
                    var existingPrimary = await _context.NextOfKeens
                        .FirstOrDefaultAsync(n => n.MemberNo == nextOfKeen.MemberNo &&
                                                  n.CompanyCode == nextOfKeen.CompanyCode &&
                                                  n.IsPrimary == true &&
                                                  n.Id != id);
                    if (existingPrimary != null)
                    {
                        existingPrimary.IsPrimary = false;
                        existingPrimary.ModifiedBy = modifiedBy;
                        existingPrimary.ModifiedAt = DateTime.Now;
                    }
                }

                // Update fields
                nextOfKeen.FullName = dto.FullName;
                nextOfKeen.Relationship = dto.Relationship;
                nextOfKeen.PhoneNo = dto.PhoneNo;
                nextOfKeen.Email = dto.Email;
                nextOfKeen.PhysicalAddress = dto.PhysicalAddress;
                nextOfKeen.IdNumber = dto.IdNumber;
                nextOfKeen.PassportNumber = dto.PassportNumber;
                nextOfKeen.Employer = dto.Employer;
                nextOfKeen.Occupation = dto.Occupation;
                nextOfKeen.BenefitPercentage = newBenefitPercentage;
                nextOfKeen.PriorityOrder = dto.PriorityOrder ?? 1;
                nextOfKeen.IsPrimary = dto.IsPrimary;
                nextOfKeen.Notes = dto.Notes;
                nextOfKeen.ModifiedBy = modifiedBy;
                nextOfKeen.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(nextOfKeen, "UPDATE", modifiedBy);

                await transaction.CommitAsync();

                _logger.LogInformation($"Next of kin {nextOfKeen.FullName} updated");
                return nextOfKeen;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating next of kin {id}");
                throw;
            }
        }

        public async Task<bool> DeleteNextOfKeenAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var nextOfKeen = await _context.NextOfKeens.FindAsync(id);
                if (nextOfKeen == null)
                {
                    throw new InvalidOperationException($"Next of kin with ID {id} not found");
                }

                // Soft delete - just mark as inactive
                nextOfKeen.Status = "Removed";
                nextOfKeen.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(nextOfKeen, "DELETE", "SYSTEM");

                await transaction.CommitAsync();

                _logger.LogInformation($"Next of kin {nextOfKeen.FullName} removed");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error deleting next of kin {id}");
                throw;
            }
        }

        public async Task<NextOfKeen> GetNextOfKeenByIdAsync(int id)
        {
            return await _context.NextOfKeens.FindAsync(id);
        }

        public async Task<List<NextOfKeenResponseDTO>> GetNextOfKeensByMemberAsync(string memberNo, string companyCode)
        {
            try
            {
                var nextOfKeens = await _context.NextOfKeens
                    .Where(n => n.MemberNo == memberNo &&
                               n.CompanyCode == companyCode &&
                               n.Status == "Active")
                    .OrderBy(n => n.PriorityOrder)
                    .ThenByDescending(n => n.IsPrimary)
                    .Select(n => new NextOfKeenResponseDTO
                    {
                        Id = n.Id,
                        MemberNo = n.MemberNo,
                        FullName = n.FullName,
                        Relationship = n.Relationship,
                        PhoneNo = n.PhoneNo,
                        Email = n.Email,
                        PhysicalAddress = n.PhysicalAddress,
                        IdNumber = n.IdNumber,
                        PassportNumber = n.PassportNumber,
                        Employer = n.Employer,
                        Occupation = n.Occupation,
                        BenefitPercentage = n.BenefitPercentage,
                        PriorityOrder = n.PriorityOrder,
                        IsPrimary = n.IsPrimary,
                        Status = n.Status,
                        Notes = n.Notes,
                        CreatedAt = n.CreatedAt,
                        BlockchainTxId = n.BlockchainTxId
                    })
                    .ToListAsync();

                return nextOfKeens;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting next of kin for member {memberNo}");
                return new List<NextOfKeenResponseDTO>();
            }
        }

        public async Task<NextOfKeen> GetPrimaryNextOfKeenAsync(string memberNo, string companyCode)
        {
            try
            {
                return await _context.NextOfKeens
                    .FirstOrDefaultAsync(n => n.MemberNo == memberNo &&
                                              n.CompanyCode == companyCode &&
                                              n.IsPrimary == true &&
                                              n.Status == "Active");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting primary next of kin for member {memberNo}");
                return null;
            }
        }

        public async Task<bool> SetPrimaryNextOfKeenAsync(int id, string memberNo, string companyCode)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Clear existing primary
                var existingPrimary = await _context.NextOfKeens
                    .FirstOrDefaultAsync(n => n.MemberNo == memberNo &&
                                              n.CompanyCode == companyCode &&
                                              n.IsPrimary == true);
                if (existingPrimary != null)
                {
                    existingPrimary.IsPrimary = false;
                    existingPrimary.ModifiedAt = DateTime.Now;
                }

                // Set new primary
                var newPrimary = await _context.NextOfKeens.FindAsync(id);
                if (newPrimary == null)
                {
                    throw new InvalidOperationException($"Next of kin with ID {id} not found");
                }
                newPrimary.IsPrimary = true;
                newPrimary.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                await RecordBlockchainTransaction(newPrimary, "SET_PRIMARY", "SYSTEM");

                await transaction.CommitAsync();

                _logger.LogInformation($"Primary next of kin set to {newPrimary.FullName}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error setting primary next of kin {id}");
                throw;
            }
        }

        public async Task<bool> UpdateBenefitPercentagesAsync(string memberNo, List<BenefitPercentageUpdateDTO> updates)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var totalPercentage = updates.Sum(u => u.BenefitPercentage ?? 0);
                if (totalPercentage > 100)
                {
                    throw new InvalidOperationException($"Total benefit percentage ({totalPercentage:F2}%) cannot exceed 100%");
                }

                foreach (var update in updates)
                {
                    var nextOfKeen = await _context.NextOfKeens.FindAsync(update.Id);
                    if (nextOfKeen != null && nextOfKeen.MemberNo == memberNo)
                    {
                        nextOfKeen.BenefitPercentage = update.BenefitPercentage;
                        nextOfKeen.ModifiedAt = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();

                // Record blockchain transaction for percentage update
                var member = await _context.Members.FirstOrDefaultAsync(m => m.MemberNo == memberNo);
                if (member != null)
                {
                    var blockchainData = new
                    {
                        MemberNo = memberNo,
                        MemberName = $"{member.Surname} {member.OtherNames}",
                        Updates = updates,
                        TotalPercentage = totalPercentage,
                        Action = "UPDATE_BENEFITS"
                    };

                    await _blockchainService.CreateAndAddTransactionAsync(
                        "NEXT_OF_KIN_BENEFITS_UPDATED",
                        memberNo,
                        member.CompanyCode,
                        0,
                        $"{memberNo}-benefits-updated",
                        blockchainData);
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Benefit percentages updated for member {memberNo}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating benefit percentages for member {memberNo}");
                throw;
            }
        }

        private async Task RecordBlockchainTransaction(NextOfKeen nextOfKeen, string action, string performedBy)
        {
            try
            {
                // Get member details for blockchain record
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == nextOfKeen.MemberNo &&
                                              m.CompanyCode == nextOfKeen.CompanyCode);

                var blockchainData = new
                {
                    Id = nextOfKeen.Id,
                    MemberNo = nextOfKeen.MemberNo,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                    FullName = nextOfKeen.FullName,
                    Relationship = nextOfKeen.Relationship,
                    PhoneNo = nextOfKeen.PhoneNo,
                    Email = nextOfKeen.Email,
                    PhysicalAddress = nextOfKeen.PhysicalAddress,
                    IdNumber = nextOfKeen.IdNumber,
                    PassportNumber = nextOfKeen.PassportNumber,
                    Employer = nextOfKeen.Employer,
                    Occupation = nextOfKeen.Occupation,
                    BenefitPercentage = nextOfKeen.BenefitPercentage,
                    PriorityOrder = nextOfKeen.PriorityOrder,
                    IsPrimary = nextOfKeen.IsPrimary,
                    Status = nextOfKeen.Status,
                    Action = action,
                    PerformedBy = performedBy,
                    PerformedAt = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    $"NEXT_OF_KIN_{action}",
                    nextOfKeen.MemberNo,
                    nextOfKeen.CompanyCode,
                    0,
                    nextOfKeen.Id.ToString(),
                    blockchainData);

                if (blockchainTx != null)
                {
                    nextOfKeen.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Blockchain transaction recorded for next of kin {action}: {blockchainTx.TransactionId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to record blockchain transaction for next of kin {action}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to record blockchain transaction for next of kin {action}");
                // Don't throw - record is still saved in database
            }
        }
    }
}