// Controllers/ShareTypeController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ShareTypeController : ControllerBase
    {
        private readonly IShareTypeService _shareTypeService;
        private readonly ILogger<ShareTypeController> _logger;

        public ShareTypeController(
            IShareTypeService shareTypeService,
            ILogger<ShareTypeController> logger)
        {
            _shareTypeService = shareTypeService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> CreateShareType([FromBody] ShareTypeCreateDTO shareTypeDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // Get company code from user claims
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                shareTypeDto.CompanyCode = companyCode;
                shareTypeDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _shareTypeService.CreateShareTypeAsync(shareTypeDto);

                return Ok(new
                {
                    Success = true,
                    Message = "Share type created successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating share type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while creating share type",
                    Error = ex.Message
                });
            }
        }

        [HttpPut("{sharesCode}")]
        public async Task<IActionResult> UpdateShareType(string sharesCode, [FromBody] ShareTypeUpdateDTO shareTypeDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                shareTypeDto.CompanyCode = companyCode;
                shareTypeDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _shareTypeService.UpdateShareTypeAsync(sharesCode, shareTypeDto);

                return Ok(new
                {
                    Success = true,
                    Message = "Share type updated successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating share type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while updating share type",
                    Error = ex.Message
                });
            }
        }

        [HttpDelete("{sharesCode}")]
        public async Task<IActionResult> DeleteShareType(string sharesCode)
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var success = await _shareTypeService.DeleteShareTypeAsync(sharesCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Message = "Share type deleted successfully",
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting share type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while deleting share type",
                    Error = ex.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetShareTypes()
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var shareTypes = await _shareTypeService.GetShareTypesByCompanyAsync(companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = shareTypes,
                    Count = shareTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting share types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting share types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{sharesCode}")]
        public async Task<IActionResult> GetShareType(string sharesCode)
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = shareType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting share type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting share type",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchShareTypes([FromQuery] string searchTerm)
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var shareTypes = await _shareTypeService.SearchShareTypesAsync(searchTerm, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = shareTypes,
                    Count = shareTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching share types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while searching share types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveShareTypes()
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var shareTypes = await _shareTypeService.GetActiveShareTypesAsync(companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = shareTypes,
                    Count = shareTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active share types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting active share types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{sharesCode}/usage")]
        public async Task<IActionResult> GetShareTypeUsage(string sharesCode)
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var usageCount = await _shareTypeService.GetShareTypeUsageCountAsync(sharesCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = new { UsageCount = usageCount }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting share type usage");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting share type usage",
                    Error = ex.Message
                });
            }
        }
    }
}