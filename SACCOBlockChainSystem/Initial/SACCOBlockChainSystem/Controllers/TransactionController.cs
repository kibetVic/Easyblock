﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System.Security.Claims;
using System.Text.Json;
using SACCOBlockChainSystem.Models;
using Microsoft.Extensions.Logging;


namespace SACCOBlockChainSystem.Controllers
{
    [Authorize(Policy = "MemberOnly")]
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IMemberService _memberService;
        private readonly AuditTrailService _auditService;
        private readonly ILogger<TransactionController> _logger;

        public TransactionController(
            ITransactionService transactionService,
            IMemberService memberService,
            ILogger<TransactionController> logger, AuditTrailService audit)
        {
            _transactionService = transactionService;
            _memberService = memberService;
            _auditService = audit;
            _logger = logger;
        }

        [HttpGet("search-member")]
        public async Task<IActionResult> SearchMember([FromQuery] string searchTerm)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
                {
                    return Ok(new
                    {
                        Success = true,
                        Data = new List<object>(),
                        Message = "Please enter at least 2 characters"
                    });
                }

                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var isAdmin = IsAdminUser(userRole);

                if (!isAdmin)
                {
                    // Regular members can only search their own details
                    var memberNo = User.FindFirst("MemberNo")?.Value;
                    if (string.IsNullOrEmpty(memberNo))
                    {
                        return Ok(new
                        {
                            Success = true,
                            Data = new List<object>(),
                            Message = "Member number not found in your profile"
                        });
                    }

                    // Check if search term matches their member number
                    if (memberNo.Contains(searchTerm))
                    {
                        var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                        if (member != null)
                        {
                            // var balance = await _memberService.GetMemberBalanceAsync(memberNo);
                            return Ok(new
                            {
                                Success = true,
                                Data = new List<object>
                                {
                                    new
                                    {
                                        memberNo = member.MemberNo,
                                        surname = member.Surname,
                                        otherNames = member.OtherNames,
                                        fullName = $"{member.Surname} {member.OtherNames}",
                                        idNumber = member.Idno,
                                        phone = member.PhoneNo,
                                        email = member.Email,
                                        //currentBalance = balance
                                    }
                                }
                            });
                        }
                    }
                    return Ok(new
                    {
                        Success = true,
                        Data = new List<object>(),
                        Message = "No members found"
                    });
                }

                // Admin users can search all members
                var members = await _memberService.SearchMembersAsync(searchTerm);
                return Ok(new
                {
                    Success = true,
                    Data = members.Select(m => new
                    {
                        memberNo = m.MemberNo,
                        surname = m.Surname,
                        otherNames = m.OtherNames,
                        fullName = m.FullName,
                        idNumber = m.Idno,
                        phone = m.PhoneNo,
                        email = m.Email,
                        companyCode = m.CompanyCode
                    }),
                    Count = members.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching members");
                return BadRequest(new
                {
                    Success = false,
                    Message = "Error searching members"
                });
            }
        }

