using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface IGIGsService
    {
        Task<GIGsResponseDTO> CreateGIGAsync(GIGsDTO gigDto);
        Task<GIGsResponseDTO> UpdateGIGAsync(int id, GIGsDTO gigDto);
        Task<bool> DeleteGIGAsync(int id);
        Task<GIGsResponseDTO> GetGIGByIdAsync(int id);
        Task<GIGsResponseDTO> GetGIGByCodeAsync(string gigCode);
        Task<List<GIGsResponseDTO>> GetAllGIGsAsync(string search = null);
        Task<string> GenerateGIGCodeAsync(string companyCode);
        Task<bool> IsGIGCodeUniqueAsync(string gigCode, string companyCode, int? excludeId = null);
        Task<List<GIGsResponseDTO>> GetGIGsByCompanyAsync(string companyCode);
    }

    public class GIGsService : IGIGsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<GIGsService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public GIGsService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<GIGsService> logger,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        public async Task<GIGsResponseDTO> CreateGIGAsync(GIGsDTO gigDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get current user's company code from context
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                _logger.LogInformation($"Creating new GIG for company: {currentCompanyCode}, Name: {gigDto.GigName}");

                // Validate company code exists
                if (string.IsNullOrEmpty(currentCompanyCode))
                {
                    throw new InvalidOperationException("Company code not found for current user.");
                }

                // Set the company code from logged-in user
                gigDto.CompanyCode = currentCompanyCode;

                // Generate GIG code if not provided
                if (string.IsNullOrEmpty(gigDto.GigCode))
                {
                    gigDto.GigCode = await GenerateGIGCodeAsync(currentCompanyCode);
                }

                // Check if GIG code already exists for this company
                if (!await IsGIGCodeUniqueAsync(gigDto.GigCode, currentCompanyCode))
                {
                    throw new InvalidOperationException($"GIG code {gigDto.GigCode} already exists for this company.");
                }

                // Get company name for display
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == currentCompanyCode);
                var companyName = company?.CompanyName;

                var gig = new GIGs
                {
                    GigCode = gigDto.GigCode,
                    GigName = gigDto.GigName,
                    CompanyCode = currentCompanyCode,
                    ContactPhone = gigDto.ContactPhone,
                    ContactEmail = gigDto.ContactEmail,
                    Chairperson = gigDto.Chairperson,
                    RegistrationDate = gigDto.RegistrationDate ?? DateTime.Now,
                    TotalMembers = gigDto.TotalMembers ?? 0,
                    Status = gigDto.Status ?? "Active",
                    CreatedBy = currentUser,
                    CreatedAt = DateTime.Now
                };

                _context.CIGs.Add(gig);
                await _context.SaveChangesAsync();

                // Create blockchain transaction (don't let it fail the whole operation)
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        GigId = gig.Id,
                        GigCode = gig.GigCode,
                        GigName = gig.GigName,
                        CompanyCode = currentCompanyCode,
                        CompanyName = companyName,
                        ContactPhone = gig.ContactPhone,
                        ContactEmail = gig.ContactEmail,
                        Chairperson = gig.Chairperson,
                        RegistrationDate = gig.RegistrationDate,
                        TotalMembers = gig.TotalMembers,
                        Action = "CREATE",
                        CreatedBy = currentUser,
                        CreatedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "GIG_CREATE",
                        gig.GigCode,
                        currentCompanyCode,
                        0,
                        gig.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        gig.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for GIG {gig.GigCode}: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for GIG {gig.GigCode} - continuing with creation");
                    // Don't throw - GIG was created successfully, just blockchain failed
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"GIG {gig.GigCode} created successfully");

                return MapToResponseDTO(gig, companyName, blockchainTxId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating GIG");
                throw;
            }
        }

        //public async Task<GIGsResponseDTO> CreateGIGAsync(GIGsDTO gigDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // Get current user's company code from context
        //        var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
        //        var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

        //        _logger.LogInformation($"Creating new GIG for company: {currentCompanyCode}, Name: {gigDto.GigName}");

        //        // Set the company code from logged-in user
        //        gigDto.CompanyCode = currentCompanyCode;

        //        // Check if GIG code already exists for this company
        //        if (!await IsGIGCodeUniqueAsync(gigDto.GigCode, currentCompanyCode))
        //        {
        //            throw new InvalidOperationException($"GIG code {gigDto.GigCode} already exists for this company.");
        //        }

        //        // Get company name for display
        //        var company = await _context.Companies
        //            .FirstOrDefaultAsync(c => c.CompanyCode == currentCompanyCode);
        //        var companyName = company?.CompanyName;

        //        var gig = new GIGs
        //        {
        //            GigCode = gigDto.GigCode,
        //            GigName = gigDto.GigName,
        //            CompanyCode = currentCompanyCode,
        //            ContactPhone = gigDto.ContactPhone,
        //            ContactEmail = gigDto.ContactEmail,
        //            Chairperson = gigDto.Chairperson,
        //            RegistrationDate = gigDto.RegistrationDate ?? DateTime.Now,
        //            TotalMembers = gigDto.TotalMembers ?? 0,
        //            Status = gigDto.Status ?? "Active",
        //            CreatedBy = currentUser,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.CIGs.Add(gig);
        //        await _context.SaveChangesAsync();

        //        // Create blockchain transaction
        //        string blockchainTxId = null;
        //        try
        //        {
        //            var blockchainData = new
        //            {
        //                GigId = gig.Id,
        //                GigCode = gig.GigCode,
        //                GigName = gig.GigName,
        //                CompanyCode = currentCompanyCode,
        //                CompanyName = companyName,
        //                ContactPhone = gig.ContactPhone,
        //                ContactEmail = gig.ContactEmail,
        //                Chairperson = gig.Chairperson,
        //                RegistrationDate = gig.RegistrationDate,
        //                TotalMembers = gig.TotalMembers,
        //                Action = "CREATE",
        //                CreatedBy = currentUser,
        //                CreatedAt = DateTime.Now
        //            };

        //            var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
        //                "GIG_CREATE",
        //                gig.GigCode,
        //                currentCompanyCode,
        //                0,
        //                gig.Id.ToString(),
        //                blockchainData);

        //            if (blockchainTx != null)
        //            {
        //                blockchainTxId = blockchainTx.TransactionId;
        //                gig.BlockchainTxId = blockchainTxId;
        //                await _context.SaveChangesAsync();
        //                _logger.LogInformation($"Blockchain transaction recorded for GIG {gig.GigCode}: {blockchainTxId}");
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError(ex, $"Failed to record blockchain transaction for GIG {gig.GigCode}");
        //        }

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"GIG {gig.GigCode} created successfully");

        //        return MapToResponseDTO(gig, companyName, blockchainTxId);
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, "Error creating GIG");
        //        throw;
        //    }
        //}

        public async Task<GIGsResponseDTO> UpdateGIGAsync(int id, GIGsDTO gigDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                _logger.LogInformation($"Updating GIG ID: {id} for company: {currentCompanyCode}");

                var gig = await _context.CIGs
                    .FirstOrDefaultAsync(g => g.Id == id && g.CompanyCode == currentCompanyCode);

                if (gig == null)
                {
                    throw new InvalidOperationException($"GIG with ID {id} not found for this company.");
                }

                // Store old values for blockchain record
                var oldValues = new
                {
                    gig.GigCode,
                    gig.GigName,
                    gig.ContactPhone,
                    gig.ContactEmail,
                    gig.Chairperson,
                    gig.TotalMembers,
                    gig.Status
                };

                // Get company name for display
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == currentCompanyCode);
                var companyName = company?.CompanyName;

                // Update GIG details (CompanyCode remains the same)
                gig.GigCode = gigDto.GigCode;
                gig.GigName = gigDto.GigName;
                gig.ContactPhone = gigDto.ContactPhone;
                gig.ContactEmail = gigDto.ContactEmail;
                gig.Chairperson = gigDto.Chairperson;
                gig.RegistrationDate = gigDto.RegistrationDate ?? gig.RegistrationDate;
                gig.TotalMembers = gigDto.TotalMembers ?? gig.TotalMembers;
                gig.Status = gigDto.Status ?? gig.Status;
                gig.ModifiedBy = currentUser;
                gig.ModifiedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Create blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        GigId = gig.Id,
                        Action = "UPDATE",
                        OldValues = oldValues,
                        NewValues = new
                        {
                            gig.GigCode,
                            gig.GigName,
                            gig.ContactPhone,
                            gig.ContactEmail,
                            gig.Chairperson,
                            gig.TotalMembers,
                            gig.Status
                        },
                        ModifiedBy = currentUser,
                        ModifiedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "GIG_UPDATE",
                        gig.GigCode,
                        currentCompanyCode,
                        0,
                        gig.Id.ToString(),
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        gig.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded for GIG update: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for GIG update");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"GIG {gig.GigCode} updated successfully");

                return MapToResponseDTO(gig, companyName, blockchainTxId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating GIG");
                throw;
            }
        }

        public async Task<bool> DeleteGIGAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var currentUser = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                var gig = await _context.CIGs
                    .FirstOrDefaultAsync(g => g.Id == id && g.CompanyCode == currentCompanyCode);

                if (gig == null)
                {
                    throw new InvalidOperationException($"GIG with ID {id} not found for this company.");
                }

                // Store GIG details for blockchain record
                var gigDetails = new
                {
                    gig.Id,
                    gig.GigCode,
                    gig.GigName,
                    gig.CompanyCode,
                    gig.ContactPhone,
                    gig.ContactEmail,
                    gig.Chairperson
                };

                _context.CIGs.Remove(gig);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "DELETE",
                        GIGDetails = gigDetails,
                        DeletedBy = currentUser,
                        DeletedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "GIG_DELETE",
                        gig.GigCode,
                        currentCompanyCode,
                        0,
                        gig.Id.ToString(),
                        blockchainData);

                    _logger.LogInformation($"Blockchain transaction recorded for GIG deletion: {blockchainTx?.TransactionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to record blockchain transaction for GIG deletion");
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"GIG {gig.GigCode} deleted successfully");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting GIG");
                throw;
            }
        }

        public async Task<GIGsResponseDTO> GetGIGByIdAsync(int id)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            var gig = await _context.CIGs
                .Include(g => g.Company)
                .FirstOrDefaultAsync(g => g.Id == id && g.CompanyCode == currentCompanyCode);

            if (gig == null)
            {
                return null;
            }

            return MapToResponseDTO(gig, gig.Company?.CompanyName, gig.BlockchainTxId);
        }

        public async Task<GIGsResponseDTO> GetGIGByCodeAsync(string gigCode)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            var gig = await _context.CIGs
                .Include(g => g.Company)
                .FirstOrDefaultAsync(g => g.GigCode == gigCode && g.CompanyCode == currentCompanyCode);

            if (gig == null)
            {
                return null;
            }

            return MapToResponseDTO(gig, gig.Company?.CompanyName, gig.BlockchainTxId);
        }

        public async Task<List<GIGsResponseDTO>> GetAllGIGsAsync(string search = null)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            var query = _context.CIGs
                .Include(g => g.Company)
                .Where(g => g.CompanyCode == currentCompanyCode)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(g =>
                    g.GigCode.Contains(search) ||
                    (g.GigName != null && g.GigName.Contains(search)) ||
                    (g.ContactPhone != null && g.ContactPhone.Contains(search)) ||
                    (g.ContactEmail != null && g.ContactEmail.Contains(search)) ||
                    (g.Chairperson != null && g.Chairperson.Contains(search)));
            }

            var gigs = await query
                .OrderBy(g => g.GigName)
                .ToListAsync();

            return gigs.Select(g => MapToResponseDTO(g, g.Company?.CompanyName, g.BlockchainTxId)).ToList();
        }

        public async Task<List<GIGsResponseDTO>> GetGIGsByCompanyAsync(string companyCode)
        {
            var gigs = await _context.CIGs
                .Include(g => g.Company)
                .Where(g => g.CompanyCode == companyCode)
                .OrderBy(g => g.GigName)
                .ToListAsync();

            return gigs.Select(g => MapToResponseDTO(g, g.Company?.CompanyName, g.BlockchainTxId)).ToList();
        }

        public async Task<string> GenerateGIGCodeAsync(string companyCode)
        {
            var prefix = companyCode?.Substring(0, Math.Min(3, companyCode.Length)) ?? "GIG";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastGIG = await _context.CIGs
                .Where(g => g.CompanyCode == companyCode && g.GigCode.StartsWith($"{prefix}{date}"))
                .OrderByDescending(g => g.GigCode)
                .FirstOrDefaultAsync();

            if (lastGIG != null && lastGIG.GigCode.Length >= prefix.Length + date.Length + 3)
            {
                var sequenceStr = lastGIG.GigCode.Substring(lastGIG.GigCode.Length - 3);
                if (int.TryParse(sequenceStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D3}";
        }

        public async Task<bool> IsGIGCodeUniqueAsync(string gigCode, string companyCode, int? excludeId = null)
        {
            var query = _context.CIGs.Where(g => g.GigCode == gigCode && g.CompanyCode == companyCode);

            if (excludeId.HasValue)
            {
                query = query.Where(g => g.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        private GIGsResponseDTO MapToResponseDTO(GIGs gig, string companyName, string blockchainTxId)
        {
            return new GIGsResponseDTO
            {
                Id = gig.Id,
                GigCode = gig.GigCode,
                GigName = gig.GigName,
                CompanyCode = gig.CompanyCode,
                CompanyName = companyName,
                ContactPhone = gig.ContactPhone,
                ContactEmail = gig.ContactEmail,
                Chairperson = gig.Chairperson,
                RegistrationDate = gig.RegistrationDate,
                TotalMembers = gig.TotalMembers,
                Status = gig.Status,
                CreatedBy = gig.CreatedBy,
                CreatedAt = gig.CreatedAt,
                ModifiedBy = gig.ModifiedBy,
                ModifiedAt = gig.ModifiedAt,
                BlockchainTxId = blockchainTxId
            };
        }
    }
}