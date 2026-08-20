// Services/AgentService.cs - Updated
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.Text.Json;
using SACCOBlockchainDb.Models;

namespace SACCOBlockChainSystem.Services
{
    public interface IAgentService
    {
        Task<AgentResponseDTO> CreateAsync(AgentDTO dto, string userId);
        Task<AgentResponseDTO> UpdateAsync(string idNo, AgentDTO dto, string userId);
        Task<bool> DeleteAsync(string idNo, string userId);
        Task<AgentResponseDTO> GetByIdNoAsync(string idNo);
        Task<AgentResponseDTO> GetByIdAsync(long id);
        Task<List<AgentResponseDTO>> GetAllAsync(string companyCode);
        Task<bool> IsIdNoUniqueAsync(string idNo, string companyCode, string? excludeIdNo = null);
        Task<List<AgentSimpleDTO>> GetAgentsForDropdownAsync(string companyCode);
        Task<List<string>> GetRecruitmentAgentTypesAsync();  // New method for dropdown options
    }

    public class AgentService : IAgentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<AgentService> _logger;

        // Predefined recruitment agent types
        private static readonly List<string> _recruitmentAgentTypes = new()
        {
            "Staff",
            "Agent",
            "Agri-prenour",
            "Board Member"
        };

        public AgentService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<AgentService> logger)
        {
            _context = context;
            _blockchainService = blockchainService;
            _logger = logger;
        }

        public async Task<List<string>> GetRecruitmentAgentTypesAsync()
        {
            return await Task.FromResult(_recruitmentAgentTypes);
        }

        public async Task<AgentResponseDTO> CreateAsync(AgentDTO dto, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Creating new agent: {dto.Names}");

                // Validate required fields
                if (string.IsNullOrEmpty(dto.IdNo))
                    throw new InvalidOperationException("ID Number is required");

                if (string.IsNullOrEmpty(dto.Names))
                    throw new InvalidOperationException("Agent names are required");

                // Validate Recruitment Agent Type is selected
                if (string.IsNullOrEmpty(dto.RecruitementAgents))
                    throw new InvalidOperationException("Recruitment Agent Type is required");

                // Check if ID Number is unique
                if (!await IsIdNoUniqueAsync(dto.IdNo, dto.CompanyCode))
                {
                    throw new InvalidOperationException($"Agent with ID Number {dto.IdNo} already exists.");
                }

                var agent = new Agent
                {
                    IdNo = dto.IdNo,
                    RecruitementAgents = dto.RecruitementAgents,  // Comes from dropdown selection
                    Names = dto.Names,
                    Gender = dto.Gender,
                    StaffCode = dto.StaffCode,
                    Occupation = dto.Occupation,
                    LandPhone = dto.LandPhone,
                    MobileNo = dto.MobileNo,
                    Branchname = dto.Branchname,
                    CompanyCode = dto.CompanyCode ?? "",
                    HomeAddress = dto.HomeAddress,
                    Town = dto.Town,
                    Recruitdate = dto.Recruitdate,
                    PIN = dto.PIN,
                    AuditId = userId,  // Logged-in user
                    AuditTime = DateTime.Now,
                    BlockchainTransactionId = null
                };

                _context.Agents.Add(agent);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                string blockchainTxId = null;
                try
                {
                    var blockchainData = new
                    {
                        Action = "CREATE",
                        AgentId = agent.Id,
                        AgentIdNo = agent.IdNo,
                        Names = agent.Names,
                        Gender = agent.Gender,
                        MobileNo = agent.MobileNo,
                        LandPhone = agent.LandPhone,
                        Town = agent.Town,
                        Recruitdate = agent.Recruitdate,
                        RecruitementAgents = agent.RecruitementAgents,  // Record the selected type
                        CreatedBy = userId,
                        CreatedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "AGENT_CREATE",
                        null,
                        dto.CompanyCode,
                        0,
                        agent.IdNo,
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        agent.BlockchainTransactionId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction recorded: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for agent creation");
                }

                await transaction.CommitAsync();

                return new AgentResponseDTO
                {
                    Id = agent.Id,
                    IdNo = agent.IdNo,
                    RecruitementAgents = agent.RecruitementAgents,
                    Names = agent.Names,
                    Gender = agent.Gender,
                    StaffCode = agent.StaffCode,
                    Occupation = agent.Occupation,
                    LandPhone = agent.LandPhone,
                    MobileNo = agent.MobileNo,
                    Branchname = agent.Branchname,
                    CompanyCode = agent.CompanyCode,
                    HomeAddress = agent.HomeAddress,
                    Town = agent.Town,
                    Recruitdate = agent.Recruitdate,
                    PIN = agent.PIN,
                    BlockchainTxId = agent.BlockchainTransactionId,
                    CreatedAt = agent.AuditTime,
                    CreatedBy = agent.AuditId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating agent");
                throw;
            }
        }

        public async Task<AgentResponseDTO> UpdateAsync(string idNo, AgentDTO dto, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"Updating agent: {idNo}");

                var agent = await _context.Agents
                    .FirstOrDefaultAsync(a => a.IdNo == idNo && a.CompanyCode == dto.CompanyCode);

                if (agent == null)
                {
                    throw new InvalidOperationException($"Agent with ID Number {idNo} not found.");
                }

                // Store old values for blockchain
                var oldValues = new
                {
                    agent.IdNo,
                    agent.Names,
                    agent.Gender,
                    agent.MobileNo,
                    agent.LandPhone,
                    agent.Town,
                    agent.HomeAddress,
                    agent.Occupation,
                    agent.StaffCode,
                    agent.Branchname,
                    agent.PIN,
                    agent.Recruitdate,
                    agent.RecruitementAgents
                };

                // Update fields
                agent.RecruitementAgents = dto.RecruitementAgents ?? agent.RecruitementAgents;
                agent.Names = dto.Names ?? agent.Names;
                agent.Gender = dto.Gender ?? agent.Gender;
                agent.StaffCode = dto.StaffCode ?? agent.StaffCode;
                agent.Occupation = dto.Occupation ?? agent.Occupation;
                agent.LandPhone = dto.LandPhone ?? agent.LandPhone;
                agent.MobileNo = dto.MobileNo ?? agent.MobileNo;
                agent.Branchname = dto.Branchname ?? agent.Branchname;
                agent.HomeAddress = dto.HomeAddress ?? agent.HomeAddress;
                agent.Town = dto.Town ?? agent.Town;
                agent.Recruitdate = dto.Recruitdate;
                agent.PIN = dto.PIN ?? agent.PIN;
                agent.AuditId = userId;
                agent.AuditTime = DateTime.Now;

                await _context.SaveChangesAsync();

                // Record blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "UPDATE",
                        AgentId = agent.Id,
                        AgentIdNo = agent.IdNo,
                        OldValues = oldValues,
                        NewValues = new
                        {
                            agent.Names,
                            agent.Gender,
                            agent.MobileNo,
                            agent.LandPhone,
                            agent.Town,
                            agent.HomeAddress,
                            agent.Occupation,
                            agent.StaffCode,
                            agent.Branchname,
                            agent.PIN,
                            agent.Recruitdate,
                            agent.RecruitementAgents
                        },
                        ModifiedBy = userId,
                        ModifiedAt = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "AGENT_UPDATE",
                        null,
                        agent.CompanyCode,
                        0,
                        agent.IdNo,
                        blockchainData);

                    if (blockchainTx != null)
                    {
                        agent.BlockchainTransactionId = blockchainTx.TransactionId;
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for agent update");
                }

                await transaction.CommitAsync();

                return new AgentResponseDTO
                {
                    Id = agent.Id,
                    IdNo = agent.IdNo,
                    RecruitementAgents = agent.RecruitementAgents,
                    Names = agent.Names,
                    Gender = agent.Gender,
                    StaffCode = agent.StaffCode,
                    Occupation = agent.Occupation,
                    LandPhone = agent.LandPhone,
                    MobileNo = agent.MobileNo,
                    Branchname = agent.Branchname,
                    CompanyCode = agent.CompanyCode,
                    HomeAddress = agent.HomeAddress,
                    Town = agent.Town,
                    Recruitdate = agent.Recruitdate,
                    PIN = agent.PIN,
                    BlockchainTxId = agent.BlockchainTransactionId,
                    CreatedAt = agent.AuditTime,
                    CreatedBy = agent.AuditId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating agent {idNo}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string idNo, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var agent = await _context.Agents
                    .FirstOrDefaultAsync(a => a.IdNo == idNo);

                if (agent == null)
                {
                    throw new InvalidOperationException($"Agent with ID Number {idNo} not found.");
                }

                var agentDetails = new
                {
                    agent.Id,
                    agent.IdNo,
                    agent.Names,
                    agent.MobileNo
                };

                _context.Agents.Remove(agent);
                await _context.SaveChangesAsync();

                // Record blockchain transaction
                try
                {
                    var blockchainData = new
                    {
                        Action = "DELETE",
                        AgentDetails = agentDetails,
                        DeletedBy = userId,
                        DeletedAt = DateTime.Now
                    };

                    await _blockchainService.CreateAndAddTransactionAsync(
                        "AGENT_DELETE",
                        null,
                        agent.CompanyCode,
                        0,
                        agent.IdNo,
                        blockchainData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record blockchain transaction for agent deletion");
                }

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error deleting agent {idNo}");
                throw;
            }
        }

        public async Task<AgentResponseDTO> GetByIdNoAsync(string idNo)
        {
            var agent = await _context.Agents
                .FirstOrDefaultAsync(a => a.IdNo == idNo);

            if (agent == null) return null;

            return new AgentResponseDTO
            {
                Id = agent.Id,
                IdNo = agent.IdNo,
                RecruitementAgents = agent.RecruitementAgents,
                Names = agent.Names,
                Gender = agent.Gender,
                StaffCode = agent.StaffCode,
                Occupation = agent.Occupation,
                LandPhone = agent.LandPhone,
                MobileNo = agent.MobileNo,
                Branchname = agent.Branchname,
                CompanyCode = agent.CompanyCode,
                HomeAddress = agent.HomeAddress,
                Town = agent.Town,
                Recruitdate = agent.Recruitdate,
                PIN = agent.PIN,
                BlockchainTxId = agent.BlockchainTransactionId,
                CreatedAt = agent.AuditTime,
                CreatedBy = agent.AuditId
            };
        }

        public async Task<AgentResponseDTO> GetByIdAsync(long id)
        {
            var agent = await _context.Agents
                .FirstOrDefaultAsync(a => a.Id == id);

            if (agent == null) return null;

            return new AgentResponseDTO
            {
                Id = agent.Id,
                IdNo = agent.IdNo,
                RecruitementAgents = agent.RecruitementAgents,
                Names = agent.Names,
                Gender = agent.Gender,
                StaffCode = agent.StaffCode,
                Occupation = agent.Occupation,
                LandPhone = agent.LandPhone,
                MobileNo = agent.MobileNo,
                Branchname = agent.Branchname,
                CompanyCode = agent.CompanyCode,
                HomeAddress = agent.HomeAddress,
                Town = agent.Town,
                Recruitdate = agent.Recruitdate,
                PIN = agent.PIN,
                BlockchainTxId = agent.BlockchainTransactionId,
                CreatedAt = agent.AuditTime,
                CreatedBy = agent.AuditId
            };
        }

        public async Task<List<AgentResponseDTO>> GetAllAsync(string companyCode)
        {
            var agents = await _context.Agents
                .Where(a => a.CompanyCode == companyCode)
                .OrderByDescending(a => a.Id)
                .ToListAsync();

            return agents.Select(a => new AgentResponseDTO
            {
                Id = a.Id,
                IdNo = a.IdNo,
                RecruitementAgents = a.RecruitementAgents,
                Names = a.Names,
                Gender = a.Gender,
                StaffCode = a.StaffCode,
                Occupation = a.Occupation,
                LandPhone = a.LandPhone,
                MobileNo = a.MobileNo,
                Branchname = a.Branchname,
                CompanyCode = a.CompanyCode,
                HomeAddress = a.HomeAddress,
                Town = a.Town,
                Recruitdate = a.Recruitdate,
                PIN = a.PIN,
                BlockchainTxId = a.BlockchainTransactionId,
                CreatedAt = a.AuditTime,
                CreatedBy = a.AuditId
            }).ToList();
        }

        public async Task<bool> IsIdNoUniqueAsync(string idNo, string companyCode, string? excludeIdNo = null)
        {
            var query = _context.Agents
                .Where(a => a.IdNo == idNo && a.CompanyCode == companyCode);

            if (!string.IsNullOrEmpty(excludeIdNo))
            {
                query = query.Where(a => a.IdNo != excludeIdNo);
            }

            return !await query.AnyAsync();
        }

        public async Task<List<AgentSimpleDTO>> GetAgentsForDropdownAsync(string companyCode)
        {
            return await _context.Agents
                .Where(a => a.CompanyCode == companyCode)
                .OrderBy(a => a.Names)
                .Select(a => new AgentSimpleDTO
                {
                    Id = a.Id,
                    IdNo = a.IdNo,
                    Names = a.Names,
                    MobileNo = a.MobileNo
                })
                .ToListAsync();
        }
    }
}