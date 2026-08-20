// Controllers/ContributionController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ContributionController : ControllerBase
    {
        private readonly IMemberService _memberService;
        private readonly ILogger<ContributionController> _logger;

        public ContributionController(IMemberService memberService, ILogger<ContributionController> logger)
        {
            _memberService = memberService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> AddContribution([FromBody] ContributionDTO contributionDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // Get company code from user claims (assuming it's stored there)
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found in user claims" });
                }

                contributionDto.CompanyCode = companyCode;
                contributionDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _memberService.AddContributionAsync(contributionDto);

                return Ok(new
                {
                    Success = true,
                    Message = "Contribution added successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding contribution");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while adding contribution",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("member/{memberNo}/sharetype/{shareTypeCode}/total")]
        public async Task<IActionResult> GetMemberShareTypeTotal(string memberNo, string shareTypeCode)
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var total = await _memberService.GetMemberShareTypeTotalAsync(memberNo, shareTypeCode, companyCode);

                return Ok(new
                {
                    Success = true,
                    CurrentTotal = total,
                    Message = "Total retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member share type total");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching member total",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("member/{memberNo}")]
        public async Task<IActionResult> GetMemberContributions(string memberNo)
        {
            try
            {
                var contributions = await _memberService.GetMemberContributionsAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = contributions,
                    Count = contributions.Count,
                    Total = contributions.Sum(c => c.Amount)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching member contributions");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching contributions",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("history/{memberNo}")]
        public async Task<IActionResult> GetMemberContributionHistory(string memberNo)
        {
            try
            {
                var history = await _memberService.GetMemberContributionHistoryAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = history
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching contribution history");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching contribution history",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("sharetypes")]
        public async Task<IActionResult> GetShareTypes()
        {
            try
            {
                var companyCode = User.FindFirst("CompanyCode")?.Value;
                if (string.IsNullOrEmpty(companyCode))
                {
                    return BadRequest(new { Success = false, Message = "Company code not found" });
                }

                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);

                return Ok(new
                {
                    Success = true,
                    Data = shareTypes,
                    Count = shareTypes.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching share types");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching share types",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchContributions(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? memberNo,
            [FromQuery] string? shareType)
        {
            try
            {
                var contributions = await _memberService.SearchContributionsAsync(fromDate, toDate, memberNo, shareType);

                return Ok(new
                {
                    Success = true,
                    Data = contributions,
                    Count = contributions.Count,
                    Total = contributions.Sum(c => c.Amount)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching contributions");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while searching contributions",
                    Error = ex.Message
                });
            }
        }
    }
}