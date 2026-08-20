using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize(Policy = "MemberOnly")]
    [Route("api/[controller]")]
    [ApiController]
    public class ShareController : ControllerBase
    {
        private readonly IShareService _shareService;
        private readonly ILogger<ShareController> _logger;

        public ShareController(IShareService shareService, ILogger<ShareController> logger)
        {
            _shareService = shareService;
            _logger = logger;
        }

        [HttpPost("purchase")]
        public async Task<IActionResult> PurchaseShares([FromBody] SharePurchaseDTO purchase)
        {
            try
            {
                var result = await _shareService.PurchaseSharesAsync(purchase);
                return Ok(new
                {
                    Success = true,
                    Message = "Share purchase completed successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error purchasing shares");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> TransferShares([FromBody] ShareTransferDTO transfer)
        {
            try
            {
                var success = await _shareService.TransferSharesAsync(transfer);
                return Ok(new
                {
                    Success = true,
                    Message = "Share transfer completed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring shares");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpPost("dividends")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> DistributeDividends([FromBody] DividendDistributionDTO distribution)
        {
            try
            {
                var result = await _shareService.DistributeDividendsAsync(distribution);
                return Ok(new
                {
                    Success = true,
                    Message = "Dividend distribution completed successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error distributing dividends");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpGet("member/{memberNo}")]
        public async Task<IActionResult> GetMemberShares(string memberNo)
        {
            try
            {
                var shares = await _shareService.GetMemberSharesAsync(memberNo);
                var totalValue = await _shareService.GetTotalSharesValueAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = new
                    {
                        Shares = shares,
                        TotalValue = totalValue,
                        LastUpdated = DateTime.Now
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching shares");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error fetching shares"
                });
            }
        }
    }
}