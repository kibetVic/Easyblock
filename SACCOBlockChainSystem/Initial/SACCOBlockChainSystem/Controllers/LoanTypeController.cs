using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LoanTypeController : ControllerBase
    {
        private readonly ILoanTypeService _loanTypeService;
        private readonly ILogger<LoanTypeController> _logger;
        private readonly ICompanyContextService _companyContextService;

        public LoanTypeController(
            ILoanTypeService loanTypeService,
            ILogger<LoanTypeController> logger,
            ICompanyContextService companyContextService)
        {
            _loanTypeService = loanTypeService;
            _logger = logger;
            _companyContextService = companyContextService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateLoanType([FromBody] LoanTypeCreateDTO loanTypeDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // Get company code from current user context
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                loanTypeDto.CompanyCode = companyCode;
                loanTypeDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanTypeService.CreateLoanTypeAsync(loanTypeDto);

                return Ok(new
                {
                    Success = true,
                    Message = "Loan type created successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating loan type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while creating loan type",
                    Error = ex.Message
                });
            }
        }

        [HttpPut("{loanCode}")]
        public async Task<IActionResult> UpdateLoanType(string loanCode, [FromBody] LoanTypeUpdateDTO loanTypeDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                loanTypeDto.CompanyCode = companyCode;
                loanTypeDto.UpdatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanTypeService.UpdateLoanTypeAsync(loanCode, loanTypeDto);

                return Ok(new
                {
                    Success = true,
                    Message = "Loan type updated successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating loan type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while updating loan type",
                    Error = ex.Message
                });
            }
        }

        [HttpDelete("{loanCode}")]
        public async Task<IActionResult> DeleteLoanType(string loanCode)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var success = await _loanTypeService.DeleteLoanTypeAsync(loanCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Message = "Loan type deleted successfully",
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting loan type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while deleting loan type",
                    Error = ex.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLoanTypes()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = loanTypes,
                    Count = loanTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loan types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting loan types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{loanCode}")]
        public async Task<IActionResult> GetLoanType(string loanCode)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loanCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = loanType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loan type");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting loan type",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchLoanTypes([FromQuery] string searchTerm)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var loanTypes = await _loanTypeService.SearchLoanTypesAsync(searchTerm, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = loanTypes,
                    Count = loanTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching loan types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while searching loan types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveLoanTypes()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var loanTypes = await _loanTypeService.GetActiveLoanTypesAsync(companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = loanTypes,
                    Count = loanTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active loan types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting active loan types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{loanCode}/usage")]
        public async Task<IActionResult> GetLoanTypeUsage(string loanCode)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var usageCount = await _loanTypeService.GetLoanTypeUsageCountAsync(loanCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = new { UsageCount = usageCount }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loan type usage");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting loan type usage",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("member/{memberNo}/eligible")]
        public async Task<IActionResult> GetEligibleLoanTypesForMember(string memberNo)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var loanTypes = await _loanTypeService.GetLoanTypesForMemberAsync(memberNo, companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = loanTypes,
                    Count = loanTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting eligible loan types for member");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while getting eligible loan types",
                    Error = ex.Message
                });
            }
        }
    }
}