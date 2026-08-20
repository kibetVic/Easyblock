using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize(Policy = "MemberOnly")]
    public class TransactionsMvcController : Controller
    {
        private readonly ITransactionService _transactionService;
        private readonly IMemberService _memberService;
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
		private readonly AuditTrailService _auditService;
		private readonly ILogger<TransactionsMvcController> _logger;

        public TransactionsMvcController(
            ITransactionService transactionService,
            IMemberService memberService,
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<TransactionsMvcController> logger, AuditTrailService audit)
        {
            _transactionService = transactionService;
            _memberService = memberService;
            _context = context;
            _blockchainService = blockchainService;
            _auditService = audit;
            _logger = logger;
        }

        // Helper method to get user's UserGroup from database
        private async Task<string?> GetUserGroupAsync()
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
                return null;

            var user = await _context.UserAccounts1
                .FirstOrDefaultAsync(u => u.UserName == username);

            return user?.UserGroup;
        }

        // Update the Helper method to check user roles:
        private async Task<bool> IsAdminOrStaffAsync()
        {
            var userGroup = await GetUserGroupAsync();
            var adminRoles = new[] { "Admin", "Staff", "Supervisor", "Teller", "LoanOfficer", "BoardMember" };
            return !string.IsNullOrEmpty(userGroup) && adminRoles.Contains(userGroup);
        }

        //// Helper method to check if user is admin/staff
        //private async Task<bool> IsAdminOrStaffAsync()
        //{
        //    var userGroup = await GetUserGroupAsync();
        //    return userGroup == "Admin" || userGroup == "Staff" || userGroup == "Supervisor";
        //}

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Deposit()
        {
            // Check if user is admin/staff (can select any member)
            var isAdmin = await IsAdminOrStaffAsync();
            ViewBag.IsAdmin = isAdmin;

            if (!isAdmin)
            {
                // Regular members can only deposit to their own account
                var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                if (!string.IsNullOrEmpty(currentMemberNo))
                {
                    var member = await _memberService.GetMemberByMemberNoAsync(currentMemberNo);
                    if (member != null)
                    {
                        ViewBag.CurrentMember = member;
                        ViewBag.CurrentBalance = await _transactionService.GetMemberBalanceAsync(currentMemberNo);
                    }
                }
            }

            return View();
        }

		[HttpPost]
		public async Task<IActionResult> Deposit(DepositDTO deposit)
		{
			try
			{
				// Validate member exists
				var member = await _memberService.GetMemberByMemberNoAsync(deposit.MemberNo);
				if (member == null)
				{
					TempData["ErrorMessage"] = $"Member {deposit.MemberNo} not found";
					return View(deposit);
				}

				// Get current user info
				var currentUser = User.FindFirst(ClaimTypes.Name)?.Value;
				var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
				deposit.ProcessedBy = currentUser ?? "SYSTEM";

				// Save to Contrib table (if needed for legacy system)
				var contrib = new Contrib
				{
					MemberNo = deposit.MemberNo,
					Amount = deposit.Amount,
					ContrDate = DateTime.Now,
					DepositedDate = DateTime.Now,
					ReceiptDate = DateTime.Now,
					ReceiptNo = deposit.ReceiptNo,
					RefNo = GenerateTransactionNumber(),
					TransBy = currentUser ?? "SYSTEM",
					CompanyCode = member.CompanyCode ?? "DEFAULT",
					AuditId = currentUser ?? "SYSTEM",
					AuditTime = DateTime.Now,
					AuditDateTime = DateTime.Now,
					TransactionNo = GenerateTransactionNumber(),
					Remarks = deposit.Purpose
				};

				// Save to ContribShare table
				var contribShare = new ContribShare
				{
					MemberNo = deposit.MemberNo,
					ContrDate = DateTime.Now,
					DepositedDate = DateTime.Now,
					ReceiptDate = DateTime.Now,
					DepositsAmount = deposit.Amount,
					CompanyCode = member.CompanyCode,
					ReceiptNo = deposit.ReceiptNo,
					Remarks = $"Deposit via {deposit.PaymentMode} - {deposit.Purpose}",
					AuditId = currentUser ?? "SYSTEM",
					AuditTime = DateTime.Now,
					Sharescode = "DEP01",
					TransactionNo = contrib.TransactionNo,
					AuditDateTime = DateTime.Now
				};

				// Create blockchain transaction for Contrib
				var blockchainDataContrib = new
				{
					Table = "Contrib",
					TransactionNo = contrib.TransactionNo,
					MemberNo = deposit.MemberNo,
					MemberName = $"{member.Surname} {member.OtherNames}",
					Amount = deposit.Amount,
					PaymentMode = deposit.PaymentMode,
					ReceiptNo = deposit.ReceiptNo,
					Purpose = deposit.Purpose,
					Timestamp = DateTime.Now
				};

				var blockchainTxContrib = await _blockchainService.CreateTransaction(
					"DEPOSIT_CONTRIB",
					deposit.MemberNo,
					member.CompanyCode,
					deposit.Amount,
					contrib.Id.ToString(),
					blockchainDataContrib
				);

				contrib.BlockchainTxId = blockchainTxContrib.TransactionId;

				// Create blockchain transaction for ContribShare
				var blockchainDataContribShare = new
				{
					Table = "ContribShare",
					TransactionNo = contribShare.TransactionNo,
					MemberNo = deposit.MemberNo,
					MemberName = $"{member.Surname} {member.OtherNames}",
					Amount = deposit.Amount,
					PaymentMode = deposit.PaymentMode,
					ReceiptNo = deposit.ReceiptNo,
					Purpose = deposit.Purpose,
					Timestamp = DateTime.Now
				};

				var blockchainTxContribShare = await _blockchainService.CreateTransaction(
					"DEPOSIT_CONTRIBSHARE",
					deposit.MemberNo,
					member.CompanyCode,
					deposit.Amount,
					contribShare.Id.ToString(),
					blockchainDataContribShare
				);

				contribShare.BlockchainTxId = blockchainTxContribShare.TransactionId;

				// Process deposit through main transaction service
				var result = await _transactionService.ProcessDepositAsync(deposit);

				// Save Contrib and ContribShare records
				_context.Contribs.Add(contrib);
				_context.ContribShares.Add(contribShare);
				await _context.SaveChangesAsync();

				// Add to blockchain
				await _blockchainService.AddToBlockchain(blockchainTxContrib);
				await _blockchainService.AddToBlockchain(blockchainTxContribShare);

				// ========== AUDIT TRAIL FOR DEPOSIT ==========
				// Audit for Contrib table
				await _auditService.SaveLogAsync(
					actionType: AuditActionType.Insert,
					newModel: contrib,
					tableName: "Contrib",
					recordId: contrib.Id.ToString(),
					userId: currentUserId ?? "SYSTEM",
					userName: currentUser ?? "SYSTEM",
					companyCode: member.CompanyCode,
					module: "Deposits",
					extraData: JsonSerializer.Serialize(new
					{
						amount = deposit.Amount,
						memberNo = deposit.MemberNo,
						memberName = $"{member.Surname} {member.OtherNames}",
						receiptNo = deposit.ReceiptNo,
						paymentMode = deposit.PaymentMode,
						purpose = deposit.Purpose,
						transactionNo = contrib.TransactionNo,
						blockchainTxId = blockchainTxContrib.TransactionId,
						transactionType = "Receipt Posting Shares Code"
					})
				);

				// Audit for ContribShare table
				await _auditService.SaveLogAsync(
					actionType: AuditActionType.Insert,
					newModel: contribShare,
					tableName: "ContribShare",
					recordId: contribShare.Id.ToString(),
					userId: currentUserId ?? "SYSTEM",
					userName: currentUser ?? "SYSTEM",
					companyCode: member.CompanyCode,
					module: "Deposits",
					extraData: JsonSerializer.Serialize(new
					{
						amount = deposit.Amount,
						memberNo = deposit.MemberNo,
						memberName = $"{member.Surname} {member.OtherNames}",
						receiptNo = deposit.ReceiptNo,
						sharesCode = "DEP01",
						transactionNo = contribShare.TransactionNo,
						blockchainTxId = blockchainTxContribShare.TransactionId,
						transactionType = "Receipt Posting Shares Code"
					})
				);

				// Audit for main transaction
				await _auditService.SaveLogAsync(
					actionType: AuditActionType.Insert,
					newModel: result,
					tableName: "Transactions",
					recordId: result.TransactionId,
					userId: currentUserId ?? "SYSTEM",
					userName: currentUser ?? "SYSTEM",
					companyCode: member.CompanyCode,
					module: "Deposits",
					extraData: JsonSerializer.Serialize(new
					{
						amount = deposit.Amount,
						memberNo = deposit.MemberNo,
						memberName = $"{member.Surname} {member.OtherNames}",
						receiptNo = result.ReceiptNo,
						newBalance = result.NewBalance,
						transactionId = result.TransactionId,
						paymentMode = deposit.PaymentMode,
						purpose = deposit.Purpose,
						transactionType = "Receipt Posting Shares Code"
					})
				);

				_logger.LogInformation($"Deposit audit logs saved successfully for Member: {deposit.MemberNo}, Amount: {deposit.Amount}");

				TempData["SuccessMessage"] = $"Deposit of {deposit.Amount:C} processed successfully! " +
										   $"Transaction ID: {result.TransactionId}, " +
										   $"Receipt No: {result.ReceiptNo}, " +
										   $"New Balance: {result.NewBalance:C}";

				return RedirectToAction("Deposit");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing deposit for Member: {MemberNo}, Amount: {Amount}", deposit.MemberNo, deposit.Amount);

				// Audit the error
				try
				{
					var currentUser = User.FindFirst(ClaimTypes.Name)?.Value ?? "SYSTEM";
					var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

					await _auditService.SaveLogAsync(
						actionType: AuditActionType.View,
						tableName: "Deposit",
						recordId: deposit.MemberNo,
						userId: currentUserId ?? "SYSTEM",
						userName: currentUser ?? "SYSTEM",
						//companyCode: deposit.CompanyCode,
						module: "Deposits",
						extraData: JsonSerializer.Serialize(new
						{
							error = true,
							errorMessage = ex.Message,
							memberNo = deposit.MemberNo,
							amount = deposit.Amount,
							timestamp = DateTime.Now
						})
					);
				}
				catch (Exception auditEx)
				{
					_logger.LogError(auditEx, "Failed to save error audit log");
				}

				TempData["ErrorMessage"] = $"Error processing deposit: {ex.Message}";

				// Check user role
				var isAdmin = await IsAdminOrStaffAsync();

				// Reload member info if available
				if (!string.IsNullOrEmpty(deposit.MemberNo))
				{
					var memberInfo = await _memberService.GetMemberByMemberNoAsync(deposit.MemberNo);
					if (memberInfo != null)
					{
						ViewBag.CurrentMember = memberInfo;
						ViewBag.CurrentBalance = await _transactionService.GetMemberBalanceAsync(deposit.MemberNo);
					}
				}

				ViewBag.IsAdmin = isAdmin;
				return View(deposit);
			}
		}

		[HttpGet]
        public async Task<IActionResult> Withdraw()
        {
            // Check if user is admin/staff (can select any member)
            var isAdmin = await IsAdminOrStaffAsync();
            ViewBag.IsAdmin = isAdmin;

            if (!isAdmin)
            {
                // Regular members can only withdraw from their own account
                var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                if (!string.IsNullOrEmpty(currentMemberNo))
                {
                    var member = await _memberService.GetMemberByMemberNoAsync(currentMemberNo);
                    if (member != null)
                    {
                        ViewBag.CurrentMember = member;
                        ViewBag.CurrentBalance = await _transactionService.GetMemberBalanceAsync(currentMemberNo);
                    }
                }
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Withdraw(WithdrawalDTO withdrawal)
        {
            try
            {
                // Validate member exists
                var member = await _memberService.GetMemberByMemberNoAsync(withdrawal.MemberNo);
                if (member == null)
                {
                    TempData["ErrorMessage"] = $"Member {withdrawal.MemberNo} not found";
                    return View(withdrawal);
                }

                // Check balance
                var currentBalance = await _transactionService.GetMemberBalanceAsync(withdrawal.MemberNo);
                if (currentBalance < withdrawal.Amount)
                {
                    TempData["ErrorMessage"] = $"Insufficient balance. Available: {currentBalance}, Requested: {withdrawal.Amount}";

                    var memberInfo = await _memberService.GetMemberByMemberNoAsync(withdrawal.MemberNo);
                    if (memberInfo != null)
                    {
                        ViewBag.CurrentMember = memberInfo;
                        ViewBag.CurrentBalance = currentBalance;
                    }

                    var isAdmin = await IsAdminOrStaffAsync();
                    ViewBag.IsAdmin = isAdmin;
                    return View(withdrawal);
                }

                // Get current user info
                var currentUser = User.FindFirst(ClaimTypes.Name)?.Value;
                withdrawal.ProcessedBy = currentUser ?? "SYSTEM";

                // Save to Contrib table (negative amount for withdrawal)
                var contrib = new Contrib
                {
                    MemberNo = withdrawal.MemberNo,
                    Amount = -withdrawal.Amount,
                    ContrDate = DateTime.Now,
                    DepositedDate = DateTime.Now,
                    ReceiptDate = DateTime.Now,
                    ReceiptNo = withdrawal.ReceiptNo,
                    RefNo = GenerateTransactionNumber(),
                    TransBy = currentUser ?? "SYSTEM",
                    CompanyCode = member.CompanyCode ?? "DEFAULT",
                    AuditId = currentUser ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    TransactionNo = GenerateTransactionNumber(),
                    Remarks = withdrawal.Purpose
                };

                // Save to ContribShare table (negative amount)
                var contribShare = new ContribShare
                {
                    MemberNo = withdrawal.MemberNo,
                    ContrDate = DateTime.Now,
                    DepositedDate = DateTime.Now,
                    ReceiptDate = DateTime.Now,
                    DepositsAmount = -withdrawal.Amount,
                    CompanyCode = member.CompanyCode,
                    ReceiptNo = withdrawal.ReceiptNo,
                    Remarks = $"Withdrawal via {withdrawal.PaymentMode} - {withdrawal.Purpose}",
                    AuditId = currentUser ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Sharescode = "WTH01",
                    TransactionNo = contrib.TransactionNo,
                    AuditDateTime = DateTime.Now
                };

                // Create blockchain transaction for Contrib
                var blockchainDataContrib = new
                {
                    Table = "Contrib",
                    TransactionNo = contrib.TransactionNo,
                    MemberNo = withdrawal.MemberNo,
                    Amount = -withdrawal.Amount,
                    PaymentMode = withdrawal.PaymentMode,
                    ReceiptNo = withdrawal.ReceiptNo,
                    Purpose = withdrawal.Purpose,
                    Timestamp = DateTime.Now
                };

                var blockchainTxContrib = await _blockchainService.CreateTransaction(
                    "WITHDRAWAL_CONTRIB",
                    withdrawal.MemberNo,
                    member.CompanyCode,
                    withdrawal.Amount,
                    contrib.Id.ToString(),
                    blockchainDataContrib
                );

                contrib.BlockchainTxId = blockchainTxContrib.TransactionId;

                // Create blockchain transaction for ContribShare
                var blockchainDataContribShare = new
                {
                    Table = "ContribShare",
                    TransactionNo = contribShare.TransactionNo,
                    MemberNo = withdrawal.MemberNo,
                    Amount = -withdrawal.Amount,
                    PaymentMode = withdrawal.PaymentMode,
                    ReceiptNo = withdrawal.ReceiptNo,
                    Purpose = withdrawal.Purpose,
                    Timestamp = DateTime.Now
                };

                var blockchainTxContribShare = await _blockchainService.CreateTransaction(
                    "WITHDRAWAL_CONTRIBSHARE",
                    withdrawal.MemberNo,
                    member.CompanyCode,
                    withdrawal.Amount,
                    contribShare.Id.ToString(),
                    blockchainDataContribShare
                );

                contribShare.BlockchainTxId = blockchainTxContribShare.TransactionId;

                // Process withdrawal through main transaction service
                var result = await _transactionService.ProcessWithdrawalAsync(withdrawal);

                // Save Contrib and ContribShare records
                _context.Contribs.Add(contrib);
                _context.ContribShares.Add(contribShare);
                await _context.SaveChangesAsync();

                // Add to blockchain
                await _blockchainService.AddToBlockchain(blockchainTxContrib);
                await _blockchainService.AddToBlockchain(blockchainTxContribShare);

                TempData["SuccessMessage"] = $"Withdrawal of {withdrawal.Amount} processed successfully! " +
                                           $"Transaction ID: {result.TransactionId}, " +
                                           $"Receipt No: {result.ReceiptNo}, " +
                                           $"New Balance: {result.NewBalance}";

                return RedirectToAction("Withdraw");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing withdrawal");
                TempData["ErrorMessage"] = $"Error processing withdrawal: {ex.Message}";

                // Check user role
                var isAdmin = await IsAdminOrStaffAsync();
                ViewBag.IsAdmin = isAdmin;

                // Reload member info if available
                if (!string.IsNullOrEmpty(withdrawal.MemberNo))
                {
                    var member = await _memberService.GetMemberByMemberNoAsync(withdrawal.MemberNo);
                    if (member != null)
                    {
                        ViewBag.CurrentMember = member;
                        ViewBag.CurrentBalance = await _transactionService.GetMemberBalanceAsync(withdrawal.MemberNo);
                    }
                }

                return View(withdrawal);
            }
        }


        // Update the SearchMember method:
        [HttpGet]
        public async Task<IActionResult> SearchMember(string searchTerm)
        {
            try
            {
                var isAdmin = await IsAdminOrStaffAsync();
                var currentMemberNo = User.FindFirst("MemberNo")?.Value;

                if (!isAdmin)
                {
                    // Regular members can only search their own member number
                    if (!string.IsNullOrEmpty(currentMemberNo))
                    {
                        var member = await _memberService.GetMemberByMemberNoAsync(currentMemberNo);
                        if (member != null &&
                            (member.MemberNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                             (member.Surname != null && member.Surname.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                             (member.OtherNames != null && member.OtherNames.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))))
                        {
                            var balance = await _transactionService.GetMemberBalanceAsync(currentMemberNo);
                            return Json(new
                            {
                                success = true,
                                data = new List<object>
                        {
                            new
                            {
                                memberNo = member.MemberNo,
                                surname = member.Surname,
                                otherNames = member.OtherNames,
                                fullName = $"{member.Surname} {member.OtherNames}",
                                idno = member.Idno,
                                phoneNo = member.PhoneNo,
                                email = member.Email,
                                currentBalance = balance
                            }
                        }
                            });
                        }
                    }
                    return Json(new { success = true, data = new List<object>() });
                }

                // Admin users can search all members
                var members = await _memberService.SearchMembersAsync(searchTerm);
                var memberResults = new List<object>();

                foreach (var member in members)
                {
                    var balance = await _transactionService.GetMemberBalanceAsync(member.MemberNo);
                    memberResults.Add(new
                    {
                        memberNo = member.MemberNo,
                        surname = member.Surname,
                        otherNames = member.OtherNames,
                        fullName = member.FullName,
                        idno = member.Idno,
                        phoneNo = member.PhoneNo,
                        email = member.Email,
                        currentBalance = balance
                    });
                }

                return Json(new { success = true, data = memberResults });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching members");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Update the GetMemberDetails method:
        [HttpGet]
        public async Task<IActionResult> GetMemberDetails(string memberNo)
        {
            try
            {
                var isAdmin = await IsAdminOrStaffAsync();
                if (!isAdmin)
                {
                    // Regular members can only view their own details
                    var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                    if (currentMemberNo != memberNo)
                    {
                        return Json(new { success = false, message = "Access denied" });
                    }
                }

                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                if (member == null)
                {
                    return Json(new { success = false, message = "Member not found" });
                }

                var balance = await _transactionService.GetMemberBalanceAsync(memberNo);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        memberNo = member.MemberNo,
                        surname = member.Surname,
                        otherNames = member.OtherNames,
                        fullName = $"{member.Surname} {member.OtherNames}",
                        idNumber = member.Idno,
                        phone = member.PhoneNo,
                        email = member.Email,
                        currentBalance = balance,
                        companyCode = member.CompanyCode,
                        employer = member.Employer,
                        station = member.Station,
                        dateJoined = member.EffectDate?.ToString("yyyy-MM-dd"),
                        status = member.Status?.ToString() ?? "0"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member details");
                return Json(new { success = false, message = ex.Message });
            }
        }

        //[HttpGet]
        //public async Task<IActionResult> SearchMember(string searchTerm)
        //{
        //    try
        //    {
        //        // Check if user has permission to search all members
        //        var isAdmin = await IsAdminOrStaffAsync();
        //        if (!isAdmin)
        //        {
        //            // Regular members can only search their own member number
        //            var currentMemberNo = User.FindFirst("MemberNo")?.Value;
        //            if (!string.IsNullOrEmpty(currentMemberNo))
        //            {
        //                var member = await _memberService.GetMemberByMemberNoAsync(currentMemberNo);
        //                if (member != null && currentMemberNo.Contains(searchTerm))
        //                {
        //                    return Json(new
        //                    {
        //                        success = true,
        //                        data = new List<object>
        //                        {
        //                            new
        //                            {
        //                                memberNo = member.MemberNo,
        //                                surname = member.Surname,
        //                                otherNames = member.OtherNames,
        //                                idno = member.Idno,
        //                                phoneNo = member.PhoneNo
        //                            }
        //                        }
        //                    });
        //                }
        //            }
        //            return Json(new { success = true, data = new List<object>() });
        //        }

        //        var members = await _memberService.SearchMembersAsync(searchTerm);
        //        return Json(new { success = true, data = members });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error searching members");
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}

        //[HttpGet]
        //public async Task<IActionResult> GetMemberDetails(string memberNo)
        //{
        //    try
        //    {
        //        // Check if user has permission to view member details
        //        var isAdmin = await IsAdminOrStaffAsync();
        //        if (!isAdmin)
        //        {
        //            // Regular members can only view their own details
        //            var currentMemberNo = User.FindFirst("MemberNo")?.Value;
        //            if (currentMemberNo != memberNo)
        //            {
        //                return Json(new { success = false, message = "Access denied" });
        //            }
        //        }

        //        var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
        //        if (member == null)
        //        {
        //            return Json(new { success = false, message = "Member not found" });
        //        }

        //        var balance = await _transactionService.GetMemberBalanceAsync(memberNo);

        //        return Json(new
        //        {
        //            success = true,
        //            data = new
        //            {
        //                memberNo = member.MemberNo,
        //                surname = member.Surname,
        //                otherNames = member.OtherNames,
        //                fullName = $"{member.Surname} {member.OtherNames}",
        //                idNumber = member.Idno,
        //                phone = member.PhoneNo,
        //                currentBalance = balance,
        //                companyCode = member.CompanyCode
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error getting member details");
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}


        
        [HttpGet]
        public async Task<IActionResult> History(string memberNo, DateTime? startDate, DateTime? endDate)
        {
            // Check user role
            var isAdmin = await IsAdminOrStaffAsync();

            if (string.IsNullOrEmpty(memberNo))
            {
                // Get current user's member number if not admin
                if (!isAdmin)
                {
                    memberNo = User.FindFirst("MemberNo")?.Value;
                }
            }

            if (string.IsNullOrEmpty(memberNo) && !isAdmin)
            {
                return RedirectToAction("Index", "Home");
            }

            List<Transaction> transactions;
            if (isAdmin && string.IsNullOrEmpty(memberNo))
            {
                // Admin can see all transactions
                transactions = await _context.Transactions
                    .Where(t => !startDate.HasValue || t.TransDate >= startDate.Value)
                    .Where(t => !endDate.HasValue || t.TransDate <= endDate.Value)
                    .OrderByDescending(t => t.TransDate)
                    .Take(100) // Limit results
                    .ToListAsync();
            }
            else
            {
                // Check permission for non-admin users
                if (!isAdmin)
                {
                    var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                    if (currentMemberNo != memberNo)
                    {
                        TempData["ErrorMessage"] = "Access denied. You can only view your own transaction history.";
                        return RedirectToAction("History", new { memberNo = currentMemberNo });
                    }
                }

                // Get specific member's transactions
                transactions = await _transactionService.GetMemberTransactionsAsync(memberNo, startDate, endDate);
            }

            var member = !string.IsNullOrEmpty(memberNo) ?
                await _memberService.GetMemberByMemberNoAsync(memberNo) : null;

            // Create a ViewModel instead of passing Member directly
            var viewModel = new MemberTransactionsViewModel
            {
                Member = member,
                Transactions = transactions,
                StartDate = startDate,
                EndDate = endDate,
                IsAdmin = isAdmin,
                MemberNo = memberNo,
                MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "All Transactions"
            };

            return View(viewModel); 
        }


        //[HttpGet]
        //public async Task<IActionResult> History(string memberNo, DateTime? startDate, DateTime? endDate)
        //{
        //    // Check user role
        //    var isAdmin = await IsAdminOrStaffAsync();

        //    if (string.IsNullOrEmpty(memberNo))
        //    {
        //        // Get current user's member number if not admin
        //        if (!isAdmin)
        //        {
        //            memberNo = User.FindFirst("MemberNo")?.Value;
        //        }
        //    }

        //    if (string.IsNullOrEmpty(memberNo) && !isAdmin)
        //    {
        //        return RedirectToAction("Index", "Home");
        //    }

        //    List<Transaction> transactions;
        //    if (isAdmin && string.IsNullOrEmpty(memberNo))
        //    {
        //        // Admin can see all transactions
        //        transactions = await _context.Transactions
        //            .Where(t => !startDate.HasValue || t.TransDate >= startDate.Value)
        //            .Where(t => !endDate.HasValue || t.TransDate <= endDate.Value)
        //            .OrderByDescending(t => t.TransDate)
        //            .ToListAsync();
        //    }
        //    else
        //    {
        //        // Check permission for non-admin users
        //        if (!isAdmin)
        //        {
        //            var currentMemberNo = User.FindFirst("MemberNo")?.Value;
        //            if (currentMemberNo != memberNo)
        //            {
        //                TempData["ErrorMessage"] = "Access denied. You can only view your own transaction history.";
        //                return RedirectToAction("History", new { memberNo = currentMemberNo });
        //            }
        //        }

        //        // Get specific member's transactions
        //        transactions = await _transactionService.GetMemberTransactionsAsync(memberNo, startDate, endDate);
        //    }

        //    var member = !string.IsNullOrEmpty(memberNo) ?
        //        await _memberService.GetMemberByMemberNoAsync(memberNo) : null;

        //    ViewBag.IsAdmin = isAdmin;
        //    ViewBag.MemberNo = memberNo;
        //    ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "All Transactions";
        //    ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
        //    ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");

        //    return View(transactions);
        //}



        [HttpGet]
        public async Task<IActionResult> Balance(string memberNo)
        {
            var isAdmin = await IsAdminOrStaffAsync();

            if (string.IsNullOrEmpty(memberNo))
            {
                // Get current user's member number if not admin
                if (!isAdmin)
                {
                    memberNo = User.FindFirst("MemberNo")?.Value;
                }
                else
                {
                    // Admin needs to select a member
                    return RedirectToAction("Index");
                }
            }

            if (string.IsNullOrEmpty(memberNo))
            {
                return RedirectToAction("Index", "Home");
            }

            // Check permission for non-admin users
            if (!isAdmin)
            {
                var currentMemberNo = User.FindFirst("MemberNo")?.Value;
                if (currentMemberNo != memberNo)
                {
                    TempData["ErrorMessage"] = "Access denied. You can only view your own account balance.";
                    return RedirectToAction("Balance", new { memberNo = currentMemberNo });
                }
            }

            var balance = await _transactionService.GetMemberBalanceAsync(memberNo);
            var member = await _memberService.GetMemberByMemberNoAsync(memberNo);

            ViewBag.IsAdmin = isAdmin;
            ViewBag.MemberNo = memberNo;
            ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown";
            ViewBag.IDNumber = member != null ? member.Idno : "Unknown";
            ViewBag.PhoneNumber = member != null ? member.PhoneNo : "Unknown";
            ViewBag.Balance = balance;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAllMembers()
        {
            try
            {
                // Check if user has permission to get all members
                var isAdmin = await IsAdminOrStaffAsync();
                if (!isAdmin)
                {
                    return Json(new { success = false, message = "Access denied" });
                }

                var members = await _context.Members
                    .Where(m => m.MemberNo != null)
                    .Select(m => new
                    {
                        memberNo = m.MemberNo,
                        fullName = $"{m.Surname} {m.OtherNames}",
                        idNumber = m.Idno,
                        phone = m.PhoneNo
                    })
                    .ToListAsync();

                return Json(new { success = true, data = members });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all members");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GenerateTransactionNumber()
        {
            return $"TX{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        private string GenerateReceiptNumber()
        {
            return $"RCP{DateTime.Now:yyyyMMdd}{new Random().Next(10000, 99999)}";
        }
    }
}