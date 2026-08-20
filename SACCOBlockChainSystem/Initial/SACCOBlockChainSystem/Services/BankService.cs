using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.Text.Json;

namespace SACCOBlockChainSystem.Services
{
    public interface IBankService
    {
        Task<BankResponseDTO> CreateBankAsync(BankDTO bankDto);
        Task<BankResponseDTO> UpdateBankAsync(int id, BankDTO bankDto);
        Task<bool> DeleteBankAsync(int id);
        Task<bool> ToggleBankStatusAsync(int id);
        Task<BankResponseDTO> GetBankByIdAsync(int id);
        Task<List<BankResponseDTO>> GetAllBanksAsync(string search = null, bool showInactive = false);
        Task<BankResponseDTO> GetBankByCodeAsync(string bankCode, string companyCode);

        // NEW: Get GL accounts for dropdown
        Task<List<GlAccountDropdownDTO>> GetGlAccountsForDropdownAsync();
    }

    public class BankService : IBankService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<BankService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public BankService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<BankService> logger,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        public async Task<List<GlAccountDropdownDTO>> GetGlAccountsForDropdownAsync()
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();

            var glAccounts = await _context.GlSetup
                .Where(g => g.CompanyCode == companyCode && g.Status == true)
                .OrderBy(g => g.AccNo)
                .Select(g => new GlAccountDropdownDTO
                {
                    AccNo = g.AccNo,
                    Glaccname = g.Glaccname ?? "",
                    Glacctype = g.Glacctype
                })
                .ToListAsync();

