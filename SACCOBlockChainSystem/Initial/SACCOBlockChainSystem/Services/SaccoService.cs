// Services/SaccoService.cs
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface ISaccoService
    {
        Task<SaccoParram> GetSaccoParametersAsync(string companyCode);
        Task<SaccoParram> GetSaccoParametersByIdAsync(int id);
        Task<List<SaccoParramListDTO>> GetAllSaccoParametersAsync();
        Task<int> GetMaxGuarantorsAsync(string companyCode);
        Task<List<SaccoParramListDTO>> GetAllSaccoParametersAsync(string companyCode);
        Task<SaccoParram> CreateSaccoParametersAsync(SaccoParramDTO parameters, string createdBy);
        Task<SaccoParram> UpdateSaccoParametersAsync(SaccoParramDTO parameters, string updatedBy);
        Task<bool> DeleteSaccoParametersAsync(int id);
        Task<List<GlSetup>> GetGlAccountsForDropdownAsync(string companyCode);
    }

    public class SaccoService : ISaccoService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<SaccoService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public SaccoService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<SaccoService> logger,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        public async Task<List<SaccoParramListDTO>> GetAllSaccoParametersAsync(string companyCode)
        {
            return await _context.SaccoParram
                .Where(sp => sp.CompanyCode == companyCode) // Filter by company code
                .OrderByDescending(sp => sp.CreatedAt)
                .Select(sp => new SaccoParramListDTO
                {
                    Id = sp.Id,
                    SaccoName = sp.SaccoName,
                    CompanyCode = sp.CompanyCode,
                    Telephone = sp.Telephone,
                    EmailAddress = sp.EmailAddress,
                    MembershipMaturityMonths = sp.MembershipMaturityMonths,
                    WithdrawalNoticeDays = sp.WithdrawalNoticeDays,
                    MaxGuarantor = sp.MaxGuarantor,
                    CreatedAt = sp.CreatedAt,
                    Suspense = sp.Suspense,
                    RetainedEarnings = sp.RetainedEarnings,
                    Creditors = sp.Creditors
                })
                .ToListAsync();
        }

        public async Task<SaccoParram> GetSaccoParametersAsync(string companyCode)
        {
            var parameters = await _context.SaccoParram
                .FirstOrDefaultAsync(sp => sp.CompanyCode == companyCode);

            if (parameters == null)
            {
                // Get company name from SaccoParram - FILTER BY COMPANY CODE
                var company = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

                var companyName = company?.SaccoName ?? $"{companyCode} SACCO";

                // Clean company name for sender ID
                //var senderId = CleanSenderId(companyName);

                // Create default parameters with all fields
                parameters = new SaccoParram
                {
                    CompanyCode = companyCode,
                    SaccoName = companyName,
                    MaxGuarantor = 5,
                    MinGuarantor = 1,
                    MembershipMaturityMonths = 6,
                    WithdrawalNoticeDays = 30,
                    DividendProcessingDays = 14,
                    DefaultCurrency = "KES",
                    DefaultRounding = 2,
                    CreatedAt = DateTime.Now,
                    CreatedBy = "SYSTEM"
                };

                _context.SaccoParram.Add(parameters);
                await _context.SaveChangesAsync();
            }

            return parameters;
        }

        public async Task<SaccoParram> GetSaccoParametersByIdAsync(int id)
        {
            return await _context.SaccoParram
                .FirstOrDefaultAsync(sp => sp.Id == id);
        }

        public async Task<List<SaccoParramListDTO>> GetAllSaccoParametersAsync()
        {
            return await _context.SaccoParram
                .OrderByDescending(sp => sp.CreatedAt)
                .Select(sp => new SaccoParramListDTO
                {
                    Id = sp.Id,
                    SaccoName = sp.SaccoName,
                    CompanyCode = sp.CompanyCode,
                    Telephone = sp.Telephone,
                    EmailAddress = sp.EmailAddress,
                    MembershipMaturityMonths = sp.MembershipMaturityMonths,
                    WithdrawalNoticeDays = sp.WithdrawalNoticeDays,
                    MaxGuarantor = sp.MaxGuarantor,
                    CreatedAt = sp.CreatedAt,
                    Suspense = sp.Suspense,
                    RetainedEarnings = sp.RetainedEarnings,
                    Creditors = sp.Creditors
                })
                .ToListAsync();
        }

        public async Task<int> GetMaxGuarantorsAsync(string companyCode)
        {
            try
            {
                var saccoParams = await _context.SaccoParram
                    .FirstOrDefaultAsync(sp => sp.CompanyCode == companyCode);

                return saccoParams?.MaxGuarantor ?? 5;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting max guarantors for company {companyCode}", companyCode);
                return 5;
            }
        }

        public async Task<SaccoParram> CreateSaccoParametersAsync(SaccoParramDTO dto, string createdBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Check if parameters already exist for this company
                var existing = await _context.SaccoParram
                    .FirstOrDefaultAsync(sp => sp.CompanyCode == dto.CompanyCode);

                if (existing != null)
                {
                    throw new InvalidOperationException($"SACCO parameters already exist for company {dto.CompanyCode}");
                }

                var parameters = new SaccoParram
                {
                    SaccoName = dto.SaccoName,
                    CompanyCode = dto.CompanyCode,
                    NoOfEmployees = dto.NoOfEmployees,
                    Address = dto.Address,
                    Town = dto.Town,
                    Telephone = dto.Telephone,
                    Fax = dto.Fax,
                    EmailAddress = dto.EmailAddress,
                    Website = dto.Website,
                    PhysicalAddress = dto.PhysicalAddress,
                    CheckOffDate = dto.CheckOffDate,
                    MembershipMaturityMonths = dto.MembershipMaturityMonths,
                    WithdrawalNoticeDays = dto.WithdrawalNoticeDays,
                    DividendProcessingDays = dto.DividendProcessingDays,
                    MaxGuarantor = dto.MaxGuarantor,
                    MinGuarantor = dto.MinGuarantor,
                    DefaultCurrency = dto.DefaultCurrency,
                    DefaultRounding = dto.DefaultRounding,
                    SignificantLoanBalance = dto.SignificantLoanBalance,
                    ActionOnDefaultedInterest = dto.ActionOnDefaultedInterest,
                    Suspense = dto.Suspense,
                    RetainedEarnings = dto.RetainedEarnings,
                    Creditors = dto.Creditors,
                    CreatedAt = DateTime.Now,
                    CreatedBy = createdBy
                };

                _context.SaccoParram.Add(parameters);
                await _context.SaveChangesAsync();

                // Record on blockchain
                await RecordOnBlockchainAsync(parameters, "CREATE", createdBy);

                await transaction.CommitAsync();

                _logger.LogInformation($"SACCO parameters created for company {dto.CompanyCode}");
                return parameters;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating SACCO parameters");
                throw;
            }
        }

        public async Task<SaccoParram> UpdateSaccoParametersAsync(SaccoParramDTO dto, string updatedBy)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var parameters = await _context.SaccoParram
                    .FirstOrDefaultAsync(sp => sp.Id == dto.Id);

                if (parameters == null)
                {
                    throw new InvalidOperationException($"SACCO parameters with ID {dto.Id} not found");
                }

                // Update all fields
                parameters.SaccoName = dto.SaccoName;
                parameters.NoOfEmployees = dto.NoOfEmployees;
                parameters.Address = dto.Address;
                parameters.Town = dto.Town;
                parameters.Telephone = dto.Telephone;
                parameters.Fax = dto.Fax;
                parameters.EmailAddress = dto.EmailAddress;
                parameters.Website = dto.Website;
                parameters.PhysicalAddress = dto.PhysicalAddress;
                parameters.CheckOffDate = dto.CheckOffDate;
                parameters.MembershipMaturityMonths = dto.MembershipMaturityMonths;
                parameters.WithdrawalNoticeDays = dto.WithdrawalNoticeDays;
                parameters.DividendProcessingDays = dto.DividendProcessingDays;
                parameters.MaxGuarantor = dto.MaxGuarantor;
                parameters.MinGuarantor = dto.MinGuarantor;
                parameters.DefaultCurrency = dto.DefaultCurrency;
                parameters.DefaultRounding = dto.DefaultRounding;
                parameters.SignificantLoanBalance = dto.SignificantLoanBalance;
                parameters.ActionOnDefaultedInterest = dto.ActionOnDefaultedInterest;
                parameters.Suspense = dto.Suspense;
                parameters.RetainedEarnings = dto.RetainedEarnings;
                parameters.Creditors = dto.Creditors;
                parameters.UpdatedAt = DateTime.Now;
                parameters.UpdatedBy = updatedBy;

                await _context.SaveChangesAsync();

                // Record on blockchain
                await RecordOnBlockchainAsync(parameters, "UPDATE", updatedBy);

                await transaction.CommitAsync();

                _logger.LogInformation($"SACCO parameters updated for company {parameters.CompanyCode}");
                return parameters;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating SACCO parameters");
                throw;
            }
        }

        public async Task<bool> DeleteSaccoParametersAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var parameters = await _context.SaccoParram.FindAsync(id);
                if (parameters == null)
                {
                    return false;
                }

                _context.SaccoParram.Remove(parameters);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"SACCO parameters deleted for company {parameters.CompanyCode}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting SACCO parameters");
                throw;
            }
        }

        public async Task<List<GlSetup>> GetGlAccountsForDropdownAsync(string companyCode)
        {
            return await _context.GlSetup
                .Where(g => g.CompanyCode == companyCode && g.Status == true)
                .OrderBy(g => g.AccNo)
                .Select(g => new GlSetup
                {
                    AccNo = g.AccNo,
                    Glaccname = g.Glaccname,
                    Type = g.Type,
                    SubType = g.SubType
                })
                .ToListAsync();
        }

        private async Task RecordOnBlockchainAsync(SaccoParram parameters, string action, string performedBy)
        {
            try
            {
                var blockchainData = new
                {
                    parameters.Id,
                    parameters.CompanyCode,
                    parameters.SaccoName,
                    parameters.MembershipMaturityMonths,
                    parameters.WithdrawalNoticeDays,
                    parameters.MaxGuarantor,
                    parameters.MinGuarantor,
                    parameters.Suspense,
                    parameters.RetainedEarnings,
                    parameters.Creditors,
                    Action = action,
                    PerformedBy = performedBy,
                    PerformedAt = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    $"SACCO_PARAMS_{action}",
                    parameters.CompanyCode,
                    parameters.CompanyCode,
                    0,
                    parameters.Id.ToString(),
                    blockchainData);

                if (blockchainTx != null)
                {
                    parameters.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"SACCO parameters recorded on blockchain: {blockchainTx.TransactionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record SACCO parameters on blockchain");
            }
        }
    }
}

