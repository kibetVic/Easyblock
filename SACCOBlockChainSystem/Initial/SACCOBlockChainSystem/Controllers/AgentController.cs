// Controllers/AgentController.cs - Updated
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class AgentController : Controller
    {
        private readonly IAgentService _agentService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<AgentController> _logger;

        public AgentController(
            IAgentService agentService,
            ICompanyContextService companyContextService,
            ILogger<AgentController> logger)
        {
            _agentService = agentService;
            _companyContextService = companyContextService;
            _logger = logger;
        }

        private string GetUserCompanyCode()
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();
            if (string.IsNullOrEmpty(companyCode))
            {
                throw new Exception("Company code not found. Please log in again.");
            }
            return companyCode;
        }

        private string GetCurrentUserId()
        {
            return User.Identity?.Name ?? "SYSTEM";
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var agents = await _agentService.GetAllAsync(companyCode);
                var recruitmentTypes = await _agentService.GetRecruitmentAgentTypesAsync();

                ViewBag.RecruitmentTypes = recruitmentTypes;
                ViewBag.CurrentUser = GetCurrentUserId();

                return View(agents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading agents index");
                TempData["ErrorMessage"] = $"Error loading agents: {ex.Message}";
                return View(new List<AgentResponseDTO>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetails(long id)
        {
            try
            {
                var agent = await _agentService.GetByIdAsync(id);
                if (agent == null)
                {
                    return Json(new { success = false, message = "Agent not found" });
                }

                return Json(new
                {
                    success = true,
                    agent = new
                    {
                        agent.Id,
                        agent.IdNo,
                        agent.RecruitementAgents,
                        agent.Names,
                        agent.Gender,
                        agent.StaffCode,
                        agent.Occupation,
                        agent.LandPhone,
                        agent.MobileNo,
                        agent.Branchname,
                        agent.CompanyCode,
                        agent.HomeAddress,
                        agent.Town,
                        agent.Recruitdate,
                        agent.PIN,
                        agent.BlockchainTxId,
                        agent.CreatedAt,
                        agent.CreatedBy
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting agent details");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecruitmentTypes()
        {
            try
            {
                var types = await _agentService.GetRecruitmentAgentTypesAsync();
                return Json(new { success = true, data = types });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] AgentDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join(", ", errors) });
                }

                dto.CompanyCode = GetUserCompanyCode();
                dto.CreatedBy = GetCurrentUserId();  // This becomes AuditId

                var result = await _agentService.CreateAsync(dto, dto.CreatedBy);

                return Json(new
                {
                    success = true,
                    message = "Agent created successfully",
                    agent = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating agent");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromBody] AgentDTO dto)
        {
            try
            {
                if (string.IsNullOrEmpty(dto.IdNo))
                {
                    return Json(new { success = false, message = "Agent ID Number is required" });
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage);
                    return Json(new { success = false, message = string.Join(", ", errors) });
                }

                dto.CompanyCode = GetUserCompanyCode();
                dto.CreatedBy = GetCurrentUserId();  // This becomes AuditId

                var result = await _agentService.UpdateAsync(dto.IdNo, dto, dto.CreatedBy);

                return Json(new
                {
                    success = true,
                    message = "Agent updated successfully",
                    agent = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(long id)
        {
            try
            {
                var agent = await _agentService.GetByIdAsync(id);
                if (agent == null)
                {
                    return Json(new { success = false, message = "Agent not found" });
                }

                var result = await _agentService.DeleteAsync(agent.IdNo, GetCurrentUserId());

                return Json(new
                {
                    success = true,
                    message = "Agent deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting agent");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}