        [HttpGet("member-details/{memberNo}")]
        public async Task<IActionResult> GetMemberDetails(string memberNo, string currentCompanyCode)
        {
            try
            {
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var isAdmin = IsAdminUser(userRole);

                if (!isAdmin)
                {
                    // Regular members can only view their own details
                    var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                    if (currentMemberNo != memberNo)
                    {
                        return Unauthorized(new
                        {
                            Success = false,
                            Message = "Access denied. You can only view your own details."
                        });
                    }
                }

                var memberDetails = await _memberService.GetMemberDetailsAsync(memberNo);

                return Ok(new
                {
                    Success = true,
                    Data = new
                    {
                        memberDetails.MemberNo,
                        memberDetails.Surname,
                        memberDetails.OtherNames,
                        memberDetails.FullName,
                        memberDetails.Idno,
                        memberDetails.PhoneNo,
                        memberDetails.Email,
                        memberDetails.CurrentBalance,
                        memberDetails.CompanyCode,
                        memberDetails.Employer,
                        memberDetails.Station,
                        memberDetails.DateJoined,
                        memberDetails.Status
                    }
                });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Member not found: {MemberNo}", memberNo);
                return NotFound(new
                {
                    Success = false,
                    Message = $"Member {memberNo} not found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member details for: {MemberNo}", memberNo);
                return BadRequest(new
                {
                    Success = false,
                    Message = "Error getting member details"
                });
            }
        }

		[HttpPost("deposit")]
		public async Task<IActionResult> Deposit([FromBody] DepositDTO deposit)
		{
			try
			{
				// Validate deposit object
				if (deposit == null)
				{
					_logger.LogWarning("Deposit request received with null data");
					return BadRequest(new
					{
						Success = false,
						Message = "Invalid deposit data"
					});
				}

				// Validate amount
				if (deposit.Amount <= 0)
				{
					_logger.LogWarning("Invalid deposit amount: {Amount}", deposit.Amount);
					return BadRequest(new
					{
						Success = false,
						Message = "Amount must be greater than zero"
					});
				}

				// Validate member exists
				var member = await _memberService.GetMemberByMemberNoAsync(deposit.MemberNo);
				if (member == null)
				{
					_logger.LogWarning("Member not found: {MemberNo}", deposit.MemberNo);

					// Audit failed attempt
					await _auditService.SaveLogAsync(
						actionType: AuditActionType.View,
						tableName: "Deposit",
						recordId: deposit.MemberNo,
						userId: User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "API_SYSTEM",
						userName: User?.Identity?.Name ?? "API_SYSTEM",
						companyCode: "UNKNOWN",
						module: "Deposits_API",
						extraData: JsonSerializer.Serialize(new
						{
							error = true,
							errorMessage = $"Member {deposit.MemberNo} not found",
							amount = deposit.Amount,
							receiptNo = deposit.ReceiptNo,
							paymentMode = deposit.PaymentMode,
							purpose = deposit.Purpose,
							timestamp = DateTime.Now
						})
					);

					return BadRequest(new
					{
						Success = false,
						Message = $"Member {deposit.MemberNo} not found"
					});
				}

				// Get current user info
				var currentUser = User?.Identity?.Name ?? "API_SYSTEM";
				var currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "API_SYSTEM";

				// Set processed by
				deposit.ProcessedBy = currentUser;

				_logger.LogInformation("Processing API deposit for Member: {MemberNo}, Amount: {Amount}, PaymentMode: {PaymentMode}",
					deposit.MemberNo, deposit.Amount, deposit.PaymentMode);

				// Process deposit through main transaction service
				var result = await _transactionService.ProcessDepositAsync(deposit);

				if (result == null)
				{
					throw new Exception("Transaction service returned null result");
				}

				// ========== AUDIT TRAIL ==========
				await _auditService.SaveLogAsync(
					actionType: AuditActionType.Insert,
					newModel: result,
					tableName: "Transactions",
					recordId: result.TransactionId,
					userId: currentUserId,
					userName: currentUser,
					companyCode: member.CompanyCode ?? "DEFAULT",
					module: "Deposits_API",
					extraData: JsonSerializer.Serialize(new
					{
						amount = deposit.Amount,
						memberNo = deposit.MemberNo,
						memberName = $"{member.Surname} {member.OtherNames}",
						receiptNo = deposit.ReceiptNo ?? result.ReceiptNo,
						paymentMode = deposit.PaymentMode,
						purpose = deposit.Purpose ?? "Deposit via API",
						transactionId = result.TransactionId,
						newBalance = result.NewBalance,
						source = "API",
						transactionType = "Receipt Posting Shares Code",
						processedBy = currentUser,
						timestamp = DateTime.Now
					})
				);

				_logger.LogInformation("API Deposit successful - Member: {MemberNo}, Amount: {Amount}, TransactionId: {TransactionId}, Balance: {NewBalance}",
					deposit.MemberNo, deposit.Amount, result.TransactionId, result.NewBalance);

				return Ok(new
				{
					Success = true,
					Message = "Deposit processed successfully",
					Data = new
					{
						result.TransactionId,
						result.ReceiptNo,
						result.NewBalance,
						MemberName = $"{member.Surname} {member.OtherNames}",
						MemberNo = deposit.MemberNo,
						Amount = deposit.Amount,
						PaymentMode = deposit.PaymentMode,
						ProcessedBy = currentUser,
						ProcessedAt = DateTime.Now
					}
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing API deposit for Member: {MemberNo}, Amount: {Amount}",
					deposit?.MemberNo, deposit?.Amount);

				// Audit the error
				try
				{
					await _auditService.SaveLogAsync(
						actionType: AuditActionType.View,
						tableName: "Deposit_API",
						recordId: deposit?.MemberNo ?? "UNKNOWN",
						userId: User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "API_SYSTEM",
						userName: User?.Identity?.Name ?? "API_SYSTEM",
						companyCode: "UNKNOWN",
						module: "Deposits_API",
						extraData: JsonSerializer.Serialize(new
						{
							error = true,
							errorMessage = ex.Message,
							memberNo = deposit?.MemberNo,
							amount = deposit?.Amount,
							receiptNo = deposit?.ReceiptNo,
							paymentMode = deposit?.PaymentMode,
							purpose = deposit?.Purpose,
							timestamp = DateTime.Now,
							stackTrace = ex.StackTrace
						})
					);
				}
				catch (Exception auditEx)
				{
					_logger.LogError(auditEx, "Failed to save error audit log for API deposit");
				}

				return BadRequest(new
				{
					Success = false,
					Message = ex.Message
				});
			}
		}

		[HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] WithdrawalDTO withdrawal)
        {
            try
            {
                // Validate member exists first
                var member = await _memberService.GetMemberByMemberNoAsync(withdrawal.MemberNo);
                if (member == null)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = $"Member {withdrawal.MemberNo} not found"
                    });
                }

