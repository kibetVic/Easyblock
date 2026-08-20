using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly IMemberService _memberService;
        private readonly ILogger<MemberController> _logger;

        public MemberController(IMemberService memberService, ILogger<MemberController> logger)
        {
            _memberService = memberService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterMember([FromBody] MemberRegistrationDTO registration)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var result = await _memberService.RegisterMemberAsync(registration);

                return Ok(new
                {
                    Success = true,
                    Message = "Member registered successfully",
                    Data = result,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering member");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while registering member",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{memberNo}")]
        public async Task<IActionResult> GetMember(string memberNo)
        {
            try
            {
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);

                if (member == null)
                    return NotFound(new { Success = false, Message = "Member not found" });

                return Ok(new
                {
                    Success = true,
                    Data = new
                    {
                        member.MemberNo,
                        member.Surname,
                        member.OtherNames,
                        member.Idno,
                        member.PhoneNo,
                        member.Email,
                        member.CompanyCode,
                        member.Status,
                        member.ShareCap,
                        member.BlockchainTxId
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching member");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching member",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchMembers([FromQuery] string searchTerm)
        {
            try
            {
                var members = await _memberService.SearchMembersAsync(searchTerm);

                return Ok(new
                {
                    Success = true,
                    Data = members.Select(m => new
                    {
                        m.MemberNo,
                        m.Surname,
                        m.OtherNames,
                        m.Idno,
                        m.PhoneNo,
                        m.CompanyCode,
                        m.Status
                    }),
                    Count = members.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching members");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while searching members",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{memberNo}/shares")]
        public async Task<IActionResult> GetMemberShares(string memberNo)
        {
            try
            {
                var shareBalance = await _memberService.GetMemberShareBalanceAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = new
                    {
                        MemberNo = memberNo,
                        ShareBalance = shareBalance,
                        LastUpdated = DateTime.Now
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching member shares");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching member shares",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{memberNo}/blockchain-history")]
        public async Task<IActionResult> GetBlockchainHistory(string memberNo)
        {
            try
            {
                var transactions = await _memberService.GetMemberBlockchainHistoryAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = transactions.Select(t => new
                    {
                        t.TransactionId,
                        t.TransactionType,
                        t.Amount,
                        t.Timestamp,
                        t.DataHash,
                        t.Status
                    }),
                    Count = transactions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching blockchain history");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while fetching blockchain history",
                    Error = ex.Message
                });
            }
        }

        [HttpPut("{memberNo}")]
        public async Task<IActionResult> UpdateMember(string memberNo, [FromBody] Member updatedMember)
        {
            try
            {
                var success = await _memberService.UpdateMemberAsync(memberNo, updatedMember);

                if (!success)
                    return NotFound(new { Success = false, Message = "Member not found" });

                return Ok(new
                {
                    Success = true,
                    Message = "Member updated successfully",
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating member");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "An error occurred while updating member",
                    Error = ex.Message
                });
            }
        }
    }
}