using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class WithdrawalController : Controller
    {
        private readonly IWithdrawalService _withdrawalService;
        private readonly IMemberService _memberService;
        private readonly ISaccoService _saccoService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<WithdrawalController> _logger;

        public WithdrawalController(
            IWithdrawalService withdrawalService,
            IMemberService memberService,
            ISaccoService saccoService,
            ICompanyContextService companyContextService,
            ApplicationDbContext context,
            ILogger<WithdrawalController> logger)
        {
            _withdrawalService = withdrawalService;
            _memberService = memberService;
            _saccoService = saccoService;
            _companyContextService = companyContextService;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string status = "All")
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var withdrawals = await _withdrawalService.GetWithdrawalsByStatusAsync(status, companyCode);
                ViewBag.CurrentStatus = status;
                return View(withdrawals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading withdrawals");
                TempData["ErrorMessage"] = "Error loading withdrawals";
                return View(new List<WithdrawalResponseDTO>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create(string memberNo)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // If memberNo is provided, load that member
                if (!string.IsNullOrEmpty(memberNo))
                {
                    var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                    if (member == null)
                    {
                        TempData["ErrorMessage"] = "Member not found";
                        return View(new MemberWithdrawalDTO());
                    }

                    // Check if member has already withdrawn
                    if (member.Withdrawn == true)
                    {
                        TempData["ErrorMessage"] = $"Member {member.MemberNo} has already withdrawn from the SACCO.";
                        return View(new MemberWithdrawalDTO());
                    }

                    // Check if member is archived
                    if (member.Archived == true)
                    {
                        TempData["ErrorMessage"] = $"Member {member.MemberNo} is archived and cannot withdraw.";
                        return View(new MemberWithdrawalDTO());
                    }

                    // Get SACCO parameters
                    var saccoParams = await _saccoService.GetSaccoParametersAsync(companyCode);

                    // Check membership maturity
                    if (member.ApplicDate.HasValue)
                    {
                        var membershipMonths = (DateTime.Now - member.ApplicDate.Value).TotalDays / 30;
                        if (membershipMonths < saccoParams.MembershipMaturityMonths)
                        {
                            var monthsNeeded = saccoParams.MembershipMaturityMonths - (int)membershipMonths;
                            TempData["ErrorMessage"] = $"Member must be a member for at least {saccoParams.MembershipMaturityMonths} months before withdrawal. " +
                                                       $"Member has been a member for {(int)membershipMonths} months. Please wait {monthsNeeded} more month(s).";
                            return View(new MemberWithdrawalDTO());
                        }
                    }

                    // Check withdrawal notice period
                    if (member.ApplicDate.HasValue)
                    {
                        var noticeDate = member.ApplicDate.Value.AddDays(saccoParams.WithdrawalNoticeDays);
                        if (DateTime.Now < noticeDate)
                        {
                            var daysRemaining = (noticeDate - DateTime.Now).Days;
                            TempData["ErrorMessage"] = $"Withdrawal requires {saccoParams.WithdrawalNoticeDays} days notice. " +
                                                       $"Notice period ends on {noticeDate:dd/MM/yyyy}. Please wait {daysRemaining} more day(s).";
                            return View(new MemberWithdrawalDTO());
                        }
                    }

                    // Calculate withdrawal amount
                    var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(memberNo, companyCode);

                    if (!calculation.IsEligibleForWithdrawal)
                    {
                        TempData["ErrorMessage"] = calculation.EligibilityMessage;
                        return View(new MemberWithdrawalDTO());
                    }

                    if (calculation.NetPayableAmount <= 0)
                    {
                        TempData["ErrorMessage"] = "Net payable amount is zero or negative. Cannot process withdrawal.";
                        return View(new MemberWithdrawalDTO());
                    }

                    ViewBag.Member = member;
                    ViewBag.Calculation = calculation;
                    ViewBag.MemberNo = memberNo;
                    ViewBag.MemberName = $"{member.Surname} {member.OtherNames}".Trim();
                    ViewBag.NoticeDays = saccoParams.WithdrawalNoticeDays;

                    return View(new MemberWithdrawalDTO
                    {
                        WithdrawalDate = DateTime.Now,
                        WithdrawalType = "Voluntary"
                    });
                }

                // If no memberNo, return empty view with search
                return View(new MemberWithdrawalDTO());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading withdrawal form for member {memberNo}", memberNo);
                TempData["ErrorMessage"] = "Error loading withdrawal form";
                return View(new MemberWithdrawalDTO());
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchMember(string memberNo, string idNo, string phoneNo, string fullName)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                if (string.IsNullOrEmpty(memberNo) && string.IsNullOrEmpty(idNo) &&
                    string.IsNullOrEmpty(phoneNo) && string.IsNullOrEmpty(fullName))
                {
                    return Json(new { success = false, message = "Please provide a search value" });
                }

                // Use the existing SearchMembersAsync method from MemberService
                string searchTerm = memberNo ?? idNo ?? phoneNo ?? fullName;
                var members = await _memberService.SearchMembersAsync(searchTerm);

                if (members == null || !members.Any())
                {
                    return Json(new { success = false, message = "No member found with the provided information" });
                }

                var member = members.FirstOrDefault();

                if (member == null)
                {
                    return Json(new { success = false, message = "Member not found" });
                }

                // Check if member has outstanding loans
                var hasOutstandingLoans = await _context.Loans
                    .AnyAsync(l => l.MemberNo == member.MemberNo &&
                                   l.CompanyCode == companyCode &&
                                   l.Status != (int)Status.Closed &&
                                   l.Status != (int)Status.WrittenOff);

                if (hasOutstandingLoans)
                {
                    return Json(new { success = false, message = "Member has outstanding loans. Please clear all loans before withdrawal." });
                }

                // Check if member has already withdrawn
                if (member.Withdrawn == true)
                {
                    return Json(new { success = false, message = "Member has already withdrawn from the SACCO." });
                }

                // Check if member is archived
                if (member.Archived == true)
                {
                    return Json(new { success = false, message = "Member is archived and cannot withdraw." });
                }

                // Return member data in the same format as ShareTransfer
                var memberData = new
                {
                    memberNo = member.MemberNo,
                    fullName = $"{member.Surname} {member.OtherNames}".Trim(),
                    idNo = member.Idno,
                    phoneNo = member.PhoneNo,
                    email = member.Email,
                    applicDate = member.ApplicDate?.ToString("yyyy-MM-dd"),
                    status = member.Status == 1 ? "Active" : "Inactive"
                };

                return Json(new { success = true, member = memberData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching member");
                return Json(new { success = false, message = "Error searching for member. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string memberNo, MemberWithdrawalDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                    var companyCode = _companyContextService.GetCurrentCompanyCode();
                    var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(memberNo, companyCode);
                    var saccoParams = await _saccoService.GetSaccoParametersAsync(companyCode);

                    ViewBag.Member = member;
                    ViewBag.Calculation = calculation;
                    ViewBag.MemberNo = memberNo;
                    ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : "";
                    ViewBag.NoticeDays = saccoParams.WithdrawalNoticeDays;

                    return View(dto);
                }

                var withdrawal = await _withdrawalService.CreateWithdrawalAsync(
                    memberNo,
                    dto,
                    User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = $"Withdrawal request {withdrawal.WithdrawalNo} created successfully! " +
                                              $"Net amount: KES {withdrawal.NetPayableAmount:N0}";

                return RedirectToAction("Details", new { id = withdrawal.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating withdrawal for member {memberNo}", memberNo);
                ModelState.AddModelError("", ex.Message);

                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(memberNo, companyCode);
                var saccoParams = await _saccoService.GetSaccoParametersAsync(companyCode);

                ViewBag.Member = member;
                ViewBag.Calculation = calculation;
                ViewBag.MemberNo = memberNo;
                ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : "";
                ViewBag.NoticeDays = saccoParams.WithdrawalNoticeDays;

                return View(dto);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var withdrawal = await _withdrawalService.GetWithdrawalByIdAsync(id);
                if (withdrawal == null)
                {
                    TempData["ErrorMessage"] = "Withdrawal not found";
                    return RedirectToAction("Index");
                }

                // Only allow editing of pending withdrawals
                if (withdrawal.Status != "Pending")
                {
                    TempData["ErrorMessage"] = $"Cannot edit withdrawal in '{withdrawal.Status}' status. Only pending withdrawals can be edited.";
                    return RedirectToAction("Details", new { id });
                }

                // Get member details
                var member = await _memberService.GetMemberByMemberNoAsync(withdrawal.MemberNo);
                if (member == null)
                {
                    TempData["ErrorMessage"] = "Member not found";
                    return RedirectToAction("Index");
                }

                // Recalculate withdrawal amount (in case balances have changed)
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(withdrawal.MemberNo, companyCode);

                var dto = new MemberWithdrawalDTO
                {
                    WithdrawalType = withdrawal.WithdrawalType,
                    WithdrawalDate = withdrawal.WithdrawalDate,
                    PaymentMethod = withdrawal.PaymentMethod,
                    BankName = withdrawal.BankName,
                    BankAccountNo = withdrawal.BankAccountNo,
                    AccountName = withdrawal.AccountName,
                    ChequeNo = withdrawal.ChequeNo,
                    MobileNo = withdrawal.MobileNo,
                    Remarks = withdrawal.Remarks,
                    DocumentPath = withdrawal.DocumentPath
                };

                ViewBag.Member = member;
                ViewBag.Calculation = calculation;
                ViewBag.MemberNo = withdrawal.MemberNo;
                ViewBag.MemberName = $"{member.Surname} {member.OtherNames}".Trim();
                ViewBag.WithdrawalNo = withdrawal.WithdrawalNo;
                ViewBag.NetPayableAmount = withdrawal.NetPayableAmount;

                return View(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading edit withdrawal form for id {id}", id);
                TempData["ErrorMessage"] = "Error loading edit form";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MemberWithdrawalDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var withdrawal = await _withdrawalService.GetWithdrawalByIdAsync(id);
                    var member = await _memberService.GetMemberByMemberNoAsync(withdrawal?.MemberNo);
                    var companyCode = _companyContextService.GetCurrentCompanyCode();
                    var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(withdrawal?.MemberNo, companyCode);

                    ViewBag.Member = member;
                    ViewBag.Calculation = calculation;
                    ViewBag.MemberNo = withdrawal?.MemberNo;
                    ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : "";
                    ViewBag.WithdrawalNo = withdrawal?.WithdrawalNo;
                    ViewBag.NetPayableAmount = withdrawal?.NetPayableAmount ?? 0;

                    return View(dto);
                }

                var updatedWithdrawal = await _withdrawalService.UpdateWithdrawalAsync(id, dto, User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = $"Withdrawal {updatedWithdrawal.WithdrawalNo} updated successfully!";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating withdrawal {id}", id);
                ModelState.AddModelError("", ex.Message);

                var withdrawal = await _withdrawalService.GetWithdrawalByIdAsync(id);
                var member = await _memberService.GetMemberByMemberNoAsync(withdrawal?.MemberNo);
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(withdrawal?.MemberNo, companyCode);

                ViewBag.Member = member;
                ViewBag.Calculation = calculation;
                ViewBag.MemberNo = withdrawal?.MemberNo;
                ViewBag.MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : "";
                ViewBag.WithdrawalNo = withdrawal?.WithdrawalNo;
                ViewBag.NetPayableAmount = withdrawal?.NetPayableAmount ?? 0;

                return View(dto);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var withdrawal = await _withdrawalService.GetWithdrawalByIdAsync(id);
                if (withdrawal == null)
                {
                    TempData["ErrorMessage"] = "Withdrawal not found";
                    return RedirectToAction("Index");
                }

                return View(withdrawal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading withdrawal details for id {id}", id);
                TempData["ErrorMessage"] = "Error loading withdrawal details";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string comments)
        {
            try
            {
                await _withdrawalService.ApproveWithdrawalAsync(id, User.Identity?.Name ?? "SYSTEM", comments);
                TempData["SuccessMessage"] = "Withdrawal approved successfully";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving withdrawal {id}", id);
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            try
            {
                await _withdrawalService.RejectWithdrawalAsync(id, User.Identity?.Name ?? "SYSTEM", reason);
                TempData["SuccessMessage"] = "Withdrawal rejected";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting withdrawal {id}", id);
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int id, DateTime paymentDate, string paymentReference)
        {
            try
            {
                await _withdrawalService.ProcessPaymentAsync(id, User.Identity?.Name ?? "SYSTEM", paymentDate, paymentReference);
                TempData["SuccessMessage"] = "Payment processed successfully. Member has been marked as withdrawn.";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment for withdrawal {id}", id);
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string reason)
        {
            try
            {
                await _withdrawalService.CancelWithdrawalAsync(id, reason, User.Identity?.Name ?? "SYSTEM");
                TempData["SuccessMessage"] = "Withdrawal cancelled";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling withdrawal {id}", id);
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Calculate(string memberNo)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var calculation = await _withdrawalService.CalculateWithdrawalAmountAsync(memberNo, companyCode);
                return Json(new { success = true, data = calculation });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating withdrawal for member {memberNo}", memberNo);
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}