            return glAccounts;
        }

        public async Task<BankResponseDTO> CreateBankAsync(BankDTO bankDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Creating new bank: {bankDto.BankCode} - {bankDto.BankName}");

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var currentUser = _companyContextService.GetCurrentUserName();

                // Check if bank code already exists
                var existingBank = await _context.Banks
                    .FirstOrDefaultAsync(b => b.BankCode == bankDto.BankCode && b.CompanyCode == companyCode);

                if (existingBank != null)
                {
                    throw new InvalidOperationException($"Bank with code {bankDto.BankCode} already exists.");
                }

                // Check if account number already exists
                var existingAccount = await _context.Banks
                    .FirstOrDefaultAsync(b => b.AccountNumber == bankDto.AccountNumber && b.CompanyCode == companyCode);

                if (existingAccount != null)
                {
                    throw new InvalidOperationException($"Account number {bankDto.AccountNumber} is already registered.");
                }

                // Get GL Account details if selected
                string glAccountName = null;
                if (!string.IsNullOrEmpty(bankDto.GlAccountNo))
                {
                    var glAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.AccNo == bankDto.GlAccountNo && g.CompanyCode == companyCode);

                    if (glAccount != null)
                    {
                        glAccountName = glAccount.Glaccname;
                    }
                    else
                    {
                        _logger.LogWarning($"GL Account {bankDto.GlAccountNo} not found");
                    }
                }

                var bank = new Bank
                {
                    BankCode = bankDto.BankCode,
                    BankName = bankDto.BankName,
                    AccountNumber = bankDto.AccountNumber,
                    AccountName = bankDto.AccountName,
                    Branch = bankDto.Branch,
                    SwiftCode = bankDto.SwiftCode,
                    SortCode = bankDto.SortCode,
                    GlAccountNo = bankDto.GlAccountNo,
                    GlAccountName = glAccountName,
                    CompanyCode = companyCode,
                    IsActive = bankDto.IsActive,
                    Notes = bankDto.Notes,
                    CreatedBy = currentUser,
                    CreatedAt = DateTime.Now
                };

                _context.Banks.Add(bank);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        BankId = bank.Id,
                        BankCode = bank.BankCode,
                        BankName = bank.BankName,
                        AccountNumber = bank.AccountNumber,
                        AccountName = bank.AccountName,
                        Branch = bank.Branch,
                        SwiftCode = bank.SwiftCode,
                        SortCode = bank.SortCode,
                        GlAccountNo = bank.GlAccountNo,
                        GlAccountName = bank.GlAccountName,
                        CompanyCode = bank.CompanyCode,
                        IsActive = bank.IsActive,
                        Action = "CREATE",
                        CreatedBy = bank.CreatedBy,
                        CreatedAt = bank.CreatedAt
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "BANK_CREATE",
                        bank.BankCode,
                        bank.CompanyCode,
                        0,
                        bank.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        bank.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for bank {bank.BankCode}: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for bank {bank.BankCode}");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Bank {bank.BankCode} created successfully");

                return MapToResponseDTO(bank, blockchainTxId);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<BankResponseDTO> UpdateBankAsync(int id, BankDTO bankDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Updating bank ID: {id}");

                var bank = await _context.Banks.FindAsync(id);
                if (bank == null)
                {
                    throw new InvalidOperationException($"Bank with ID {id} not found.");
                }

                var currentUser = _companyContextService.GetCurrentUserName();
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Store old values for blockchain record
                var oldValues = new
                {
                    bank.BankCode,
                    bank.BankName,
                    bank.AccountNumber,
                    bank.AccountName,
                    bank.Branch,
                    bank.SwiftCode,
                    bank.SortCode,
                    bank.GlAccountNo,
                    bank.GlAccountName,
                    bank.IsActive
                };

                // Get GL Account details if selected
                string glAccountName = null;
                if (!string.IsNullOrEmpty(bankDto.GlAccountNo))
                {
                    var glAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.AccNo == bankDto.GlAccountNo && g.CompanyCode == companyCode);

                    if (glAccount != null)
                    {
                        glAccountName = glAccount.Glaccname;
                    }
                }

                // Update bank details
                bank.BankCode = bankDto.BankCode;
                bank.BankName = bankDto.BankName;
                bank.AccountNumber = bankDto.AccountNumber;
                bank.AccountName = bankDto.AccountName;
                bank.Branch = bankDto.Branch;
                bank.SwiftCode = bankDto.SwiftCode;
                bank.SortCode = bankDto.SortCode;
                bank.GlAccountNo = bankDto.GlAccountNo;
                bank.GlAccountName = glAccountName;
                bank.IsActive = bankDto.IsActive;
                bank.Notes = bankDto.Notes;
                bank.ModifiedBy = currentUser;
                bank.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Create blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        BankId = bank.Id,
                        Action = "UPDATE",
                        OldValues = oldValues,
                        NewValues = new
                        {
                            bank.BankCode,
                            bank.BankName,
                            bank.AccountNumber,
                            bank.AccountName,
                            bank.Branch,
                            bank.SwiftCode,
                            bank.SortCode,
                            bank.GlAccountNo,
                            bank.GlAccountName,
                            bank.IsActive
                        },
                        ModifiedBy = bank.ModifiedBy,
                        ModifiedAt = bank.ModifiedAt
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "BANK_UPDATE",
                        bank.BankCode,
                        bank.CompanyCode,
                        0,
                        bank.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        bank.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for bank update: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for bank update");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Bank {bank.BankCode} updated successfully");

                return MapToResponseDTO(bank, blockchainTxId);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ... (other methods remain the same)

        public async Task<bool> DeleteBankAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var bank = await _context.Banks.FindAsync(id);
                if (bank == null)
                {
                    throw new InvalidOperationException($"Bank with ID {id} not found.");
                }

                var currentUser = _companyContextService.GetCurrentUserName();

                var bankDetails = new
                {
                    bank.Id,
                    bank.BankCode,
                    bank.BankName,
                    bank.AccountNumber,
                    bank.AccountName,
                    bank.Branch,
                    bank.SwiftCode,
                    bank.SortCode,
                    bank.GlAccountNo,
                    bank.GlAccountName,
                    bank.CompanyCode,
                    bank.IsActive
                };

                _context.Banks.Remove(bank);
                await _context.SaveChangesAsync();

                try
                {
                    var blockchainData = new
                    {
                        Action = "DELETE",
                        BankDetails = bankDetails,
                        DeletedBy = currentUser,
                        DeletedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "BANK_DELETE",
                        bank.BankCode,
                        bank.CompanyCode,
                        0,
                        bank.Id.ToString(),
                        blockchainData);

                    _logger.LogInformation($"Blockchain transaction recorded for bank deletion: {blockchainTx?.TransactionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for bank deletion");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Bank {bank.BankCode} deleted successfully");
                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> ToggleBankStatusAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var bank = await _context.Banks.FindAsync(id);
                if (bank == null)
                {
                    throw new InvalidOperationException($"Bank with ID {id} not found.");
                }

                var currentUser = _companyContextService.GetCurrentUserName();
                var oldStatus = bank.IsActive;
                bank.IsActive = !bank.IsActive;
                bank.ModifiedBy = currentUser;
                bank.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                try
                {
                    var blockchainData = new
                    {
                        BankId = bank.Id,
                        BankCode = bank.BankCode,
                        BankName = bank.BankName,
                        Action = "TOGGLE_STATUS",
                        OldStatus = oldStatus,
                        NewStatus = bank.IsActive,
                        ModifiedBy = bank.ModifiedBy,
                        ModifiedAt = bank.ModifiedAt
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "BANK_STATUS_TOGGLE",
                        bank.BankCode,
                        bank.CompanyCode,
                        0,
                        bank.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        bank.BlockchainTxId = blockchainTx.TransactionId;
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for bank status toggle");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Bank {bank.BankCode} status toggled to {bank.IsActive}");
                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<BankResponseDTO> GetBankByIdAsync(int id)
        {
            var bank = await _context.Banks.FindAsync(id);
            if (bank == null)
            {
                return null;
            }

            return MapToResponseDTO(bank, bank.BlockchainTxId);
        }

        public async Task<List<BankResponseDTO>> GetAllBanksAsync(string search = null, bool showInactive = false)
        {
            var query = _context.Banks.AsQueryable();

            var companyCode = _companyContextService.GetCurrentCompanyCode();
            query = query.Where(b => b.CompanyCode == companyCode);

            if (!showInactive)
            {
                query = query.Where(b => b.IsActive == true);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(b =>
                    b.BankCode.Contains(search) ||
                    b.BankName.Contains(search) ||
                    b.AccountNumber.Contains(search) ||
                    (b.Branch != null && b.Branch.Contains(search)) ||
                    (b.AccountName != null && b.AccountName.Contains(search)) ||
                    (b.GlAccountNo != null && b.GlAccountNo.Contains(search)));
            }

            var banks = await query
                .OrderBy(b => b.BankName)
                .ToListAsync();

            return banks.Select(b => MapToResponseDTO(b, b.BlockchainTxId)).ToList();
        }

        public async Task<BankResponseDTO> GetBankByCodeAsync(string bankCode, string companyCode)
        {
            var bank = await _context.Banks
                .FirstOrDefaultAsync(b => b.BankCode == bankCode && b.CompanyCode == companyCode);

            if (bank == null)
            {
                return null;
            }

            return MapToResponseDTO(bank, bank.BlockchainTxId);
        }

        private BankResponseDTO MapToResponseDTO(Bank bank, string blockchainTxId)
        {
            return new BankResponseDTO
            {
                Id = bank.Id,
                BankCode = bank.BankCode,
                BankName = bank.BankName,
                AccountNumber = bank.AccountNumber,
                AccountName = bank.AccountName,
                Branch = bank.Branch,
                SwiftCode = bank.SwiftCode,
                SortCode = bank.SortCode,
                GlAccountNo = bank.GlAccountNo,
                GlAccountName = bank.GlAccountName,
                IsActive = bank.IsActive,
                Notes = bank.Notes,
                CreatedBy = bank.CreatedBy,
                CreatedAt = bank.CreatedAt,
                ModifiedBy = bank.ModifiedBy,
                ModifiedAt = bank.ModifiedAt,
                BlockchainTxId = blockchainTxId
            };
        }
    }
}