                var result = await _transactionService.ProcessWithdrawalAsync(withdrawal);
                return Ok(new
                {
                    Success = true,
                    Message = "Withdrawal processed successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing withdrawal");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpGet("member/{memberNo}")]
        public async Task<IActionResult> GetMemberTransactions(string memberNo, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            try
            {
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var isAdmin = IsAdminUser(userRole);

                if (!isAdmin)
                {
                    // Regular members can only view their own transactions
                    var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                    if (currentMemberNo != memberNo)
                    {
                        return Unauthorized(new
                        {
                            Success = false,
                            Message = "Access denied. You can only view your own transactions."
                        });
                    }
                }

                var transactions = await _transactionService.GetMemberTransactionsAsync(memberNo, fromDate, toDate);
                return Ok(new
                {
                    Success = true,
                    Data = transactions,
                    Count = transactions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching transactions");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error fetching transactions"
                });
            }
        }

        [HttpGet("balance/{memberNo}")]
        public async Task<IActionResult> GetMemberBalance(string memberNo, string currentCompanyCode)
        {
            try
            {
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var isAdmin = IsAdminUser(userRole);

                if (!isAdmin)
                {
                    // Regular members can only view their own balance
                    var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                    if (currentMemberNo != memberNo)
                    {
                        return Unauthorized(new
                        {
                            Success = false,
                            Message = "Access denied. You can only view your own balance."
                        });
                    }
                }

                var balance = await _transactionService.GetMemberBalanceAsync(memberNo);
                return Ok(new
                {
                    Success = true,
                    Data = new
                    {
                        MemberNo = memberNo,
                        Balance = balance,
                        LastUpdated = DateTime.Now
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching balance");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error fetching balance"
                });
            }
        }

        [HttpGet("all-members")]
        public async Task<IActionResult> GetAllMembers()
        {
            try
            {
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var isAdmin = IsAdminUser(userRole);

                if (!isAdmin)
                {
                    return Unauthorized(new
                    {
                        Success = false,
                        Message = "Access denied. Admin access required."
                    });
                }

                var members = await _memberService.GetAllMembersAsync();
                return Ok(new
                {
                    Success = true,
                    Data = members.Select(m => new
                    {
                        m.MemberNo,
                        m.Surname,
                        m.OtherNames,
                        FullName = $"{m.Surname} {m.OtherNames}",
                        m.Idno,
                        m.PhoneNo,
                        m.Email,
                        m.CompanyCode,
                        m.Status
                    }),
                    Count = members.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all members");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error getting all members"
                });
            }
        }

        private bool IsAdminUser(string? userRole)
        {
            var adminRoles = new[] { "Admin", "Staff", "Supervisor", "Teller", "LoanOfficer", "BoardMember" };
            return !string.IsNullOrEmpty(userRole) && adminRoles.Contains(userRole);
        }

		private string GetCurrentUserId()
		{
			try
			{
				return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "API_SYSTEM";
			}
			catch
			{
				return "API_SYSTEM";
			}
		}

		private string GetCurrentUserName()
		{
			try
			{
				return User.Identity?.Name ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "API_SYSTEM";
			}
			catch
			{
				return "API_SYSTEM";
			}
		}

		private string GetClientIpAddress()
		{
			try
			{
				var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

				// Check for proxy headers
				if (string.IsNullOrEmpty(ipAddress) || ipAddress == "::1")
				{
					var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
					if (!string.IsNullOrEmpty(forwardedFor))
					{
						ipAddress = forwardedFor.Split(',').First().Trim();
					}
				}

				return ipAddress ?? "Unknown";
			}
			catch
			{
				return "Unknown";
			}
		}

		private string GenerateTransactionNumber()
		{
			return $"API_TXN{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
		}

		private string GenerateReceiptNumber()
		{
			return $"RCP{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
		}
	}
}