using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface ICompanyService
    {
        Task<CompanyResponseDTO> CreateCompanyAsync(CompanyDTO companyDto);
        Task<CompanyResponseDTO> UpdateCompanyAsync(int id, CompanyDTO companyDto);
        Task<bool> DeleteCompanyAsync(int id);
        Task<CompanyResponseDTO> GetCompanyByIdAsync(int id);
        Task<CompanyResponseDTO> GetCompanyByCodeAsync(string companyCode);
        Task<List<CompanyResponseDTO>> GetAllCompaniesAsync(string search = null);
        Task<string> GenerateCompanyCodeAsync();
        Task<List<object>> GetAllCompaniesForDropdownAsync();
        Task<bool> IsCompanyCodeUniqueAsync(string companyCode, int? excludeId = null);
    }

    public class CompanyService : ICompanyService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<CompanyService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public CompanyService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<CompanyService> logger,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        public async Task<List<object>> GetAllCompaniesForDropdownAsync()
        {
            try
            {
                return await _context.Companies
                    .Where(c => c.Project == true)
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new
                    {
                        c.CompanyCode,
                        DisplayText = $"{c.CompanyCode} - {c.CompanyName}"
                    })
                    .ToListAsync<object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetAllCompaniesForDropdownAsync");
                return new List<object>();
            }
        }

        public async Task<CompanyResponseDTO> CreateCompanyAsync(CompanyDTO companyDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Creating new company: {companyDto.CompanyName}");

                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                // Check if company code already exists
                if (!await IsCompanyCodeUniqueAsync(companyDto.CompanyCode))
                {
                    throw new InvalidOperationException($"Company code {companyDto.CompanyCode} already exists.");
                }

                // Get location names if IDs are provided
                string countyName = null;
                string subCountyName = null;
                string wardName = null;

                var company = new Company
                {
                    CompanyCode = companyDto.CompanyCode,
                    CompanyName = companyDto.CompanyName,
                    Contactperson = companyDto.Contactperson,
                    Telephone = companyDto.Telephone,
                    Email = companyDto.Email,
                    Address = companyDto.Address,
                    NoEmployees = companyDto.NoEmployees,
                    County = countyName ?? companyDto.County,
                    SubCounty = subCountyName ?? companyDto.SubCounty,
                    Ward = wardName ?? companyDto.Ward,
                    Village = companyDto.Village,
                    Cigcode = companyDto.Cigcode,
                    CountyCode = companyDto.CountyCode,
                    Unitcode = companyDto.Unitcode,
                    AccountNo = companyDto.AccountNo,
                    NoYears = companyDto.NoYears,
                    Location = companyDto.Location,
                    Type = companyDto.Type,
                    Capital = companyDto.Capital,
                    Project = companyDto.Project,
                    AuditId = currentUser,
                    AuditTime = DateTime.Now
                };

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        CompanyId = company.Id,
                        CompanyCode = company.CompanyCode,
                        CompanyName = company.CompanyName,
                        Email = company.Email,
                        Contactperson = company.Contactperson,
                        Telephone = company.Telephone,
                        Address = company.Address,
                        Location = company.Location,
                        County = company.County,
                        SubCounty = company.SubCounty,
                        Ward = company.Ward,
                        NoEmployees = company.NoEmployees,
                        Action = "CREATE",
                        CreatedBy = currentUser,
                        CreatedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "COMPANY_CREATE",
                        company.CompanyCode,
                        company.CompanyCode,
                        0,
                        company.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        company.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for company {company.CompanyCode}: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for company {company.CompanyCode}");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Company {company.CompanyCode} created successfully");

                return MapToResponseDTO(company, blockchainTxId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating company");
                throw;
            }
        }

        public async Task<CompanyResponseDTO> UpdateCompanyAsync(int id, CompanyDTO companyDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Updating company ID: {id}");

                var company = await _context.Companies.FindAsync(id);
                if (company == null)
                {
                    throw new InvalidOperationException($"Company with ID {id} not found.");
                }

                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                // Get location names if IDs are provided
                string countyName = null;
                string subCountyName = null;
                string wardName = null;

                // Store old values for blockchain record
                var oldValues = new
                {
                    company.CompanyCode,
                    company.CompanyName,
                    company.Email,
                    company.Contactperson,
                    company.Telephone,
                    company.Address,
                    company.Location
                };

                // Update company details
                company.CompanyCode = companyDto.CompanyCode;
                company.CompanyName = companyDto.CompanyName;
                company.Contactperson = companyDto.Contactperson;
                company.Telephone = companyDto.Telephone;
                company.Email = companyDto.Email;
                company.Address = companyDto.Address;
                company.NoEmployees = companyDto.NoEmployees;
                company.County = countyName ?? companyDto.County;
                company.SubCounty = subCountyName ?? companyDto.SubCounty;
                company.Ward = wardName ?? companyDto.Ward;
                company.Village = companyDto.Village;
                company.Cigcode = companyDto.Cigcode;
                company.CountyCode = companyDto.CountyCode;
                company.Unitcode = companyDto.Unitcode;
                company.AccountNo = companyDto.AccountNo;
                company.NoYears = companyDto.NoYears;
                company.Location = companyDto.Location;
                company.Type = companyDto.Type;
                company.Capital = companyDto.Capital;
                company.Project = companyDto.Project;
                company.AuditId = currentUser;
                company.AuditTime = DateTime.Now;

                await _context.SaveChangesAsync();

                // Create blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        CompanyId = company.Id,
                        Action = "UPDATE",
                        OldValues = oldValues,
                        NewValues = new
                        {
                            company.CompanyCode,
                            company.CompanyName,
                            company.Email,
                            company.Contactperson,
                            company.Telephone,
                            company.Address,
                            company.Location
                        },
                        ModifiedBy = currentUser,
                        ModifiedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "COMPANY_UPDATE",
                        company.CompanyCode,
                        company.CompanyCode,
                        0,
                        company.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        company.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for company update: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for company update");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Company {company.CompanyCode} updated successfully");

                return MapToResponseDTO(company, blockchainTxId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating company");
                throw;
            }
        }

        public async Task<bool> DeleteCompanyAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var company = await _context.Companies.FindAsync(id);
                if (company == null)
                {
                    throw new InvalidOperationException($"Company with ID {id} not found.");
                }

                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                // Store company details for blockchain record
                var companyDetails = new
                {
                    company.Id,
                    company.CompanyCode,
                    company.CompanyName,
                    company.Email,
                    company.Contactperson,
                    company.Telephone,
                    company.Address,
                    company.Location
                };

                _context.Companies.Remove(company);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "DELETE",
                        CompanyDetails = companyDetails,
                        DeletedBy = currentUser,
                        DeletedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "COMPANY_DELETE",
                        company.CompanyCode,
                        company.CompanyCode,
                        0,
                        company.Id.ToString(),
                        blockchainData);

                    _logger.LogInformation($"Blockchain transaction recorded for company deletion: {blockchainTx?.TransactionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for company deletion");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Company {company.CompanyCode} deleted successfully");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting company");
                throw;
            }
        }

        public async Task<CompanyResponseDTO> GetCompanyByIdAsync(int id)
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Id == id); 

            if (company == null) return null;

            return MapToResponseDTO(company, company.BlockchainTxId);
        }

        public async Task<CompanyResponseDTO> GetCompanyByCodeAsync(string companyCode)
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

            if (company == null)
            {
                return null;
            }

            return MapToResponseDTO(company, company.BlockchainTxId);
        }

        public async Task<List<CompanyResponseDTO>> GetAllCompaniesAsync(string search = null)
        {
            var query = _context.Companies.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c =>
                    c.CompanyCode.Contains(search) ||
                    (c.CompanyName != null && c.CompanyName.Contains(search)) ||
                    (c.Email != null && c.Email.Contains(search)) ||
                    (c.Contactperson != null && c.Contactperson.Contains(search)) ||
                    (c.Telephone != null && c.Telephone.Contains(search)));
            }

            var companies = await query
                .OrderBy(c => c.CompanyName)
                .ToListAsync();

            return companies.Select(c => MapToResponseDTO(c, c.BlockchainTxId)).ToList();
        }


        //public async Task<CompanyResponseDTO> GetCompanyByIdAsync(int id)
        //{
        //    var company = await _context.Companies
        //        .Include(c => c.CountyNavigation)
        //        .Include(c => c.SubCountyNavigation)
        //        .Include(c => c.WardNavigation)
        //        .FirstOrDefaultAsync(c => c.Id == id);

        //    if (company == null)
        //    {
        //        return null;
        //    }

        //    return MapToResponseDTO(company, company.BlockchainTxId);
        //}

        //public async Task<CompanyResponseDTO> GetCompanyByCodeAsync(string companyCode)
        //{
        //    var company = await _context.Companies
        //        .Include(c => c.CountyNavigation)
        //        .Include(c => c.SubCountyNavigation)
        //        .Include(c => c.WardNavigation)
        //        .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

        //    if (company == null)
        //    {
        //        return null;
        //    }

        //    return MapToResponseDTO(company, company.BlockchainTxId);
        //}

        //public async Task<List<CompanyResponseDTO>> GetAllCompaniesAsync(string search = null)
        //{
        //    var query = _context.Companies
        //        .Include(c => c.CountyNavigation)
        //        .Include(c => c.SubCountyNavigation)
        //        .Include(c => c.WardNavigation)
        //        .AsQueryable();

        //    if (!string.IsNullOrEmpty(search))
        //    {
        //        query = query.Where(c =>
        //            c.CompanyCode.Contains(search) ||
        //            (c.CompanyName != null && c.CompanyName.Contains(search)) ||
        //            (c.Email != null && c.Email.Contains(search)) ||
        //            (c.Contactperson != null && c.Contactperson.Contains(search)) ||
        //            (c.Telephone != null && c.Telephone.Contains(search)));
        //    }

        //    var companies = await query
        //        .OrderBy(c => c.CompanyName)
        //        .ToListAsync();

        //    return companies.Select(c => MapToResponseDTO(c, c.BlockchainTxId)).ToList();
        //}

        public async Task<string> GenerateCompanyCodeAsync()
        {
            var prefix = "SACCO";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastCompany = await _context.Companies
                .Where(c => c.CompanyCode.StartsWith($"{prefix}{date}"))
                .OrderByDescending(c => c.CompanyCode)
                .FirstOrDefaultAsync();

            if (lastCompany != null && lastCompany.CompanyCode.Length >= prefix.Length + date.Length + 3)
            {
                var sequenceStr = lastCompany.CompanyCode.Substring(lastCompany.CompanyCode.Length - 3);
                if (int.TryParse(sequenceStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D3}";
        }

        public async Task<bool> IsCompanyCodeUniqueAsync(string companyCode, int? excludeId = null)
        {
            var query = _context.Companies.Where(c => c.CompanyCode == companyCode);

            if (excludeId.HasValue)
            {
                query = query.Where(c => c.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        private CompanyResponseDTO MapToResponseDTO(Company company, string blockchainTxId)
        {
            return new CompanyResponseDTO
            {
                Id = company.Id,
                CompanyCode = company.CompanyCode,
                CompanyName = company.CompanyName,
                Contactperson = company.Contactperson,
                Telephone = company.Telephone,
                Email = company.Email,
                Address = company.Address,
                NoEmployees = company.NoEmployees,
                County = company.County,
                SubCounty = company.SubCounty,
                Ward = company.Ward,
                Village = company.Village,
                Cigcode = company.Cigcode,
                CountyCode = company.CountyCode,
                Unitcode = company.Unitcode,
                AccountNo = company.AccountNo,
                NoYears = company.NoYears,
                Location = company.Location,
                Type = company.Type,
                Capital = company.Capital,
                Project = company.Project,
                AuditId = company.AuditId,
                AuditTime = company.AuditTime,
                CreatedBy = company.AuditId,
                CreatedAt = company.AuditTime ?? DateTime.Now,
                ModifiedBy = company.AuditId,
                ModifiedAt = company.AuditTime,
                BlockchainTxId = blockchainTxId,
                BusinessStatus = "Active"
            };
        }
    }
}