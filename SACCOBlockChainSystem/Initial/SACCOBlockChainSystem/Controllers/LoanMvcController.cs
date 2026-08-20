using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class LoanMvcController : Controller
    {
        private readonly ILoanService _loanService;
        private readonly ILoanTypeService _loanTypeService;
        private readonly IMemberService _memberService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<LoanMvcController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ISaccoService _saccoService;

        public LoanMvcController(
            ILoanService loanService,
            ILoanTypeService loanTypeService,
            IMemberService memberService,
            ICompanyContextService companyContextService,
            ApplicationDbContext context,
            ISaccoService saccoService,
            ILogger<LoanMvcController> logger)
        {
            _loanService = loanService;
            _loanTypeService = loanTypeService;
            _memberService = memberService;
            _companyContextService = companyContextService;
            _logger = logger;
            _saccoService = saccoService;
            _context = context;
        }


        private int GetCompanyId()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                if (string.IsNullOrEmpty(companyCode))
                {
                    _logger.LogWarning("Company code is null or empty");
                    return 1; // Default company ID
                }

                var company = _context.Companies
                    .FirstOrDefault(c => c.CompanyCode == companyCode);

                if (company != null)
                {
                    return company.Id;
                }

                _logger.LogWarning($"Company not found for code: {companyCode}");
                return 1; // Default company ID
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting company ID");
                return 1; // Default company ID
            }
        }

        private bool IsAdminUser()
        {
            return User.IsInRole("Admin") ||
                   User.HasClaim(c => c.Type == "UserGroup" && c.Value == "Admin");
        }

        [HttpGet]
        public async Task<IActionResult> CheckMemberEligibility(string memberNo)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Use the existing method name (it now returns the new tuple)
                var eligibility = await _loanService.CheckMemberEligibilityWithContributionsAsync(memberNo, companyCode);

                // Get member details
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);

                // Check for existing active loans
                var existingLoansResult = await _loanService.CheckExistingLoansAsync(memberNo, companyCode);

                return Json(new
                {
                    success = eligibility.IsEligible,
                    message = eligibility.Message,
                    hasExistingLoan = existingLoansResult.HasExistingLoan,
                    existingLoans = existingLoansResult.ExistingLoans?.Select(l => new
                    {
                        l.LoanNo,
                        l.LoanType,
                        l.LoanStatus,
                        l.OutstandingBalance
                    }),
                    data = new
                    {
                        memberNo = member?.MemberNo,
                        name = member?.FullName ?? $"{member?.Surname} {member?.OtherNames}",
                        idNo = member?.Idno,
                        phone = member?.PhoneNo,
                        email = member?.Email,
                        shareCapital = member?.ShareCap ?? 0,
                        eligibleShares = eligibility.TotalEligibleShares,
                        totalEligibleShares = eligibility.TotalEligibleShares,
                        maxLoanAmount = eligibility.MaxLoanAmount,
                        hasValidShares = eligibility.HasValidShares,
                        availableShares = eligibility.TotalEligibleShares,
                        totalContributions = eligibility.TotalEligibleShares,
                        maxLoanAmountFromShares = eligibility.MaxLoanAmount
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking member eligibility");
                return Json(new
                {
                    success = false,
                    message = $"Error checking eligibility: {ex.Message}"
                });
            }
        }



        #region Loan Deletion

        // GET: /LoanMvc/DeleteLoan
        [HttpGet]
        public IActionResult DeleteLoan()
        {
            try
            {
                // Check permission
                if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to delete loans";
                    return RedirectToAction("AllLoans");
                }

                // Return simple content for testing
                return Content("Delete Loan page - View file needs to be created at Views/LoanMvc/DeleteLoan.cshtml");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading delete loan page");
                TempData["ErrorMessage"] = "Error loading page";
                return RedirectToAction("AllLoans");
            }
        }

        // POST: /LoanMvc/DeleteLoan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLoan(string loanNo, string reason)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                _logger.LogInformation($"DeleteLoan POST called for loan {loanNo}");

                // Validate input
                if (string.IsNullOrEmpty(loanNo))
                {
                    TempData["ErrorMessage"] = "Loan number is required";
                    return RedirectToAction("DeleteLoan");
                }

                if (string.IsNullOrEmpty(reason))
                {
                    TempData["ErrorMessage"] = "Please provide a reason for deleting the loan";
                    return RedirectToAction("DeleteLoan");
                }

                // Check permission
                if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to delete loans";
                    return RedirectToAction("AllLoans");
                }

                // Verify loan exists - DO NOT check for Closed status
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("DeleteLoan");
                }

                // REMOVE THIS CHECK - Allow deletion even if status is Closed
                // if (loan.Status == (int)Status.Closed)
                // {
                //     TempData["ErrorMessage"] = "This loan is already closed/deleted";
                //     return RedirectToAction("DeleteLoan");
                // }

                // Get counts for logging (query before deletion)
                var guarantorCount = await _context.Loanguar.CountAsync(g => g.LoanNo == loanNo);
                var collateralCount = await _context.ColloanGuars.CountAsync(cg => cg.LoanNo == loanNo);
                var scheduleCount = await _context.LoanSchedules.CountAsync(s => s.LoanNo == loanNo);
                var repaymentCount = await _context.Repay.CountAsync(r => r.LoanNo == loanNo);
                var appraisalExists = await _context.Appraisal.AnyAsync(a => a.LoanNo == loanNo);
                var endorsementExists = await _context.Endmain.AnyAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);
                var chequeExists = await _context.Cheques.AnyAsync(c => c.LoanNo == loanNo && c.CompanyCode == companyCode);
                var loanbalExists = await _context.Loanbal.AnyAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);

                _logger.LogInformation($"Loan {loanNo} - Found data: Guarantors={guarantorCount}, Collateral={collateralCount}, " +
                    $"Schedules={scheduleCount}, Repayments={repaymentCount}, Appraisal={appraisalExists}, " +
                    $"Endorsement={endorsementExists}, Cheque={chequeExists}, LoanBal={loanbalExists}");

                // Permanently delete the loan and all related data
                await _loanService.DeleteLoanAsync(loanNo, companyCode, User.Identity?.Name ?? "SYSTEM", reason);

                TempData["SuccessMessage"] = $"Loan {loanNo} has been PERMANENTLY DELETED. " +
                                             $"Removed: {guarantorCount} guarantor(s), " +
                                             $"{collateralCount} collateral(s), " +
                                             $"{scheduleCount} schedule entries, " +
                                             $"{repaymentCount} repayment(s)";

                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting loan {loanNo}");
                TempData["ErrorMessage"] = $"Error deleting loan: {ex.Message}";
                return RedirectToAction("DeleteLoan");
            }
        }

        // GET: /LoanMvc/DeleteLoanIndex
        [HttpGet]
        public IActionResult DeleteLoanIndex()
        {
            try
            {
                // Check permission
                if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to delete loans";
                    return RedirectToAction("AllLoans");
                }

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading delete loan page");
                TempData["ErrorMessage"] = "Error loading page";
                return RedirectToAction("AllLoans");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLoanDetailsForDeletion(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                _logger.LogInformation($"Getting loan details for deletion: {loanNo}");

                // Get the loan - DO NOT filter by status
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    return Json(new { success = false, message = "Loan not found" });
                }

                // REMOVE THIS CHECK - Allow showing loans even if status is Closed
                // if (loan.Status == (int)Status.Closed)
                // {
                //     return Json(new { success = false, message = "This loan is already closed/deleted" });
                // }

                // Get member details
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                // Get loan type
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

                // Get guarantors (include ALL, not just Transfered == false)
                var guarantors = await _context.Loanguar
                    .Where(g => g.LoanNo == loanNo)
                    .Select(g => new
                    {
                        Id = g.Id,
                        MemberNo = g.MemberNo,
                        Name = _context.Members.Where(m => m.MemberNo == g.MemberNo).Select(m => m.FullName).FirstOrDefault() ?? g.MemberNo,
                        Amount = g.Amount ?? 0,
                        Transfered = g.Transfered
                    })
                    .ToListAsync();

                // Get collateral guarantees (include ALL, not just Balance > 0)
                var collateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo)
                    .Select(cg => new
                    {
                        Id = cg.Id,
                        ColCode = cg.ColCode,
                        DocNo = cg.DocNo,
                        MarketValue = cg.Mktvalue,
                        GuaranteeAmount = cg.Balance
                    })
                    .ToListAsync();

                // Check if loan has endorsement
                var hasEndorsement = await _context.Endmain
                    .AnyAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);

                // Check if loan has cheque
                var hasCheque = await _context.Cheques
                    .AnyAsync(c => c.LoanNo == loanNo && c.CompanyCode == companyCode);

                // Check if loan has loan balance
                var hasLoanBal = await _context.Loanbal
                    .AnyAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);

                // Get status name
                string statusName = ((Status)(loan.Status ?? 0)).ToString();

                return Json(new
                {
                    success = true,
                    loan = new
                    {
                        loanNo = loan.LoanNo,
                        memberNo = loan.MemberNo,
                        memberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : loan.MemberNo,
                        loanType = loanType?.LoanType1 ?? loan.LoanCode,
                        principalAmount = loan.LoanAmt ?? 0,
                        status = statusName,
                        applicationDate = loan.ApplicDate.ToString("yyyy-MM-dd"),
                        interestRate = loan.Interest ?? 0,
                        repayPeriod = loan.RepayPeriod ?? 0,
                        repayMethod = loan.RepayMethod ?? loanType?.Repaymethod ?? "N/A",
                        guarantors = guarantors,
                        collateralGuarantees = collateralGuarantees,
                        hasEndorsement = hasEndorsement,
                        hasCheque = hasCheque,
                        hasLoanBal = hasLoanBal
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting loan details for deletion: {loanNo}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLoanForDeletion(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                _logger.LogInformation($"Getting loan details for deletion: {loanNo}");

                // Get the loan
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    return Json(new { success = false, message = "Loan not found" });
                }

                // Check if loan is already closed
                if (loan.Status == (int)Status.Closed)
                {
                    return Json(new { success = false, message = "This loan is already closed/deleted" });
                }

                // Get member details
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                // Get loan type
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

                // Get guarantors
                var guarantors = await _context.Loanguar
                    .Where(g => g.LoanNo == loanNo && g.Transfered == false)
                    .Select(g => new
                    {
                        Id = g.Id,
                        MemberNo = g.MemberNo,
                        Name = _context.Members.Where(m => m.MemberNo == g.MemberNo).Select(m => m.FullName).FirstOrDefault() ?? g.MemberNo,
                        Amount = g.Amount ?? 0
                    })
                    .ToListAsync();

                // Get collateral guarantees
                var collateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .Select(cg => new
                    {
                        Id = cg.Id,
                        ColCode = cg.ColCode,
                        DocNo = cg.DocNo,
                        MarketValue = cg.Mktvalue,
                        GuaranteeAmount = cg.Balance
                    })
                    .ToListAsync();

                // Check if loan has endorsement
                var hasEndorsement = await _context.Endmain
                    .AnyAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);

                // Get status name
                string statusName = ((Status)(loan.Status ?? 0)).ToString();

                return Json(new
                {
                    success = true,
                    loan = new
                    {
                        loanNo = loan.LoanNo,
                        memberNo = loan.MemberNo,
                        memberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : loan.MemberNo,
                        loanType = loanType?.LoanType1 ?? loan.LoanCode,
                        principalAmount = loan.LoanAmt ?? 0,
                        status = statusName,
                        applicationDate = loan.ApplicDate,
                        interestRate = loan.Interest ?? 0,
                        repayPeriod = loan.RepayPeriod ?? 0,
                        repayMethod = loan.RepayMethod ?? loanType?.Repaymethod ?? "N/A",
                        guarantors = guarantors,
                        collateralGuarantees = collateralGuarantees,
                        hasEndorsement = hasEndorsement
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting loan details for deletion: {loanNo}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDeleteLoan(string loanNo, string reason)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                _logger.LogInformation($"ConfirmDeleteLoan POST called for loan {loanNo}");

                // Validate input
                if (string.IsNullOrEmpty(loanNo))
                {
                    TempData["ErrorMessage"] = "Loan number is required";
                    return RedirectToAction("DeleteLoanIndex");
                }

                if (string.IsNullOrEmpty(reason))
                {
                    TempData["ErrorMessage"] = "Please provide a reason for deleting the loan";
                    return RedirectToAction("DeleteLoanIndex");
                }

                // Check permission (only admin or specific roles)
                if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to delete loans";
                    return RedirectToAction("AllLoans");
                }

                // Verify loan exists and is not already closed
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("DeleteLoanIndex");
                }

                if (loan.Status == (int)Status.Closed)
                {
                    TempData["ErrorMessage"] = "This loan is already closed/deleted";
                    return RedirectToAction("DeleteLoanIndex");
                }

                // Get counts before deletion for logging
                var guarantorCount = await _context.Loanguar
                    .CountAsync(g => g.LoanNo == loanNo && g.Transfered == false);

                var collateralCount = await _context.ColloanGuars
                    .CountAsync(cg => cg.LoanNo == loanNo && cg.Balance > 0);

                // Delete the loan and release all guarantees
                await _loanService.DeleteLoanAsync(loanNo, companyCode, User.Identity?.Name ?? "SYSTEM", reason);

                TempData["SuccessMessage"] = $"Loan {loanNo} has been successfully deleted. " +
                                             $"Released {guarantorCount} guarantor(s) and {collateralCount} collateral guarantee(s).";

                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting loan {loanNo}");
                TempData["ErrorMessage"] = $"Error deleting loan: {ex.Message}";
                return RedirectToAction("DeleteLoanIndex");
            }
        }

        #endregion


        #region All Loans View

        [HttpGet]
        public async Task<IActionResult> AllLoans(int page = 1, int pageSize = 10)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var searchDto = new LoanSearchDTO
                {
                    CompanyCode = companyCode
                };

                var allLoans = await _loanService.SearchLoansAsync(searchDto);

                // Load loan types for filter dropdown
                ViewBag.LoanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);

                // Calculate pagination
                var totalItems = allLoans.Count;
                var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                var loans = allLoans
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalItems = totalItems;

                return View(loans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading all loans");
                ViewBag.ErrorMessage = "Error loading loans";
                return View(new List<LoanSummaryDTO>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportAllLoans(LoanSearchDTO searchDto)
        {
            try
            {
                searchDto.CompanyCode = GetUserCompanyCode();
                var loans = await _loanService.SearchLoansAsync(searchDto);

                // Build CSV content
                var csv = new StringBuilder();
                csv.AppendLine("Loan No,Member No,Member Name,Loan Type,Principal Amount,Approved Amount,Disbursed Amount,Outstanding Balance,Application Date,Status");

                foreach (var loan in loans)
                {
                    csv.AppendLine($"\"{loan.LoanNo}\",\"{loan.MemberNo}\",\"{loan.MemberName}\",\"{loan.LoanType}\",{loan.PrincipalAmount},{loan.ApprovedAmount},{loan.DisbursedAmount},{loan.OutstandingBalance},\"{loan.ApplicationDate:dd/MM/yyyy}\",\"{loan.LoanStatus}\"");
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", $"AllLoans_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting all loans");
                ViewBag.ErrorMessage = "Error exporting data";
                return RedirectToAction("AllLoans");
            }
        }

        #endregion


        #region Dashboard

        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var dashboard = await _loanService.GetLoanDashboardAsync(companyCode);

                ViewBag.CompanyCode = companyCode;
                ViewBag.UserName = User.Identity?.Name;


                return View(dashboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading loan dashboard");
                return View("Error");
            }
        }

        #endregion


        #region Loan Application

        public async Task<IActionResult> Apply()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(companyCode);

                ViewBag.LoanTypes = loanTypes;
                ViewBag.CompanyCode = companyCode;

                return View(new LoanApplicationDTO
                {
                    CompanyCode = companyCode,
                    ApplicationDate = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading loan application form");
                return View("Error");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(LoanApplicationDTO application)
        {
            try
            {
                application.CompanyCode = GetUserCompanyCode();
                application.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var existingLoansCheck = await _loanService.CheckExistingLoansAsync(application.MemberNo, application.CompanyCode);
                if (existingLoansCheck.HasExistingLoan)
                {
                    var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(GetUserCompanyCode());
                    ViewBag.LoanTypes = loanTypes;
                    ViewBag.ExistingLoans = existingLoansCheck.ExistingLoans;
                    ModelState.AddModelError("MemberNo", existingLoansCheck.Message);
                    return View(application);
                }

                var eligibility = await _loanService.CheckMemberEligibilityWithContributionsAsync(application.MemberNo, application.CompanyCode);
                if (!eligibility.IsEligible)
                {
                    var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(GetUserCompanyCode());
                    ViewBag.LoanTypes = loanTypes;
                    ModelState.AddModelError("MemberNo", eligibility.Message);
                    return View(application);
                }

                var loanTypeEligibility = await _loanService.CheckMemberEligibilityAsync(application.MemberNo, application.LoanCode, application.CompanyCode);
                if (!loanTypeEligibility.IsEligible)
                {
                    var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(GetUserCompanyCode());
                    ViewBag.LoanTypes = loanTypes;
                    ModelState.AddModelError("", loanTypeEligibility.Message);
                    return View(application);
                }

                var loan = await _loanService.ApplyForLoanAsync(application);

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(application.LoanCode, application.CompanyCode);
                var requiresGuarantor = !string.IsNullOrEmpty(loanType.Guarantor) &&
                                        loanType.Guarantor != "No" &&
                                        loanType.Guarantor != "N";

                if (requiresGuarantor && (loan.Guaranteed != "0" && !string.IsNullOrEmpty(loan.Guaranteed)))
                {
                    TempData["SuccessMessage"] = $"Loan application created! Please assign the required guarantor(s).";
                    return RedirectToAction("AssignGuarantor", new { loanNo = loan.LoanNo });
                }

                TempData["SuccessMessage"] = $"Loan application {loan.LoanNo} submitted successfully!";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting loan application");

                var loanTypes = await _loanTypeService.GetLoanTypesByCompanyAsync(GetUserCompanyCode());
                ViewBag.LoanTypes = loanTypes;
                ViewBag.CompanyCode = GetUserCompanyCode();

                if (ex.Message.Contains("contributions") || ex.Message.Contains("eligibility") || ex.Message.Contains("loan"))
                {
                    ModelState.AddModelError("", ex.Message);
                }

                return View(application);
            }
        }

        public async Task<IActionResult> Details(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, loan.CompanyCode);

                var guarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var appraisal = await _loanService.GetLoanAppraisalAsync(loanNo);
                var approvals = await _loanService.GetLoanApprovalsAsync(loanNo);
                var disbursement = await _loanService.GetLoanDisbursementAsync(loanNo);  
                var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                var repayments = await _loanService.GetLoanRepaymentsAsync(loanNo);

                // ADD THIS: Get LoanBalance separately
                var loanBalance = await _loanService.GetLoanBalanceAsync(loanNo);  

                ViewBag.Guarantors = guarantors;
                ViewBag.Appraisal = appraisal;
                ViewBag.Approvals = approvals;
                ViewBag.Disbursement = disbursement; 
                ViewBag.LoanBalance = loanBalance;   
                ViewBag.Schedule = schedule;
                ViewBag.Repayments = repayments;
                ViewBag.LoanTypeName = loanType?.LoanType ?? "Unknown";
                ViewBag.CanEdit = loan.Status == (int)Status.Draft || loan.Status == (int)Status.Submitted;
                ViewBag.CanAppraise = loan.Status == (int)Status.Submitted;
                ViewBag.CanApprove = loan.Status == (int)Status.Submitted;
                ViewBag.CanEndorse = loan.Status == (int)Status.Approved;
                ViewBag.CanDisburse = loan.Status == (int)Status.Endorsed;
                ViewBag.CanRepay = loan.Status == (int)Status.Disbursed || loan.Status == (int)Status.Endorsed;

                return View(loan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading loan details for {loanNo}");
                return View("Error");
            }
        }

        #endregion



        #region Guarantor Management

        [HttpGet]
        public async Task<IActionResult> LoansNeedingGuarantors()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                _logger.LogInformation($"Loading loans needing guarantors for company: {companyCode}");

                // DEBUG: Test each database call individually
                _logger.LogInformation("Step 1: Getting loans from database...");

                // Get loans with Draft status (1)
                var rawLoans = await _context.Loans
                    .Where(l => l.CompanyCode == companyCode && l.Status == (int)Status.Draft)
                    .OrderByDescending(l => l.ApplicDate)
                    .Select(l => new
                    {
                        l.LoanNo,
                        l.MemberNo,
                        l.LoanAmt,
                        l.Status,
                        l.ApplicDate,
                        l.Guaranteed,
                        l.LoanCode
                    })
                    .ToListAsync();

                _logger.LogInformation($"Step 1 complete: Found {rawLoans.Count} loans");

                _logger.LogInformation("Step 2: Getting max guarantors from SACCO service...");
                var maxGuarantors = await _saccoService.GetMaxGuarantorsAsync(companyCode);
                _logger.LogInformation($"Step 2 complete: MaxGuarantors = {maxGuarantors}");

                var loansNeedingGuarantors = new List<object>();

                foreach (var loan in rawLoans)
                {
                    try
                    {
                        _logger.LogInformation($"Processing loan: {loan.LoanNo}");

                        // Get member name
                        var member = await _context.Members
                            .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                        var memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;
                        if (string.IsNullOrEmpty(memberName)) memberName = loan.MemberNo;

                        // Get loan type
                        var loanType = await _context.Loantypes
                            .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == companyCode);

                        var loanTypeName = loanType?.LoanType1 ?? loan.LoanCode ?? "Unknown";

                        // Check if loan requires guarantors
                        var requiresGuarantor = false;
                        var requiredGuarantorsCount = 0;

                        if (loanType != null && !string.IsNullOrEmpty(loanType.Guarantor))
                        {
                            var guarantorValue = loanType.Guarantor;
                            if (guarantorValue.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                                guarantorValue.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                                guarantorValue == "1")
                            {
                                requiresGuarantor = true;
                                requiredGuarantorsCount = 1;
                            }
                            else if (guarantorValue.Equals("No", StringComparison.OrdinalIgnoreCase) ||
                                     guarantorValue.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                                     guarantorValue == "0")
                            {
                                requiresGuarantor = false;
                                requiredGuarantorsCount = 0;
                            }
                            else if (int.TryParse(guarantorValue, out int count))
                            {
                                requiresGuarantor = count > 0;
                                requiredGuarantorsCount = count;
                            }
                        }

                        if (!requiresGuarantor)
                        {
                            continue;
                        }

                        // Get existing guarantors
                        var existingGuarantors = await _context.Loanguar
                            .Where(g => g.LoanNo == loan.LoanNo && g.Transfered == false)
                            .ToListAsync();

                        var totalGuarantee = existingGuarantors.Sum(g => g.Amount ?? 0);
                        var loanAmount = loan.LoanAmt ?? 0;
                        var isFullyGuaranteed = totalGuarantee >= loanAmount;
                        var assignedCount = existingGuarantors.Count;

                        var stillNeedsGuarantors = assignedCount < requiredGuarantorsCount || !isFullyGuaranteed;

                        if (stillNeedsGuarantors)
                        {
                            var selfGuaranteeEnabled = loanType?.SelfGuarantee ?? false;
                            var isApplicantGuarantor = existingGuarantors.Any(g => g.MemberNo == loan.MemberNo);

                            loansNeedingGuarantors.Add(new
                            {
                                LoanNo = loan.LoanNo,
                                MemberName = memberName,
                                LoanType = loanTypeName,
                                PrincipalAmount = loanAmount,
                                LoanStatus = "Draft",
                                TotalGuarantee = totalGuarantee,
                                RemainingAmount = loanAmount - totalGuarantee,
                                IsFullyGuaranteed = isFullyGuaranteed,
                                GuarantorCount = assignedCount,
                                MaxGuarantors = maxGuarantors,
                                RequiredGuarantors = requiredGuarantorsCount,
                                AssignedGuarantors = assignedCount,
                                ApprovedGuarantors = assignedCount,
                                RemainingRequired = requiredGuarantorsCount - assignedCount,
                                IsSelfGuarantee = selfGuaranteeEnabled,
                                IsApplicantGuarantor = isApplicantGuarantor,
                                NeedsGuarantors = true
                            });
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, $"Error processing loan {loan.LoanNo}");
                    }
                }

                ViewBag.MaxGuarantors = maxGuarantors;
                ViewBag.TotalLoans = rawLoans.Count;
                ViewBag.EligibleLoans = loansNeedingGuarantors.Count;

                return View(loansNeedingGuarantors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading loans needing guarantors");
                TempData["ErrorMessage"] = $"Error loading loans: {ex.Message}";
                return View(new List<object>());
            }
        }
        private int ParseGuaranteedValue(string? guaranteedValue)
        {
            if (string.IsNullOrEmpty(guaranteedValue))
                return 0;

            if (guaranteedValue.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                guaranteedValue.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                guaranteedValue == "1")
                return 1;

            if (guaranteedValue.Equals("No", StringComparison.OrdinalIgnoreCase) ||
                guaranteedValue.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                guaranteedValue == "0")
                return 0;

            if (int.TryParse(guaranteedValue, out int result))
                return result;

            return 0;
        }
        [HttpGet]
        public async Task<IActionResult> AssignGuarantor(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("LoansNeedingGuarantors");
                }

                _logger.LogInformation($"Loan {loanNo} status from DB: {loan.Status}");

                if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
                {
                    TempData["ErrorMessage"] = $"Cannot assign guarantors to loan in status '{loan.Status}'.";
                    return RedirectToAction("AllLoans");
                }

                // Get loan type
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);

                // Get max guarantors from SACCO parameters
                var maxGuarantors = await _saccoService.GetMaxGuarantorsAsync(companyCode);

                // Get existing member guarantors
                var existingGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var totalMemberGuarantee = existingGuarantors.Sum(g => g.GuaranteeAmount);

                // ============================================================
                // CRITICAL: LOAD COLLATERAL GUARANTEES FROM COLLOANGUAR TABLE
                // ============================================================
                // Get existing collateral guarantees directly from database
                var existingCollateralGuaranteesRaw = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == loanNo && cg.Balance > 0)
                    .ToListAsync();

                _logger.LogInformation($"Found {existingCollateralGuaranteesRaw.Count} collateral guarantees for loan {loanNo}");

                // Convert to DTOs with collateral descriptions
                var existingCollateralGuarantees = new List<CollateralGuaranteeResponseDTO>();

                // Get all collateral types for descriptions
                var collateralTypes = await _context.Collaterals
                    .Where(c => c.CompanyCode == companyCode)
                    .ToDictionaryAsync(c => c.ColCode, c => c);

                foreach (var cg in existingCollateralGuaranteesRaw)
                {
                    var collateral = collateralTypes.GetValueOrDefault(cg.ColCode);
                    existingCollateralGuarantees.Add(new CollateralGuaranteeResponseDTO
                    {
                        Id = cg.Id,
                        ColCode = cg.ColCode,
                        Coldescription = collateral?.Coldescription ?? cg.ColCode,
                        DocNo = cg.DocNo,
                        MarketValue = cg.Mktvalue,
                        GuaranteeAmount = cg.Balance,
                        RemainingBalance = cg.Balance,
                        AssignedDate = DateTime.Now,
                        BlockchainTxId = cg.BlockchainTxId
                    });
                }

                var totalCollateralGuarantee = existingCollateralGuarantees.Sum(g => g.GuaranteeAmount);

                // Calculate totals including collateral
                var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
                var loanAmount = loan.LoanAmt ?? 0;
                var remainingAmount = loanAmount - totalGuarantee;
                var isFullyGuaranteed = remainingAmount <= 0;

                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;
                var canProceed = isFullyGuaranteed || isSelfGuarantee;

                // Get all available collateral types for dropdown
                var allCollateralTypes = await _context.Collaterals
                    .Where(c => c.CompanyCode == companyCode)
                    .OrderBy(c => c.ColCode)
                    .ToListAsync();

                _logger.LogInformation($"Loading {allCollateralTypes.Count} collateral types for company {companyCode}");

                // Set ViewBag properties
                ViewBag.Loan = loan;
                ViewBag.LoanType = loanType;
                ViewBag.ExistingGuarantors = existingGuarantors;
                ViewBag.TotalGuarantee = totalGuarantee;
                ViewBag.TotalMemberGuarantee = totalMemberGuarantee;
                ViewBag.TotalCollateralGuarantee = totalCollateralGuarantee;
                ViewBag.RemainingAmount = remainingAmount > 0 ? remainingAmount : 0;
                ViewBag.IsFullyGuaranteed = isFullyGuaranteed;
                ViewBag.CanProceed = canProceed;
                ViewBag.IsSelfGuarantee = isSelfGuarantee;
                ViewBag.CompanyCode = companyCode;
                ViewBag.MaxGuarantors = maxGuarantors;
                ViewBag.LoanAmount = loanAmount;

                // CRITICAL: These must be set for the view to show collaterals
                ViewBag.CollateralTypes = allCollateralTypes;
                ViewBag.ExistingCollateralGuarantees = existingCollateralGuarantees;

                return View(loan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading guarantor assignment for {loanNo}");
                TempData["ErrorMessage"] = $"Error loading guarantor assignment: {ex.Message}";
                return RedirectToAction("LoansNeedingGuarantors");
            }
        }
        [HttpGet]
        public async Task<IActionResult> DebugLoanStatus(string loanNo)
        {
            var companyCode = GetUserCompanyCode();

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

            if (loan == null)
            {
                return Json(new { error = "Loan not found" });
            }

            var result = new
            {
                LoanNo = loan.LoanNo,
                StatusFromDB = loan.Status,
                LoanAmt = loan.LoanAmt,
                Interest = loan.Interest,
                RepayPeriod = loan.RepayPeriod,
                CreatedAt = loan.AuditDateTime,
                ApplicationDate = loan.ApplicDate
            };

            return Json(result);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddGuarantor(string loanNo, string guarantorMemberNo, decimal guaranteeAmount, string remarks)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                _logger.LogInformation($"Adding guarantor to loan {loanNo}. Member: {guarantorMemberNo}, Amount: {guaranteeAmount}");

                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

                _logger.LogInformation($"Loan {loanNo} current status: {loan.Status}");

                if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
                {
                    return Json(new { success = false, message = $"Cannot add guarantors to loan in status '{loan.Status}'. Loan must be in Draft or Submitted status." });
                }

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;

                bool isSelfGuarantor = guarantorMemberNo == loan.MemberNo;

                if (!isSelfGuarantee && isSelfGuarantor)
                {
                    return Json(new { success = false, message = "Self guarantee is not allowed for this loan type. Please add another member as guarantor." });
                }

                if (guaranteeAmount < 1000)
                {
                    return Json(new { success = false, message = "Guarantee amount must be at least KES 1,000." });
                }

                if (!isSelfGuarantor)
                {
                    var existingGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
                    if (existingGuarantors.Any(g => g.GuarantorMemberNo == guarantorMemberNo))
                    {
                        return Json(new { success = false, message = "This member is already a guarantor for this loan." });
                    }
                }

                var guarantor = new GuarantorAssignmentDTO
                {
                    GuarantorMemberNo = guarantorMemberNo,
                    GuaranteeAmount = guaranteeAmount,
                    Remarks = remarks,
                    CompanyCode = companyCode
                };

                var result = await _loanService.AssignGuarantorAsync(loanNo, guarantor, User.Identity?.Name ?? "SYSTEM");

                // Return JSON success response
                return Json(new
                {
                    success = true,
                    message = $"Guarantor {guarantorMemberNo} assigned with KES {guaranteeAmount:N0}!",
                    guarantorId = result.Id,
                    guarantorMemberNo = result.MemberNo,
                    guaranteeAmount = result.Amount,
                    loanStatus = loan.Status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning guarantor for {loanNo}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveGuarantor(int guarantorId, string loanNo)
        {
            try
            {
                await _loanService.RejectGuarantorAsync(guarantorId, "Removed by user", User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = "Guarantor removed successfully";
                return RedirectToAction("AssignGuarantor", new { loanNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing guarantor {guarantorId}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AssignGuarantor", new { loanNo });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitToAppraisal(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

                _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo} status from DB: {loan.Status}");

                // Allow both Draft and Submitted status to be submitted
                if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
                {
                    TempData["ErrorMessage"] = $"Cannot submit loan to appraisal. Current status: '{loan.Status}'. Loan must be in Draft or Submitted status.";
                    return RedirectToAction("AssignGuarantor", new { loanNo });
                }

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;

                // 1. Get member guarantors from Loanguar table
                var memberGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var totalMemberGuarantee = memberGuarantors.Sum(g => g.GuaranteeAmount);

                // 2. Get collateral guarantees from ColloanGuar table
                var collateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);
                var totalCollateralGuarantee = collateralGuarantees.Sum(g => g.GuaranteeAmount);

                // 3. Calculate TOTAL guarantee
                var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
                var loanAmount = loan.LoanAmt ?? 0;

                var isFullyGuaranteed = totalGuarantee >= loanAmount;
                var isApplicantGuarantor = memberGuarantors.Any(g => g.GuarantorMemberNo == loan.MemberNo);

                // Log the values for debugging
                _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo}:");
                _logger.LogInformation($"  - Member Guarantee: KES {totalMemberGuarantee:N0}");
                _logger.LogInformation($"  - Collateral Guarantee: KES {totalCollateralGuarantee:N0}");
                _logger.LogInformation($"  - Total Guarantee: KES {totalGuarantee:N0}");
                _logger.LogInformation($"  - Loan Amount: KES {loanAmount:N0}");
                _logger.LogInformation($"  - Is Self Guarantee: {isSelfGuarantee}");
                _logger.LogInformation($"  - Is Applicant Guarantor: {isApplicantGuarantor}");

                // ✅ FIX: Allow submission if self-guarantee is enabled and applicant is a guarantor
                // OR if loan is fully guaranteed
                bool canSubmit = false;

                if (isSelfGuarantee && isApplicantGuarantor)
                {
                    // Self-guarantee enabled and applicant is a guarantor - can submit even if not fully guaranteed
                    canSubmit = true;
                    _logger.LogInformation($"Self-guarantee enabled. Loan can be submitted with partial guarantee of KES {totalGuarantee:N0} out of KES {loanAmount:N0}");
                }
                else if (isFullyGuaranteed)
                {
                    // Fully guaranteed by other members or collateral
                    canSubmit = true;
                    _logger.LogInformation($"Loan fully guaranteed. Can submit to appraisal.");
                }
                else
                {
                    var remaining = loanAmount - totalGuarantee;
                    TempData["ErrorMessage"] = $"Loan requires guarantee of KES {remaining:N0} more. Total guarantee: KES {totalGuarantee:N0}, Loan amount: KES {loanAmount:N0}. Please add more guarantees or enable self-guarantee.";
                    return RedirectToAction("AssignGuarantor", new { loanNo });
                }

                if (!canSubmit)
                {
                    TempData["ErrorMessage"] = "Cannot submit loan to appraisal. Please ensure guarantees are in place.";
                    return RedirectToAction("AssignGuarantor", new { loanNo });
                }

                // Update loan status to Submitted (if not already)
                if (loan.Status != (int)Status.Submitted)
                {
                    loan.Status = (int)Status.Submitted;
                }
                loan.Posted = "SUBMIT";
                loan.UserName = User.Identity?.Name ?? "SYSTEM";
                loan.AuditDateTime = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Loan {loanNo} has been submitted for appraisal!";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting loan {loanNo} to appraisal");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AssignGuarantor", new { loanNo });
            }
        }       


        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> SubmitToAppraisal(string loanNo)
        //{
        //    try
        //    {
        //        var companyCode = GetUserCompanyCode();

        //        var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

        //        _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo} status from DB: {loan.Status}");

        //        // Allow both Draft and Submitted status to be submitted
        //        if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
        //        {
        //            TempData["ErrorMessage"] = $"Cannot submit loan to appraisal. Current status: '{loan.Status}'. Loan must be in Draft or Submitted status.";
        //            return RedirectToAction("AssignGuarantor", new { loanNo });
        //        }

        //        var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
        //        var isSelfGuarantee = loanType?.SelfGuarantee ?? false;

        //        // ============================================================
        //        // CRITICAL FIX: GET BOTH MEMBER AND COLLATERAL GUARANTEES
        //        // ============================================================

        //        // 1. Get member guarantors from Loanguar table
        //        var memberGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
        //        var totalMemberGuarantee = memberGuarantors.Sum(g => g.GuaranteeAmount);

        //        // 2. Get collateral guarantees from ColloanGuar table
        //        var collateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);
        //        var totalCollateralGuarantee = collateralGuarantees.Sum(g => g.GuaranteeAmount);

        //        // 3. Calculate TOTAL guarantee
        //        var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
        //        var loanAmount = loan.LoanAmt ?? 0;

        //        var isFullyGuaranteed = totalGuarantee >= loanAmount;
        //        var isApplicantGuarantor = memberGuarantors.Any(g => g.GuarantorMemberNo == loan.MemberNo);

        //        // Log the values for debugging
        //        _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo}:");
        //        _logger.LogInformation($"  - Member Guarantee: KES {totalMemberGuarantee:N0}");
        //        _logger.LogInformation($"  - Collateral Guarantee: KES {totalCollateralGuarantee:N0}");
        //        _logger.LogInformation($"  - Total Guarantee: KES {totalGuarantee:N0}");
        //        _logger.LogInformation($"  - Loan Amount: KES {loanAmount:N0}");
        //        _logger.LogInformation($"  - Is Fully Guaranteed: {isFullyGuaranteed}");

        //        if (!isFullyGuaranteed && !(isSelfGuarantee && isApplicantGuarantor))
        //        {
        //            var remaining = loanAmount - totalGuarantee;
        //            TempData["ErrorMessage"] = $"Loan requires guarantee of KES {remaining:N0} more. Total guarantee: KES {totalGuarantee:N0}, Loan amount: KES {loanAmount:N0}";
        //            return RedirectToAction("AssignGuarantor", new { loanNo });
        //        }

        //        // Update loan status to Submitted (if not already)
        //        if (loan.Status != (int)Status.Submitted)
        //        {
        //            loan.Status = (int)Status.Submitted;
        //        }
        //        loan.Posted = "SUBMIT";
        //        loan.UserName = User.Identity?.Name ?? "SYSTEM";
        //        loan.AuditDateTime = DateTime.Now;
        //        await _context.SaveChangesAsync();

        //        TempData["SuccessMessage"] = $"Loan {loanNo} has been submitted for appraisal!";
        //        return RedirectToAction("AllLoans");
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Error submitting loan {loanNo} to appraisal");
        //        TempData["ErrorMessage"] = ex.Message;
        //        return RedirectToAction("AssignGuarantor", new { loanNo });
        //    }
        //}

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> SubmitToAppraisal(string loanNo)
        //{
        //    try
        //    {
        //        var companyCode = GetUserCompanyCode();

        //        var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

        //        _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo} status from DB: {loan.Status}");

        //        // Allow both Draft and Submitted status to be submitted
        //        if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
        //        {
        //            TempData["ErrorMessage"] = $"Cannot submit loan to appraisal. Current status: '{loan.Status}'. Loan must be in Draft or Submitted status.";
        //            return RedirectToAction("AssignGuarantor", new { loanNo });
        //        }

        //        var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
        //        var isSelfGuarantee = loanType?.SelfGuarantee ?? false;

        //        // ============================================================
        //        // FIX: CHECK BOTH MEMBER AND COLLATERAL GUARANTEES
        //        // ============================================================

        //        // 1. Get member guarantors
        //        var memberGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
        //        var totalMemberGuarantee = memberGuarantors.Sum(g => g.GuaranteeAmount);

        //        // 2. Get collateral guarantees
        //        var collateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);
        //        var totalCollateralGuarantee = collateralGuarantees.Sum(g => g.GuaranteeAmount);

        //        // 3. Calculate total guarantee
        //        var totalGuarantee = totalMemberGuarantee + totalCollateralGuarantee;
        //        var loanAmount = loan.LoanAmt ?? 0;

        //        var isFullyGuaranteed = totalGuarantee >= loanAmount;
        //        var isApplicantGuarantor = memberGuarantors.Any(g => g.GuarantorMemberNo == loan.MemberNo);

        //        _logger.LogInformation($"SubmitToAppraisal - Loan {loanNo}: Member Guarantee: {totalMemberGuarantee:C}, Collateral Guarantee: {totalCollateralGuarantee:C}, Total: {totalGuarantee:C}, Loan Amount: {loanAmount:C}");

        //        if (!isFullyGuaranteed && !(isSelfGuarantee && isApplicantGuarantor))
        //        {
        //            var remaining = loanAmount - totalGuarantee;
        //            TempData["ErrorMessage"] = $"Loan requires guarantee of KES {remaining:N0} more. Total guarantee: KES {totalGuarantee:N0}, Loan amount: KES {loanAmount:N0}";
        //            return RedirectToAction("AssignGuarantor", new { loanNo });
        //        }

        //        // Update loan status to Submitted (if not already)
        //        if (loan.Status != (int)Status.Submitted)
        //        {
        //            loan.Status = (int)Status.Submitted;
        //        }
        //        loan.Posted = "SUBMIT";
        //        loan.UserName = User.Identity?.Name ?? "SYSTEM";
        //        loan.AuditDateTime = DateTime.Now;
        //        await _context.SaveChangesAsync();

        //        TempData["SuccessMessage"] = $"Loan {loanNo} has been submitted for appraisal!";
        //        return RedirectToAction("AllLoans");
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Error submitting loan {loanNo} to appraisal");
        //        TempData["ErrorMessage"] = ex.Message;
        //        return RedirectToAction("AssignGuarantor", new { loanNo });
        //    }
        //}


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SkipGuarantors(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;

                if (!isSelfGuarantee)
                {
                    TempData["ErrorMessage"] = "This loan requires guarantors. Please add guarantors or enable self guarantee.";
                    return RedirectToAction("AssignGuarantor", new { loanNo });
                }

                await _loanService.UpdateLoanStatusAsync(loanNo, Status.Submitted.ToString(), User.Identity?.Name ?? "SYSTEM",
                      "Self guarantee enabled. Moving to appraisal.");

                TempData["SuccessMessage"] = $"Loan {loanNo} submitted for appraisal (self guarantee).";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting loan {loanNo}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AssignGuarantor", new { loanNo });
            }
        }

        #endregion


        #region Collateral Guarantee Management

        [HttpGet]
        public async Task<IActionResult> AssignCollateralGuarantee(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // LOG THE COMPANY CODE FOR DEBUGGING
                _logger.LogInformation($"=== AssignCollateralGuarantee called ===");
                _logger.LogInformation($"CompanyCode: '{companyCode}'");
                _logger.LogInformation($"LoanNo: '{loanNo}'");

                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("AllLoans");
                }

                if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
                {
                    TempData["ErrorMessage"] = $"Cannot add collateral guarantees to loan in status '{loan.Status}'";
                    return RedirectToAction("AllLoans");
                }

                // ============================================================
                // GET ALL COLLATERAL TYPES
                // ============================================================
                var allCollateralTypes = await _context.Collaterals
                    .Where(c => c.CompanyCode == companyCode)
                    .OrderBy(c => c.ColCode)
                    .ToListAsync();

                // ============================================================
                // GET USED COLLATERALS (already assigned to ANY active loan)
                // ============================================================
                // Get all active collateral guarantees for ANY loan (not just this one)
                // This prevents using the same collateral document for multiple loans
                var usedCollaterals = await _context.ColloanGuars
                    .Where(cg => cg.CompanyCode == companyCode && cg.Balance > 0)
                    .Select(cg => new { cg.ColCode, cg.DocNo })
                    .ToListAsync();

                // Create a set of used collateral identifiers (ColCode + DocNo combination)
                var usedCollateralKeys = usedCollaterals
                    .Select(u => $"{u.ColCode}|{u.DocNo}")
                    .ToHashSet();

                _logger.LogInformation($"Found {usedCollateralKeys.Count} used collateral document(s)");

                // ============================================================
                // FILTER OUT USED COLLATERALS FROM DROPDOWN
                // ============================================================
                // For collateral types dropdown, we need to know which ones have 
                // available documents. Since the same ColCode can have multiple DocNo,
                // we need to track available documents per collateral type.

                // Get all used documents grouped by ColCode
                var usedDocumentsByColCode = usedCollaterals
                    .GroupBy(u => u.ColCode)
                    .ToDictionary(g => g.Key, g => g.Select(u => u.DocNo).ToHashSet());

                // Build a list of available collaterals with their available document counts
                var availableCollaterals = new List<dynamic>();

                foreach (var collateral in allCollateralTypes)
                {
                    // Get used documents for this collateral type
                    var usedDocs = usedDocumentsByColCode.ContainsKey(collateral.ColCode)
                        ? usedDocumentsByColCode[collateral.ColCode]
                        : new HashSet<string>();

                    // For now, we don't have a list of all documents per collateral type.
                    // In a real system, you would have a MemberCollateral table.
                    // For this implementation, we'll assume each collateral type can be used
                    // multiple times with different document numbers, but the SAME document
                    // cannot be reused.

                    // We'll still show the collateral type in dropdown, but validation
                    // will prevent reusing the same document number.

                    availableCollaterals.Add(new
                    {
                        collateral.ColCode,
                        collateral.Coldescription,
                        collateral.Percentage,
                        HasUsedDocuments = usedDocs.Any(),
                        UsedDocumentCount = usedDocs.Count
                    });
                }

                // Get existing collateral guarantees for THIS loan
                var existingCollateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);

                _logger.LogInformation($"Found {existingCollateralGuarantees.Count} collateral guarantees for loan {loanNo}");
                foreach (var g in existingCollateralGuarantees)
                {
                    _logger.LogInformation($"  - Collateral: {g.ColCode}, Doc: {g.DocNo}, Amount: {g.GuaranteeAmount:C}");
                }

                var totalCollateralGuarantee = existingCollateralGuarantees.Sum(g => g.GuaranteeAmount);

                // Get existing member guarantees
                var existingMemberGuarantees = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var totalMemberGuarantee = existingMemberGuarantees.Sum(g => g.GuaranteeAmount);

                var totalGuarantee = totalCollateralGuarantee + totalMemberGuarantee;
                var loanAmount = loan.LoanAmt ?? 0;
                var remainingAmount = loanAmount - totalGuarantee;
                var isFullyGuaranteed = remainingAmount <= 0;

                // Get loan type
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

                // Get max guarantors from SACCO parameters
                var saccoParams = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

                // Get existing member guarantors
                var existingGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);

                // ============================================================
                // SET ALL ViewBag PROPERTIES
                // ============================================================
                ViewBag.Loan = loan;
                ViewBag.LoanAmount = loanAmount;
                ViewBag.TotalGuarantee = totalGuarantee;
                ViewBag.TotalCollateralGuarantee = totalCollateralGuarantee;
                ViewBag.TotalMemberGuarantee = totalMemberGuarantee;
                ViewBag.RemainingAmount = remainingAmount > 0 ? remainingAmount : 0;
                ViewBag.IsFullyGuaranteed = isFullyGuaranteed;
                ViewBag.ExistingCollateralGuarantees = existingCollateralGuarantees;
                ViewBag.CollateralTypes = allCollateralTypes;  // Pass all types, but we'll track used docs
                ViewBag.UsedCollateralKeys = usedCollateralKeys;  // Pass used keys for validation
                ViewBag.UsedDocumentsByColCode = usedDocumentsByColCode;
                ViewBag.CompanyCode = companyCode;
                ViewBag.IsSelfGuarantee = loanType?.SelfGuarantee ?? false;
                ViewBag.MaxGuarantors = saccoParams?.MaxGuarantor ?? 5;
                ViewBag.LoanType = loanType;
                ViewBag.ExistingGuarantors = existingGuarantors;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading collateral guarantee assignment for {loanNo}");
                TempData["ErrorMessage"] = $"Error loading page: {ex.Message}";
                return RedirectToAction("AllLoans");
            }
        }

        //[HttpGet]
        //public async Task<IActionResult> AssignCollateralGuarantee(string loanNo)
        //{
        //    try
        //    {
        //        var companyCode = GetUserCompanyCode();

        //        // LOG THE COMPANY CODE FOR DEBUGGING
        //        _logger.LogInformation($"=== AssignCollateralGuarantee called ===");
        //        _logger.LogInformation($"CompanyCode: '{companyCode}'");
        //        _logger.LogInformation($"LoanNo: '{loanNo}'");

        //        var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

        //        if (loan == null)
        //        {
        //            TempData["ErrorMessage"] = "Loan not found";
        //            return RedirectToAction("AllLoans");
        //        }

        //        if (loan.Status != (int)Status.Draft && loan.Status != (int)Status.Submitted)
        //        {
        //            TempData["ErrorMessage"] = $"Cannot add collateral guarantees to loan in status '{loan.Status}'";
        //            return RedirectToAction("AllLoans");
        //        }

        //        // ============================================================
        //        // GET COLLATERAL TYPES - SINGLE ASSIGNMENT ONLY
        //        // ============================================================
        //        var collateralTypes = await _context.Collaterals
        //            .Where(c => c.CompanyCode == companyCode)
        //            .OrderBy(c => c.ColCode)
        //            .ToListAsync();

        //        // LOG THE RESULTS
        //        _logger.LogInformation($"Found {collateralTypes.Count} collateral types for company '{companyCode}'");
        //        foreach (var c in collateralTypes)
        //        {
        //            _logger.LogInformation($"  - {c.ColCode}: {c.Coldescription} ({c.Percentage}%)");
        //        }

        //        // Get existing collateral guarantees for this loan
        //        var existingCollateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);

        //        _logger.LogInformation($"Found {existingCollateralGuarantees.Count} collateral guarantees for loan {loanNo}");
        //        foreach (var g in existingCollateralGuarantees)
        //        {
        //            _logger.LogInformation($"  - Collateral: {g.ColCode}, Doc: {g.DocNo}, Amount: {g.GuaranteeAmount:C}");
        //        }

        //        var totalCollateralGuarantee = existingCollateralGuarantees.Sum(g => g.GuaranteeAmount);

        //        // Get existing member guarantees
        //        var existingMemberGuarantees = await _loanService.GetLoanGuarantorsAsync(loanNo);
        //        var totalMemberGuarantee = existingMemberGuarantees.Sum(g => g.GuaranteeAmount);

        //        var totalGuarantee = totalCollateralGuarantee + totalMemberGuarantee;
        //        var loanAmount = loan.LoanAmt ?? 0;
        //        var remainingAmount = loanAmount - totalGuarantee;
        //        var isFullyGuaranteed = remainingAmount <= 0;

        //        // ============================================================
        //        // GET LOAN TYPE - FIXED: Use a variable, not the class name
        //        // ============================================================
        //        var loanType = await _context.Loantypes
        //            .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

        //        // Get max guarantors from SACCO parameters
        //        var saccoParams = await _context.SaccoParram
        //            .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

        //        // Get existing member guarantors
        //        var existingGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);

        //        // ============================================================
        //        // SET ALL ViewBag PROPERTIES - ONLY ONCE EACH
        //        // ============================================================
        //        ViewBag.Loan = loan;
        //        ViewBag.LoanAmount = loanAmount;
        //        ViewBag.TotalGuarantee = totalGuarantee;
        //        ViewBag.TotalCollateralGuarantee = totalCollateralGuarantee;
        //        ViewBag.TotalMemberGuarantee = totalMemberGuarantee;
        //        ViewBag.RemainingAmount = remainingAmount > 0 ? remainingAmount : 0;
        //        ViewBag.IsFullyGuaranteed = isFullyGuaranteed;
        //        ViewBag.ExistingCollateralGuarantees = existingCollateralGuarantees;
        //        ViewBag.CollateralTypes = collateralTypes;
        //        ViewBag.CompanyCode = companyCode;

        //        // FIXED: Use the loanType variable, not the class name 'Loantype'
        //        ViewBag.IsSelfGuarantee = loanType?.SelfGuarantee ?? false;
        //        ViewBag.MaxGuarantors = saccoParams?.MaxGuarantor ?? 5;
        //        ViewBag.LoanType = loanType;
        //        ViewBag.ExistingGuarantors = existingGuarantors;

        //        return View();
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Error loading collateral guarantee assignment for {loanNo}");
        //        TempData["ErrorMessage"] = $"Error loading page: {ex.Message}";
        //        return RedirectToAction("AllLoans");
        //    }
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCollateralGuarantee(CollateralGuaranteeDTO guaranteeDto)
        {
            try
            {
                _logger.LogInformation($"=== ADD COLLATERAL GUARANTEE POST ===");
                _logger.LogInformation($"LoanNo: {guaranteeDto.LoanNo}");
                _logger.LogInformation($"ColCode: {guaranteeDto.ColCode}");
                _logger.LogInformation($"Amount: {guaranteeDto.GuaranteeAmount:C}");

                guaranteeDto.CompanyCode = GetUserCompanyCode();

                var result = await _loanService.AssignCollateralGuaranteeAsync(guaranteeDto, User.Identity?.Name ?? "SYSTEM");

                _logger.LogInformation($"Collateral guarantee created with ID: {result.Id}, Balance: {result.Balance:C}");

                // Verify it was saved
                var verify = await _context.ColloanGuars.FirstOrDefaultAsync(c => c.Id == result.Id);
                _logger.LogInformation($"Verification - Found: {verify != null}, Balance: {verify?.Balance:C}");

                TempData["SuccessMessage"] = $"Collateral {guaranteeDto.ColCode} (Doc: {guaranteeDto.DocNo}) assigned as guarantee for KES {guaranteeDto.GuaranteeAmount:N0}";

                return RedirectToAction("AssignCollateralGuarantee", new { loanNo = guaranteeDto.LoanNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding collateral guarantee for loan {guaranteeDto.LoanNo}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AssignCollateralGuarantee", new { loanNo = guaranteeDto.LoanNo });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCollateralGuarantee(long collateralGuaranteeId, string loanNo, string reason)
        {
            try
            {
                _logger.LogInformation($"Removing collateral guarantee {collateralGuaranteeId} from loan {loanNo}");

                await _loanService.ReleaseCollateralGuaranteeAsync(collateralGuaranteeId, User.Identity?.Name ?? "SYSTEM", reason);

                TempData["SuccessMessage"] = "Collateral guarantee removed successfully";

                return RedirectToAction("AssignCollateralGuarantee", new { loanNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing collateral guarantee {collateralGuaranteeId}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AssignCollateralGuarantee", new { loanNo });
            }
        }

        #endregion


        #region Loan Appraisal

        [HttpGet]
        public async Task<IActionResult> PendingAppraisal()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // Get loans with Submitted status (2) directly
                var submittedLoans = await _context.Loans
                    .Where(l => l.CompanyCode == companyCode && l.Status == (int)Status.Submitted)
                    .OrderByDescending(l => l.ApplicDate)
                    .ToListAsync();

                _logger.LogInformation($"Found {submittedLoans.Count} loans with Submitted status");

                var pendingAppraisal = new List<dynamic>();

                foreach (var loan in submittedLoans)
                {
                    // Check if already appraised
                    var existingAppraisal = await _context.Appraisal
                        .FirstOrDefaultAsync(a => a.LoanNo == loan.LoanNo);

                    if (existingAppraisal != null)
                    {
                        _logger.LogInformation($"Loan {loan.LoanNo} already appraised, skipping");
                        continue;
                    }

                    // Get member name
                    var member = await _context.Members
                        .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                    var memberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo;
                    if (string.IsNullOrEmpty(memberName)) memberName = loan.MemberNo;

                    // Get loan type name
                    var loanType = await _context.Loantypes
                        .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == companyCode);

                    var loanTypeName = loanType?.LoanType1 ?? loan.LoanCode ?? "Unknown";

                    // Get total guarantee
                    var existingGuarantors = await _context.Loanguar
                        .Where(g => g.LoanNo == loan.LoanNo && g.Transfered == false)
                        .ToListAsync();

                    var totalGuarantee = existingGuarantors.Sum(g => g.Amount ?? 0);

                    pendingAppraisal.Add(new
                    {
                        LoanNo = loan.LoanNo,
                        MemberName = memberName,
                        LoanType = loanTypeName,
                        PrincipalAmount = loan.LoanAmt ?? 0,
                        TotalGuarantee = totalGuarantee,
                        ApplicationDate = loan.ApplicDate
                    });
                }

                ViewBag.Count = pendingAppraisal.Count;
                _logger.LogInformation($"Returning {pendingAppraisal.Count} loans for appraisal");

                return View(pendingAppraisal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pending appraisal loans");
                TempData["ErrorMessage"] = $"Error loading loans pending appraisal: {ex.Message}";
                return View(new List<dynamic>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Appraise(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);
                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("Index");
                }

                if (loan.Status != (int)Status.Submitted)
                {
                    TempData["ErrorMessage"] = $"Loan cannot be appraised in status '{loan.Status}'. Loan must be in Submitted status.";
                    return RedirectToAction("AllLoans");
                }

                var existingAppraisal = await _loanService.GetLoanAppraisalAsync(loanNo);
                if (existingAppraisal != null)
                {
                    TempData["ErrorMessage"] = "This loan has already been appraised.";
                    return RedirectToAction("AllLoans");
                }

                var member = await _memberService.GetMemberByMemberNoAsync(loan.MemberNo);
                if (member == null)
                {
                    TempData["ErrorMessage"] = "Member not found";
                    return RedirectToAction("Index");
                }

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);

                var requiresGuarantor = !string.IsNullOrEmpty(loanType.Guarantor) &&
                                        loanType.Guarantor != "No" &&
                                        loanType.Guarantor != "N";

                // GET TOTAL GUARANTEE
                var totalGuarantee = await _loanService.GetTotalGuaranteeForLoanAsync(loanNo, companyCode);

                // Also get individual breakdown for display
                var memberGuarantors = await _loanService.GetLoanGuarantorsAsync(loanNo);
                var totalMemberGuarantee = memberGuarantors.Sum(g => g.GuaranteeAmount);

                var collateralGuarantees = await _loanService.GetLoanCollateralGuaranteesAsync(loanNo);
                var totalCollateralGuarantee = collateralGuarantees.Sum(g => g.GuaranteeAmount);

                var loanAmount = loan.LoanAmt ?? 0;
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;
                var isApplicantGuarantor = memberGuarantors.Any(g => g.GuarantorMemberNo == loan.MemberNo);

                _logger.LogInformation($"Loan {loanNo}: Member Guarantee: {totalMemberGuarantee:C}, Collateral Guarantee: {totalCollateralGuarantee:C}, Total: {totalGuarantee:C}");

                // ✅ FIX: Amount to appraise = MIN(loanAmount, totalGuarantee)
                // If guarantee is less than loan amount, only appraise the guaranteed amount
                decimal amountToAppraise;
                string amountSource;

                if (requiresGuarantor)
                {
                    // Cap the appraisal amount by the total guarantee
                    amountToAppraise = Math.Min(loanAmount, totalGuarantee);

                    if (totalGuarantee <= 0)
                    {
                        TempData["ErrorMessage"] = "This loan requires guarantors but no guarantees found. Please add member guarantors or collateral guarantees first.";
                        return RedirectToAction("AssignGuarantor", new { loanNo });
                    }

                    if (amountToAppraise <= 0)
                    {
                        TempData["ErrorMessage"] = $"Cannot appraise loan. Total guarantee amount is {totalGuarantee:C} which is less than minimum appraisal amount.";
                        return RedirectToAction("AssignGuarantor", new { loanNo });
                    }

                    if (isSelfGuarantee && isApplicantGuarantor)
                    {
                        amountSource = $"Appraisal Amount Limited to Guarantee: KES {amountToAppraise:N0} (Loan Applied: {loanAmount:C}, Total Guarantee: {totalGuarantee:C}) - Self Guarantee Enabled";
                    }
                    else
                    {
                        amountSource = $"Appraisal Amount Limited to Guarantee: KES {amountToAppraise:N0} (Loan Applied: {loanAmount:C}, Total Guarantee: {totalGuarantee:C})";
                    }

                    _logger.LogInformation($"Appraisal amount capped at guarantee: {amountToAppraise:C} (Loan: {loanAmount:C}, Guarantee: {totalGuarantee:C})");
                }
                else
                {
                    amountToAppraise = loanAmount;
                    amountSource = "Applied Principal Amount (No Guarantor Required)";
                }

                decimal interestRate = 0;
                if (!string.IsNullOrEmpty(loanType.Interest) && decimal.TryParse(loanType.Interest, out interestRate))
                {
                    if (interestRate > 1 && interestRate <= 100)
                    {
                        interestRate = interestRate / 100;
                    }
                }

                var appraisalDto = new LoanAppraisalDTO
                {
                    LoanNo = loanNo,
                    CompanyCode = companyCode,
                    AppraisedBy = User.Identity?.Name ?? "SYSTEM",
                    AppliedAmount = loanAmount,
                    RecommendedAmount = amountToAppraise,
                    RecommendedInterestRate = interestRate * 100,
                    RecommendedPeriod = loan.RepayPeriod ?? 12,
                    AppraisalNotes = $"Loan Type: {loanType.LoanType}\n" +
                                    $"Loan Applied: KES {loanAmount:N0}\n" +
                                    $"Total Guarantee Available: KES {totalGuarantee:N0}\n" +
                                    $"Amount to Appraise: KES {amountToAppraise:N0}\n" +
                                    $"Member Guarantee: KES {totalMemberGuarantee:N0}\n" +
                                    $"Collateral Guarantee: KES {totalCollateralGuarantee:N0}\n"
                };

                ViewBag.Loan = loan;
                ViewBag.Member = member;
                ViewBag.LoanType = loanType;
                ViewBag.RequiresGuarantor = requiresGuarantor;
                ViewBag.TotalMemberGuarantee = totalMemberGuarantee;
                ViewBag.TotalCollateralGuarantee = totalCollateralGuarantee;
                ViewBag.TotalGuarantee = totalGuarantee;
                ViewBag.AmountToAppraise = amountToAppraise;
                ViewBag.AmountSource = amountSource;
                ViewBag.MemberGuarantors = memberGuarantors;
                ViewBag.CollateralGuarantees = collateralGuarantees;
                ViewBag.IsSelfGuarantee = isSelfGuarantee;
                ViewBag.IsApplicantGuarantor = isApplicantGuarantor;

                return View(appraisalDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading appraisal form for {loanNo}");
                TempData["ErrorMessage"] = $"Error loading appraisal: {ex.Message}";
                return RedirectToAction("AllLoans");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Appraise(LoanAppraisalDTO appraisalDto)
        {
            try
            {
                _logger.LogInformation($"=== APPRAISE POST CALLED ===");
                _logger.LogInformation($"LoanNo: {appraisalDto.LoanNo}");
                _logger.LogInformation($"AppraisalDecision: {appraisalDto.AppraisalDecision}");
                _logger.LogInformation($"RecommendedAmount: {appraisalDto.RecommendedAmount}");

                if (string.IsNullOrEmpty(appraisalDto.AppraisalDecision))
                {
                    TempData["ErrorMessage"] = "Please select an appraisal decision.";
                    return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
                }

                if (string.IsNullOrEmpty(appraisalDto.AppraisalNotes))
                {
                    TempData["ErrorMessage"] = "Please enter appraisal notes.";
                    return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
                }

                if (!ModelState.IsValid)
                {
                    var errors = string.Join(", ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                    _logger.LogWarning($"ModelState invalid: {errors}");

                    TempData["ErrorMessage"] = $"Validation error: {errors}";
                    return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
                }

                appraisalDto.CompanyCode = GetUserCompanyCode();
                appraisalDto.AppraisedBy = User.Identity?.Name ?? "SYSTEM";

                // VERIFY GUARANTEES STILL EXIST BEFORE APPRAISAL
                var memberGuarantees = await _context.Loanguar
                    .Where(g => g.LoanNo == appraisalDto.LoanNo && g.Transfered == false)
                    .SumAsync(g => g.Amount ?? 0);

                var collateralGuarantees = await _context.ColloanGuars
                    .Where(cg => cg.LoanNo == appraisalDto.LoanNo && cg.Balance > 0)
                    .SumAsync(cg => cg.Balance);

                var totalGuarantee = memberGuarantees + collateralGuarantees;

                var loan = await _loanService.GetLoanByNoForDisplayAsync(appraisalDto.LoanNo, appraisalDto.CompanyCode);
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, appraisalDto.CompanyCode);
                var requiresGuarantor = !string.IsNullOrEmpty(loanType.Guarantor) &&
                                        loanType.Guarantor != "No" &&
                                        loanType.Guarantor != "N";
                var isSelfGuarantee = loanType?.SelfGuarantee ?? false;
                var isApplicantGuarantor = await _context.Loanguar
                    .AnyAsync(g => g.LoanNo == appraisalDto.LoanNo && g.MemberNo == loan.MemberNo && g.Transfered == false);

                var loanAmount = loan.LoanAmt ?? 0;
                var maxAppraisalAmount = requiresGuarantor ? Math.Min(loanAmount, totalGuarantee) : loanAmount;

                _logger.LogInformation($"Pre-appraisal verification - Member: {memberGuarantees:C}, Collateral: {collateralGuarantees:C}, Total: {totalGuarantee:C}");
                _logger.LogInformation($"Self Guarantee: {isSelfGuarantee}, Applicant Guarantor: {isApplicantGuarantor}");
                _logger.LogInformation($"Max Appraisal Amount: {maxAppraisalAmount:C} (Loan: {loanAmount:C}, Guarantee: {totalGuarantee:C})");

                if (requiresGuarantor)
                {
                    if (totalGuarantee <= 0)
                    {
                        TempData["ErrorMessage"] = "This loan requires guarantees but no guarantees found. Cannot proceed with appraisal.";
                        return RedirectToAction("AssignGuarantor", new { loanNo = appraisalDto.LoanNo });
                    }

                    // ✅ FIX: Check if the recommended amount exceeds the guarantee
                    if (appraisalDto.RecommendedAmount > maxAppraisalAmount)
                    {
                        TempData["ErrorMessage"] = $"Recommended amount KES {appraisalDto.RecommendedAmount:N0} exceeds the maximum allowed based on guarantees KES {maxAppraisalAmount:N0}.";
                        return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
                    }
                }

                _logger.LogInformation($"Calling AppraiseLoanAsync for loan {appraisalDto.LoanNo}");

                var appraisal = await _loanService.AppraiseLoanAsync(appraisalDto);

                if (appraisal != null)
                {
                    _logger.LogInformation($"Appraisal completed successfully for loan {appraisalDto.LoanNo}");

                    if (appraisalDto.AppraisalDecision == "Recommend")
                    {
                        TempData["SuccessMessage"] = $"Loan appraisal completed successfully. Loan has been moved to Approved status for endorsement.";
                    }
                    else if (appraisalDto.AppraisalDecision == "NotRecommend")
                    {
                        TempData["SuccessMessage"] = $"Loan has been rejected and will be closed.";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = $"Loan appraisal completed with decision: {appraisalDto.AppraisalDecision}";
                    }

                    return RedirectToAction("AllLoans");
                }
                else
                {
                    TempData["ErrorMessage"] = "Appraisal returned null. Please check logs.";
                    return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting loan appraisal: {ex.Message}");
                TempData["ErrorMessage"] = $"Error submitting appraisal: {ex.Message}";
                return RedirectToAction("Appraise", new { loanNo = appraisalDto.LoanNo });
            }
        }


        [HttpGet]
        public async Task<IActionResult> Approve(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                if (loan.Status != (int)Status.Approved)
                {
                    ViewBag.ErrorMessage = "Loan is not ready for approval. Loan must be appraised first.";
                    return RedirectToAction("AllLoans");
                }

                var appraisal = await _loanService.GetLoanAppraisalAsync(loanNo);
                if (appraisal == null)
                {
                    ViewBag.ErrorMessage = "Loan must be appraised before approval";
                    return RedirectToAction("AllLoans");
                }

                var approvals = await _loanService.GetLoanApprovalsAsync(loanNo);

                ViewBag.Loan = loan;
                ViewBag.Appraisal = appraisal;
                ViewBag.PreviousApprovals = approvals;
                ViewBag.ApprovalLevel = approvals.Count + 1;

                var approvalDto = new LoanApprovalDTO
                {
                    LoanNo = loanNo,
                    CompanyCode = companyCode,
                    ApprovedBy = User.Identity?.Name ?? "SYSTEM",
                    ApprovalLevel = approvals.Count + 1,
                    IsFinalApproval = (approvals.Count + 1) >= GetRequiredApprovalLevels()
                };

                return View(approvalDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading approval form for {loanNo}");
                return View("Error");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(LoanApprovalDTO approvalDto)
        {
            try
            {
                approvalDto.CompanyCode = GetUserCompanyCode();
                approvalDto.ApprovedBy = User.Identity?.Name ?? "SYSTEM";

                var approval = await _loanService.ApproveLoanAsync(approvalDto);

                TempData["SuccessMessage"] = $"Loan {approvalDto.ApprovalStatus} successfully";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting loan approval");
                TempData["ErrorMessage"] = ex.Message;
                return View(approvalDto);
            }
        }

        #endregion


        #region Loan Endorsement

        [HttpGet]
        public async Task<IActionResult> PendingEndorsement()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var searchDto = new LoanSearchDTO
                {
                    CompanyCode = companyCode,
                    LoanStatus = ((int)Status.Approved).ToString()
                };

                var allLoans = await _loanService.SearchLoansAsync(searchDto);

                var pendingEndorsement = new List<dynamic>();
                foreach (var loan in allLoans)
                {
                    var hasEndorsement = await _loanService.HasEndorsementAsync(loan.LoanNo, companyCode);
                    if (!hasEndorsement)
                    {
                        pendingEndorsement.Add(new
                        {
                            LoanNo = loan.LoanNo,
                            MemberName = loan.MemberName,
                            LoanType = loan.LoanType,
                            ApprovedAmount = loan.PrincipalAmount,
                            ApplicationDate = loan.ApplicationDate
                        });
                    }
                }

                ViewBag.Count = pendingEndorsement.Count;
                return View(pendingEndorsement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Approved endorsement loans");
                TempData["ErrorMessage"] = "Error loading loans pending endorsement";
                return View(new List<dynamic>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Endorse(LoanEndorsementDTO endorsementDto)
        {
            try
            {
                _logger.LogInformation($"=== ENDORSE POST CALLED ===");
                _logger.LogInformation($"LoanNo: {endorsementDto.LoanNo}");

                // Log all deductions for debugging
                foreach (var deduction in endorsementDto.Deductions)
                {
                    _logger.LogInformation($"Deduction: {deduction.DeductionCode}, Amount: {deduction.Amount}, GL Account: {deduction.GlAccountNo}");
                }

                // Validate that all deductions with amount > 0 have GL accounts
                var invalidDeductions = endorsementDto.Deductions
                    .Where(d => d.Amount > 0 && string.IsNullOrEmpty(d.GlAccountNo))
                    .ToList();

                if (invalidDeductions.Any())
                {
                    var invalidNames = string.Join(", ", invalidDeductions.Select(d => d.DeductionName));
                    TempData["ErrorMessage"] = $"Please select income accounts for: {invalidNames}";
                    return RedirectToAction("Endorse", new { loanNo = endorsementDto.LoanNo });
                }

                endorsementDto.CompanyCode = GetUserCompanyCode();
                endorsementDto.EndorsedBy = User.Identity?.Name ?? "SYSTEM";

                var endorsement = await _loanService.CreateEndorsementAsync(endorsementDto);
                TempData["SuccessMessage"] = $"✅ Endorsement {endorsement.MinuteNo} completed successfully! The loan is now endorsed and waiting for Finance Officer to disburse the loan. Net amount: KES {endorsement.AmtApproved:N0}";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting loan endorsement");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Endorse", new { loanNo = endorsementDto.LoanNo });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Endorse(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loan = await _loanService.GetLoanByNoForDisplayAsync(loanNo, companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("PendingEndorsement");
                }

                if (loan.Status != (int)Status.Approved)
                {
                    TempData["ErrorMessage"] = $"Cannot endorse loan in status '{loan.Status}'. Loan must be Approved.";
                    return RedirectToAction("AllLoans");
                }

                var existingEndorsement = await _loanService.GetEndorsementByLoanNoAsync(loanNo, companyCode);
                if (existingEndorsement != null)
                {
                    TempData["ErrorMessage"] = "Endorsement already exists for this loan";
                    return RedirectToAction("AllLoans");
                }

                var availableDeductions = await _loanService.GetAvailableDeductionsAsync(companyCode);

                var allGlAccounts = await _context.GlSetup
                    .Where(g => g.CompanyCode == companyCode && g.Status == true)
                    .OrderBy(g => g.AccNo)
                    .Select(g => new
                    {
                        AccountNo = g.AccNo,
                        AccountName = g.Glaccname,
                        AccountType = g.Glacctype,
                        DisplayText = $"{g.AccNo} - {g.Glaccname} ({g.Glacctype ?? "General"})"
                    })
                    .ToListAsync();

                if (!allGlAccounts.Any())
                {
                    TempData["ErrorMessage"] = "No GL accounts found. Please set up GL accounts first.";
                    return RedirectToAction("PendingEndorsement");
                }

                string defaultSourceAccountNo = null;

                var defaultBank = await _context.Banks
                    .Where(b => b.CompanyCode == companyCode &&
                               b.IsActive == true &&
                               !string.IsNullOrEmpty(b.GlAccountNo))
                    .OrderBy(b => b.Id)
                    .FirstOrDefaultAsync();

                if (defaultBank != null)
                {
                    defaultSourceAccountNo = defaultBank.GlAccountNo;
                    _logger.LogInformation($"Auto-selected source bank: {defaultBank.BankName} with GL Account: {defaultBank.GlAccountNo}");
                }
                else
                {
                    var defaultGlAccount = await _context.GlSetup
                        .Where(g => g.CompanyCode == companyCode &&
                                   g.Status == true &&
                                   (g.Glacctype == "CASH" || g.Glacctype == "BANK" || g.GlAccMainGroup == "ASSET"))
                        .OrderBy(g => g.AccNo)
                        .FirstOrDefaultAsync();

                    if (defaultGlAccount != null)
                    {
                        defaultSourceAccountNo = defaultGlAccount.AccNo;
                        _logger.LogInformation($"Auto-selected source GL account: {defaultGlAccount.AccNo} - {defaultGlAccount.Glaccname}");
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No source account found. Please set up a bank with a GL account or a cash GL account.";
                        return RedirectToAction("PendingEndorsement");
                    }
                }

                var defaultDeductions = new List<LoanDeductionDTO>();
                foreach (var deduction in availableDeductions)
                {
                    defaultDeductions.Add(new LoanDeductionDTO
                    {
                        DeductionCode = deduction.DeductionCode,
                        DeductionName = deduction.DeductionName,
                        GlAccountNo = "",
                        GlAccountName = "",
                        Amount = 0,
                        Description = deduction.Description,
                        IsMandatory = false,
                        IsPercentage = false,
                        PercentageValue = null
                    });
                }

                var endorsementDto = new LoanEndorsementDTO
                {
                    LoanNo = loanNo,
                    CompanyCode = companyCode,
                    EndorsementDate = DateTime.Now,
                    EndorsedBy = User.Identity?.Name ?? "SYSTEM",
                    Deductions = defaultDeductions,
                    Remarks = "",
                    SourceAccountNo = defaultSourceAccountNo
                };

                ViewBag.Loan = loan;
                ViewBag.GrossAmount = loan.LoanAmt ?? 0;
                ViewBag.AllGlAccounts = allGlAccounts;

                return View(endorsementDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading endorsement form for {loanNo}");
                TempData["ErrorMessage"] = $"Error loading endorsement: {ex.Message}";
                return RedirectToAction("PendingEndorsement");
            }
        }


        #endregion


        #region Loan Disbursement

        [HttpGet]
        [FinanceOfficerOnly]
        public async Task<IActionResult> PendingDisbursement()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // Get loans with Endorsed status (5)
                var endorsedLoans = await _context.Loans
                    .Where(l => l.CompanyCode == companyCode && l.Status == (int)Status.Endorsed)
                    .OrderByDescending(l => l.AuditDateTime)
                    .ToListAsync();

                var pendingDisbursement = new List<dynamic>();

                foreach (var loan in endorsedLoans)
                {
                    // Check if already disbursed (has Loanbal record)
                    var existingDisbursement = await _context.Loanbal
                        .FirstOrDefaultAsync(lb => lb.LoanNo == loan.LoanNo && lb.Companycode == companyCode);

                    if (existingDisbursement != null)
                    {
                        continue; // Skip already disbursed loans
                    }

                    // Get endorsement record
                    var endorsement = await _context.Endmain
                        .FirstOrDefaultAsync(e => e.LoanNo == loan.LoanNo && e.CompanyCode == companyCode);

                    if (endorsement == null)
                    {
                        continue; // Skip if no endorsement found
                    }

                    // Get cheque record
                    var cheque = await _context.Cheques
                        .FirstOrDefaultAsync(c => c.LoanNo == loan.LoanNo && c.CompanyCode == companyCode);

                    // Get member details
                    var member = await _context.Members
                        .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                    // Get loan type
                    var loanType = await _context.Loantypes
                        .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

                    // Calculate values
                    decimal approvedAmount = endorsement.AmtApproved;
                    decimal netAmount = cheque?.AmountIssued ?? (cheque?.Amount ?? approvedAmount);
                    decimal totalDeductions = approvedAmount - netAmount;

                    // Get GL transactions for deductions
                    var glTransactions = await _context.Gltransactions
                        .Where(g => g.DocumentNo == cheque.Voucherno && g.Source == "LOAN_ENDORSEMENT")
                        .ToListAsync();

                    if (glTransactions.Any())
                    {
                        totalDeductions = glTransactions.Sum(g => g.Amount);
                        netAmount = approvedAmount - totalDeductions;
                    }

                    pendingDisbursement.Add(new
                    {
                        LoanNo = loan.LoanNo,
                        MemberName = member != null ? $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim() : loan.MemberNo,
                        LoanType = loanType?.LoanType1 ?? loan.LoanCode ?? "Unknown",
                        GrossAmount = approvedAmount,
                        TotalDeductions = totalDeductions,
                        NetAmount = netAmount,
                        ApplicationDate = loan.ApplicDate,
                        MemberMobile = member?.PhoneNo ?? member?.MobileNo ?? "N/A"
                    });
                }

                ViewBag.Count = pendingDisbursement.Count;
                return View(pendingDisbursement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pending disbursement loans");
                TempData["ErrorMessage"] = $"Error loading loans: {ex.Message}";
                return View(new List<dynamic>());
            }
        }

        [HttpGet]
        [FinanceOfficerOnly]
        public async Task<IActionResult> Disburse(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                if (string.IsNullOrEmpty(companyCode))
                {
                    _logger.LogError("CompanyCode is null or empty");
                    TempData["ErrorMessage"] = "Company code not found. Please log in again.";
                    return RedirectToAction("PendingDisbursement");
                }

                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanNo == loanNo && l.CompanyCode == companyCode);

                if (loan == null)
                {
                    TempData["ErrorMessage"] = "Loan not found";
                    return RedirectToAction("PendingDisbursement");
                }

                if (loan.Status != (int)Status.Endorsed)
                {
                    TempData["ErrorMessage"] = $"Loan cannot be disbursed in status '{loan.Status}'. Loan must be Endorsed.";
                    return RedirectToAction("AllLoans");
                }

                // Check if already disbursed
                var existingLoanbal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loanNo && lb.Companycode == companyCode);

                if (existingLoanbal != null)
                {
                    TempData["ErrorMessage"] = "This loan has already been disbursed";
                    return RedirectToAction("AllLoans");
                }

                // GET MEMBER DETAILS
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                // Get endorsement record
                var endorsement = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);

                // Get cheque record from endorsement (contains AmountIssued = net amount after deductions)
                var cheque = await _context.Cheques
                    .FirstOrDefaultAsync(c => c.LoanNo == loanNo && c.CompanyCode == companyCode);

                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(l => l.LoanCode == loan.LoanCode && l.CompanyCode == companyCode);

                // Get GL Accounts for dropdown
                var glAccounts = await _context.GlSetup
                    .Where(g => g.CompanyCode == companyCode && g.Status == true)
                    .OrderBy(g => g.AccNo)
                    .Select(g => new
                    {
                        AccountNo = g.AccNo,
                        AccountName = g.Glaccname,
                        DisplayText = $"{g.Glaccname}"
                    })
                    .ToListAsync();

                // Get Banks for dropdown
                var banks = await _context.Banks
                    .Where(b => b.CompanyCode == companyCode && b.IsActive == true)
                    .OrderBy(b => b.BankName)
                    .Select(b => new
                    {
                        BankId = b.Id,
                        BankCode = b.BankCode,
                        BankName = b.BankName,
                        AccountNumber = b.AccountNumber,
                        AccountName = b.AccountName,
                        Branch = b.Branch,
                        DisplayText = $"{b.BankName}"
                    })
                    .ToListAsync();

                // CORRECT: Net amount to disburse is AmountIssued from Cheque (after deductions)
                decimal netAmountToDisburse = cheque?.AmountIssued ?? endorsement?.AmtApproved ?? loan.LoanAmt ?? 0;

                // Approved amount from endorsement (before deductions)
                decimal approvedAmount = endorsement?.AmtApproved ?? loan.LoanAmt ?? 0;

                // Calculate total deductions
                decimal totalDeductions = approvedAmount - netAmountToDisburse;

                ViewBag.CashGlAccounts = glAccounts;
                ViewBag.Banks = banks;
                ViewBag.Loan = loan;
                ViewBag.Member = member;
                ViewBag.LoanType = loanType;
                ViewBag.Endorsement = endorsement;
                ViewBag.Cheque = cheque;
                ViewBag.ApprovedAmount = approvedAmount;
                ViewBag.NetAmount = netAmountToDisburse; 
                ViewBag.TotalDeductions = totalDeductions;

                var disbursementDto = new LoanDisbursementDTO
                {
                    LoanNo = loanNo,
                    CompanyCode = companyCode,
                    DisbursementDate = DateTime.Now,
                    DisbursedBy = User.Identity?.Name ?? "SYSTEM",
                    AuthorizedBy = User.Identity?.Name ?? "SYSTEM",
                    DisbursedAmount = netAmountToDisburse,
                    ProcessingFee = 0,
                    InsuranceFee = 0,
                    LegalFees = 0,
                    OtherFees = 0,
                    MobileNo = member?.PhoneNo ?? member?.MobileNo ?? ""
                };

                return View(disbursementDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading disbursement form for {loanNo}");
                TempData["ErrorMessage"] = $"Error loading disbursement: {ex.Message}";
                return RedirectToAction("PendingDisbursement");
            }
        }

        [HttpPost]
        [FinanceOfficerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Disburse(LoanDisbursementDTO disbursementDto)
        {
            try
            {
                _logger.LogInformation($"=== DISBURSE POST CALLED ===");
                _logger.LogInformation($"LoanNo: {disbursementDto.LoanNo}");
                _logger.LogInformation($"DisbursementMethod: {disbursementDto.DisbursementMethod}");
                _logger.LogInformation($"BankId: {disbursementDto.BankId}");

                if (string.IsNullOrEmpty(disbursementDto.DisbursementMethod))
                {
                    TempData["ErrorMessage"] = "Please select a disbursement method.";
                    return RedirectToAction("Disburse", new { loanNo = disbursementDto.LoanNo });
                }

                disbursementDto.CompanyCode = GetUserCompanyCode();
                disbursementDto.DisbursedBy = User.Identity?.Name ?? "SYSTEM";
                disbursementDto.AuthorizedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanService.DisburseLoanAsync(disbursementDto);

                TempData["SuccessMessage"] = $"Loan disbursed successfully. Net Amount: KES {result.Amount:N0}";
                return RedirectToAction("AllLoans");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, $"Database error disbursing loan: {ex.Message}");

                string errorMessage = "Error disbursing loan. ";
                if (ex.InnerException != null)
                {
                    if (ex.InnerException.Message.Contains("String or binary data would be truncated"))
                    {
                        errorMessage += "One or more fields exceed the maximum length allowed.";
                    }
                    else if (ex.InnerException.Message.Contains("FOREIGN KEY"))
                    {
                        errorMessage += "Referenced record does not exist.";
                    }
                    else
                    {
                        errorMessage += ex.InnerException.Message;
                    }
                }
                else
                {
                    errorMessage += ex.Message;
                }

                TempData["ErrorMessage"] = errorMessage;
                return RedirectToAction("Disburse", new { loanNo = disbursementDto.LoanNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error disbursing loan: {ex.Message}");
                TempData["ErrorMessage"] = $"Error disbursing loan: {ex.Message}";
                return RedirectToAction("Disburse", new { loanNo = disbursementDto.LoanNo });
            }
        }

        public class FinanceOfficerOnlyAttribute : AuthorizeAttribute
        {
            public FinanceOfficerOnlyAttribute()
            {
                Roles = "Finance Officer, Super Admin, Admin";
            }
        }

        #endregion

        #region Loan Repayments

        [HttpGet]
        public async Task<IActionResult> Repay(string loanNo = null)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                ViewBag.CompanyCode = companyCode;

                _logger.LogInformation($"Repay page loading - CompanyCode: {companyCode}");

                // Load active GL accounts from GLSETUP
                var glAccounts = await _context.GlSetup
                    .Where(g => g.CompanyCode == companyCode && g.Status == true)
                    .OrderBy(g => g.AccNo)
                    .Select(g => new
                    {
                        AccNo = g.AccNo,
                        Glaccname = g.Glaccname,
                        Glacctype = g.Glacctype ?? "General",
                        GlAccMainGroup = g.GlAccMainGroup,
                        DisplayText = $"{g.AccNo} - {g.Glaccname} ({g.Glacctype ?? "General"})"
                    })
                    .ToListAsync();

                _logger.LogInformation($"GL Accounts found: {glAccounts.Count}");
                ViewBag.GlAccounts = glAccounts;

                var repaymentDto = new LoanRepaymentDTO
                {
                    CompanyCode = companyCode,
                    PaymentDate = DateTime.Now,
                    ReceivedBy = User.Identity?.Name ?? "SYSTEM"
                };

                if (!string.IsNullOrEmpty(loanNo))
                {
                    var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                    if (loan != null && (loan.Status == (int)Status.Disbursed || loan.Status == (int)Status.Endorsed))
                    {
                        var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                        var totalOutstanding = schedule.Where(s => s.Status != "Paid").Sum(s => s.OutstandingAmount);
                        var nextInstallment = schedule.FirstOrDefault(s => s.Status == "Pending" || s.Status == "Overdue");

                        ViewBag.Loan = loan;
                        ViewBag.Schedule = schedule;
                        ViewBag.TotalOutstanding = totalOutstanding;
                        ViewBag.NextInstallment = nextInstallment;
                        ViewBag.PreSelectedLoanNo = loanNo;

                        repaymentDto.LoanNo = loanNo;
                        repaymentDto.MemberNo = loan.MemberNo;
                    }
                }

                return View(repaymentDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading repayment page");
                TempData["ErrorMessage"] = $"Error loading repayment page: {ex.Message}";
                return View(new LoanRepaymentDTO
                {
                    CompanyCode = GetUserCompanyCode(),
                    PaymentDate = DateTime.Now,
                    ReceivedBy = User.Identity?.Name ?? "SYSTEM"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveLoans(string memberNo = null, string loanNo = null, string companyCode = null)
        {
            try
            {
                if (string.IsNullOrEmpty(companyCode))
                {
                    companyCode = GetUserCompanyCode();
                }

                List<object> activeLoans = new List<object>();
                string memberName = null;
                string memberPhone = null;
                string memberEmail = null;
                string actualMemberNo = null;

                // CASE 1: Search by Loan Number
                if (!string.IsNullOrEmpty(loanNo))
                {
                    var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                    // ✅ FIX: Include more statuses for repayment
                    // Loans that can be repaid: Disbursed, Endorsed, Approved (after approval but not yet disbursed?),
                    // and also Submitted (if guarantors are assigned)
                    bool canRepay = loan != null && (
                        loan.Status == (int)Status.Disbursed ||
                        loan.Status == (int)Status.Endorsed ||
                        loan.Status == (int)Status.Approved ||  // Approved but not yet disbursed - might have advance payments
                        (loan.Status == (int)Status.Submitted && loan.Guaranteed != "0") // Submitted with guarantors
                    );

                    if (loan != null && canRepay)
                    {
                        actualMemberNo = loan.MemberNo;
                        var member = await _memberService.GetMemberByMemberNoAsync(loan.MemberNo);
                        if (member != null)
                        {
                            memberName = $"{member.Surname} {member.OtherNames}".Trim();
                            memberPhone = member.PhoneNo;
                            memberEmail = member.Email;
                        }

                        var loanTypeName = "Unknown";
                        var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                        if (loanType != null)
                        {
                            loanTypeName = loanType.LoanType ?? loanType.LoanCode ?? "Unknown";
                        }

                        // GET CURRENT SCHEDULE - Check if schedule exists (for disbursed loans)
                        var currentSchedule = await _context.LoanSchedules
                            .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                            .OrderBy(s => s.InstallmentNo)
                            .FirstOrDefaultAsync();

                        var loanbal = await _loanService.GetLoanBalanceAsync(loanNo);

                        // For loans not yet disbursed, use loan amount
                        decimal outstandingPrincipal = 0;
                        decimal outstandingInterest = 0;
                        decimal totalOutstanding = 0;
                        decimal nextInstallmentAmount = 0;
                        DateTime? dueDate = null;
                        int daysOverdue = 0;

                        if (currentSchedule != null)
                        {
                            // Disbursed loan with schedule
                            outstandingPrincipal = currentSchedule.OutstandingPrincipal;
                            outstandingInterest = currentSchedule.OutstandingInterest;
                            totalOutstanding = currentSchedule.OutstandingTotal + currentSchedule.PenaltyAmount;
                            nextInstallmentAmount = currentSchedule.TotalInstallment;
                            dueDate = currentSchedule.DueDate;
                            daysOverdue = currentSchedule.DaysOverdue;
                        }
                        else if (loanbal != null)
                        {
                            // Disbursed loan with loanbal but no schedule
                            outstandingPrincipal = loanbal.Balance;
                            outstandingInterest = loanbal.IntrOwed;
                            totalOutstanding = loanbal.Balance + loanbal.IntrOwed + loanbal.Penalty;
                            nextInstallmentAmount = loanbal.RepayRate;
                            dueDate = loanbal.Duedate;
                        }
                        else
                        {
                            // Loan not yet disbursed - use applied amount
                            outstandingPrincipal = loan.LoanAmt ?? 0;
                            outstandingInterest = 0;
                            totalOutstanding = outstandingPrincipal;
                            nextInstallmentAmount = 0;
                            dueDate = null;
                        }

                        activeLoans.Add(new
                        {
                            loanNo = loan.LoanNo,
                            loanType = loanTypeName,
                            repayMethod = loan.RepayMethod ?? loanType?.RepayMethod ?? "AMT",
                            interestRate = loan.Interest ?? 0,
                            outstandingPrincipal = outstandingPrincipal,
                            outstandingInterest = outstandingInterest,
                            outstandingPenalty = loanbal?.Penalty ?? 0,
                            totalOutstanding = totalOutstanding,
                            nextDueDate = dueDate,
                            nextInstallmentAmount = nextInstallmentAmount,
                            dueDate = dueDate,
                            daysSinceLastPayment = daysOverdue,
                            disbursementDate = loan.AuditDateTime ?? loan.ApplicDate,
                            installmentNo = currentSchedule?.InstallmentNo ?? 1,
                            totalPrincipalBalance = loanbal?.Balance ?? loan.LoanAmt ?? 0,
                            totalInterestBalance = loanbal?.IntrOwed ?? 0
                        });
                    }

                    return Json(new
                    {
                        success = true,
                        memberNo = actualMemberNo,
                        memberName = memberName ?? "N/A",
                        phone = memberPhone ?? "N/A",
                        email = memberEmail ?? "N/A",
                        loans = activeLoans
                    });
                }
                // CASE 2: Search by Member Number
                else if (!string.IsNullOrEmpty(memberNo))
                {
                    actualMemberNo = memberNo;
                    var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                    if (member != null)
                    {
                        memberName = $"{member.Surname} {member.OtherNames}".Trim();
                        memberPhone = member.PhoneNo;
                        memberEmail = member.Email;
                    }

                    // ✅ FIX: Get ALL loans for the member, then filter by status
                    var allLoans = await _context.Loans
                        .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode)
                        .ToListAsync();

                    // Filter loans that can be repaid (Disbursed, Endorsed, Approved, or Submitted with guarantors)
                    var activeLoansList = allLoans
                        .Where(l => l.Status == (int)Status.Disbursed ||
                                   l.Status == (int)Status.Endorsed ||
                                   l.Status == (int)Status.Approved ||
                                   (l.Status == (int)Status.Submitted && l.Guaranteed != "0"))
                        .ToList();

                    foreach (var loan in activeLoansList)
                    {
                        var loanTypeName = "Unknown";
                        var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);
                        if (loanType != null)
                        {
                            loanTypeName = loanType.LoanType ?? loanType.LoanCode ?? "Unknown";
                        }

                        // GET CURRENT SCHEDULE
                        var currentSchedule = await _context.LoanSchedules
                            .Where(s => s.LoanNo == loan.LoanNo && s.Status != "Paid")
                            .OrderBy(s => s.InstallmentNo)
                            .FirstOrDefaultAsync();

                        var loanbal = await _loanService.GetLoanBalanceAsync(loan.LoanNo);

                        decimal outstandingPrincipal = 0;
                        decimal outstandingInterest = 0;
                        decimal totalOutstanding = 0;
                        decimal nextInstallmentAmount = 0;
                        DateTime? dueDate = null;
                        int daysOverdue = 0;

                        if (currentSchedule != null)
                        {
                            outstandingPrincipal = currentSchedule.OutstandingPrincipal;
                            outstandingInterest = currentSchedule.OutstandingInterest;
                            totalOutstanding = currentSchedule.OutstandingTotal + currentSchedule.PenaltyAmount;
                            nextInstallmentAmount = currentSchedule.TotalInstallment;
                            dueDate = currentSchedule.DueDate;
                            daysOverdue = currentSchedule.DaysOverdue;
                        }
                        else if (loanbal != null)
                        {
                            outstandingPrincipal = loanbal.Balance;
                            outstandingInterest = loanbal.IntrOwed;
                            totalOutstanding = loanbal.Balance + loanbal.IntrOwed + loanbal.Penalty;
                            nextInstallmentAmount = loanbal.RepayRate;
                            dueDate = loanbal.Duedate;
                        }
                        else
                        {
                            outstandingPrincipal = loan.LoanAmt ?? 0;
                            outstandingInterest = 0;
                            totalOutstanding = outstandingPrincipal;
                        }

                        activeLoans.Add(new
                        {
                            loanNo = loan.LoanNo,
                            loanType = loanTypeName,
                            repayMethod = loan.RepayMethod ?? loanType?.RepayMethod ?? "AMT",
                            interestRate = loan.Interest ?? 0,
                            outstandingPrincipal = outstandingPrincipal,
                            outstandingInterest = outstandingInterest,
                            outstandingPenalty = loanbal?.Penalty ?? 0,
                            totalOutstanding = totalOutstanding,
                            nextDueDate = dueDate,
                            nextInstallmentAmount = nextInstallmentAmount,
                            dueDate = dueDate,
                            daysSinceLastPayment = daysOverdue,
                            disbursementDate = loan.AuditDateTime ?? loan.ApplicDate,
                            installmentNo = currentSchedule?.InstallmentNo ?? 1,
                            totalPrincipalBalance = loanbal?.Balance ?? loan.LoanAmt ?? 0,
                            totalInterestBalance = loanbal?.IntrOwed ?? 0
                        });
                    }

                    return Json(new
                    {
                        success = true,
                        memberNo = actualMemberNo,
                        memberName = memberName ?? "N/A",
                        phone = memberPhone ?? "N/A",
                        email = memberEmail ?? "N/A",
                        loans = activeLoans
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Please provide either a loan number or member number to search",
                        loans = new List<object>()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active loans");
                return Json(new { success = false, message = ex.Message, loans = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CalculateRepayment(string loanNo, decimal amount, DateTime paymentDate)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                if (loan == null)
                {
                    return Json(new { success = false, message = "Loan not found" });
                }

                // GET CURRENT SCHEDULE - first unpaid installment
                var currentSchedule = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                    .OrderBy(s => s.InstallmentNo)
                    .FirstOrDefaultAsync();

                if (currentSchedule == null)
                {
                    return Json(new { success = false, message = "No active installment found for this loan" });
                }

                // Get overall loan balance
                var loanbal = await _loanService.GetLoanBalanceAsync(loanNo);
                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);

                int gracePeriodDays = loanType?.GracePeriod ?? 30;

                // Calculate penalty for current installment if overdue
                decimal penaltyAmount = 0;
                int daysOverdue = 0;

                if (paymentDate > currentSchedule.DueDate)
                {
                    daysOverdue = (paymentDate - currentSchedule.DueDate).Days;

                    if (daysOverdue > gracePeriodDays && loanType != null && Convert.ToInt32(loanType.Penalty) == 1)
                    {
                        int overdueMonths = (int)Math.Ceiling((daysOverdue - gracePeriodDays) / 30.0);
                        decimal monthlyPenaltyRate = 2m; // Fixed 2% per month
                        monthlyPenaltyRate = monthlyPenaltyRate / 100;
                        penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
                    }
                }

                // Calculate TOTAL remaining balance (principal + interest + penalty)
                decimal totalRemainingPrincipal = loanbal?.Balance ?? 0;
                decimal totalRemainingInterest = loanbal?.IntrOwed ?? 0;
                decimal totalRemainingPenalty = (loanbal?.Penalty ?? 0) + penaltyAmount;
                decimal totalFullBalance = totalRemainingPrincipal + totalRemainingInterest + totalRemainingPenalty;

                // Determine if payment is for current installment or full balance
                bool isFullBalancePayment = amount >= totalFullBalance - 0.01m;

                decimal penaltyAllocated = 0;
                decimal interestAllocated = 0;
                decimal principalAllocated = 0;
                decimal overpayment = 0;
                decimal balanceAfter = 0;

                if (isFullBalancePayment)
                {
                    // Full balance payment - pay off everything
                    penaltyAllocated = totalRemainingPenalty;
                    interestAllocated = totalRemainingInterest;
                    principalAllocated = totalRemainingPrincipal;
                    overpayment = amount - totalFullBalance;
                    balanceAfter = 0;

                    _logger.LogInformation($"Full balance payment: Amount={amount:C}, Total Due={totalFullBalance:C}");
                }
                else
                {
                    // Regular payment - apply to current installment first
                    decimal remainingAmount = amount;

                    // Apply to penalty
                    if (remainingAmount > 0 && penaltyAmount > 0)
                    {
                        penaltyAllocated = Math.Min(remainingAmount, penaltyAmount);
                        remainingAmount -= penaltyAllocated;
                    }

                    // Apply to interest (current installment)
                    if (remainingAmount > 0 && currentSchedule.OutstandingInterest > 0)
                    {
                        interestAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingInterest);
                        remainingAmount -= interestAllocated;
                    }

                    // Apply to principal (current installment)
                    if (remainingAmount > 0 && currentSchedule.OutstandingPrincipal > 0)
                    {
                        principalAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingPrincipal);
                        remainingAmount -= principalAllocated;
                    }

                    overpayment = remainingAmount;

                    // Calculate remaining balance after payment
                    decimal remainingPrincipalAfter = totalRemainingPrincipal - principalAllocated;
                    decimal remainingInterestAfter = totalRemainingInterest - interestAllocated;
                    decimal remainingPenaltyAfter = totalRemainingPenalty - penaltyAllocated;
                    balanceAfter = remainingPrincipalAfter + remainingInterestAfter + remainingPenaltyAfter;
                }

                _logger.LogInformation($"Repayment Calculation: Installment {currentSchedule.InstallmentNo}, " +
                    $"Amount={amount:C}, FullBalance={totalFullBalance:C}, IsFullPayment={isFullBalancePayment}");

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        penaltyAllocated = Math.Round(penaltyAllocated, 2),
                        interestAllocated = Math.Round(interestAllocated, 2),
                        principalAllocated = Math.Round(principalAllocated, 2),
                        overpayment = Math.Round(overpayment, 2),
                        balanceAfter = Math.Round(balanceAfter, 2),
                        penaltyAmount = Math.Round(penaltyAmount, 2),
                        daysOverdue,
                        installmentNo = currentSchedule.InstallmentNo,
                        dueDate = currentSchedule.DueDate,
                        currentInstallmentDue = currentSchedule.OutstandingTotal + penaltyAmount,
                        totalFullBalance = Math.Round(totalFullBalance, 2),
                        isFullBalancePayment
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating repayment");
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Repay(LoanRepaymentDTO repaymentDto)
        {
            try
            {
                _logger.LogInformation($"=== REPAY POST CALLED ===");
                _logger.LogInformation($"LoanNo: {repaymentDto.LoanNo}");
                _logger.LogInformation($"Amount: {repaymentDto.AmountPaid:C}");
                _logger.LogInformation($"PaymentMethod: {repaymentDto.PaymentMethod}");

                if (!ModelState.IsValid)
                {
                    var errors = string.Join(", ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                    _logger.LogWarning($"ModelState invalid: {errors}");

                    TempData["ErrorMessage"] = $"Validation error: {errors}";
                    return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
                }

                if (string.IsNullOrEmpty(repaymentDto.LoanNo))
                {
                    TempData["ErrorMessage"] = "Please select a loan to repay";
                    return RedirectToAction("Repay");
                }

                if (string.IsNullOrEmpty(repaymentDto.PaymentMethod))
                {
                    TempData["ErrorMessage"] = "Please select a payment method";
                    return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
                }

                if (string.IsNullOrEmpty(repaymentDto.GlAccountNo))
                {
                    TempData["ErrorMessage"] = "Please select a GL Account";
                    return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
                }

                if (repaymentDto.AmountPaid <= 0)
                {
                    TempData["ErrorMessage"] = "Please enter a valid payment amount greater than zero";
                    return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
                }

                repaymentDto.CompanyCode = GetUserCompanyCode();
                repaymentDto.ReceivedBy = User.Identity?.Name ?? "SYSTEM";

                var repayment = await _loanService.ProcessRepaymentAsync(repaymentDto);

                TempData["SuccessMessage"] = $"Repayment of KES {repaymentDto.AmountPaid:N0} processed successfully. Receipt: {repayment.ReceiptNo}";
                return RedirectToAction("AllLoans");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, $"Database error processing repayment: {ex.Message}");

                string errorMessage = "Error processing repayment. ";
                if (ex.InnerException != null)
                {
                    if (ex.InnerException.Message.Contains("String or binary data would be truncated"))
                    {
                        errorMessage += "One or more fields exceed the maximum length allowed.";
                    }
                    else if (ex.InnerException.Message.Contains("FOREIGN KEY"))
                    {
                        errorMessage += "Referenced record does not exist.";
                    }
                    else
                    {
                        errorMessage += ex.InnerException.Message;
                    }
                }
                else
                {
                    errorMessage += ex.Message;
                }

                TempData["ErrorMessage"] = errorMessage;
                return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing repayment: {ex.Message}");
                TempData["ErrorMessage"] = $"Error processing repayment: {ex.Message}";
                return RedirectToAction("Repay", new { loanNo = repaymentDto.LoanNo });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReverseRepayment(int repaymentId, string reason, string loanNo)
        {
            try
            {
                _logger.LogInformation($"=== REVERSE REPAYMENT CALLED ===");
                _logger.LogInformation($"RepaymentId: {repaymentId}, Reason: {reason}");

                if (string.IsNullOrEmpty(reason))
                {
                    TempData["ErrorMessage"] = "Please provide a reason for reversing the repayment";
                    return RedirectToAction("Details", new { loanNo });
                }

                var repayment = await _loanService.ReverseRepaymentAsync(
                    repaymentId,
                    reason,
                    User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = $"Repayment {repayment.ReceiptNo} reversed successfully";
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reversing repayment: {ex.Message}");
                TempData["ErrorMessage"] = $"Error reversing repayment: {ex.Message}";
                return RedirectToAction("Details", new { loanNo });
            }
        }

        #endregion


        #region Loan Status Management

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLoanStatus(string loanNo, string newStatus)
        {
            try
            {

                var companyCode = GetUserCompanyCode();

                // Verify the loan exists and user has access
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                // Update the status
                await _loanService.UpdateLoanStatusAsync(
                    loanNo,
                    newStatus,
                    User.Identity?.Name ?? "SYSTEM",
                    $"Status updated to {newStatus}");

                ViewBag.SuccessMessage = $"Loan status updated to {newStatus} successfully";
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating loan status for {loanNo}");
                ViewBag.ErrorMessage = ex.Message;
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectLoan(string loanNo, string rejectionReason)
        {
            try
            {

                var companyCode = GetUserCompanyCode();

                // Verify the loan exists and user has access
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                // Update the status to Rejected with reason
                await _loanService.UpdateLoanStatusAsync(
                    loanNo,
                    "Rejected",
                    User.Identity?.Name ?? "SYSTEM",
                    $"Loan rejected: {rejectionReason}");

                ViewBag.SuccessMessage = "Loan rejected successfully";
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rejecting loan {loanNo}");
                ViewBag.ErrorMessage = ex.Message;
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WriteOffLoan(string loanNo, string writeOffReason)
        {
            try
            {

                var companyCode = GetUserCompanyCode();
                var isAdmin = IsAdminUser();

                // Only admins can write off loans
                if (!isAdmin)
                {
                    ViewBag.ErrorMessage = "You don't have permission to write off loans";
                    //return RedirectToAction("Details", new { loanNo });
                    return RedirectToAction("AllLoans");
                }

                // Verify the loan exists
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);

                // Update the status to WrittenOff
                await _loanService.UpdateLoanStatusAsync(
                    loanNo,
                    "WrittenOff",
                    User.Identity?.Name ?? "SYSTEM",
                    $"Loan written off: {writeOffReason}");

                ViewBag.SuccessMessage = "Loan written off successfully";
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error writing off loan {loanNo}");
                ViewBag.ErrorMessage = ex.Message;
                //return RedirectToAction("Details", new { loanNo });
                return RedirectToAction("AllLoans");
            }
        }

        #endregion

        #region Loan Search

        [HttpGet]
        public IActionResult Search()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> SearchResults(LoanSearchDTO searchDto)
        {
            try
            {
                searchDto.CompanyCode = GetUserCompanyCode();

                var results = await _loanService.SearchLoansAsync(searchDto);

                ViewBag.SearchCriteria = searchDto;
                ViewBag.ResultCount = results.Count;


                return View(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching loans");
                return View("Error");
            }
        }

        #endregion

        #region Member Loans

        [HttpGet]
        public async Task<IActionResult> MemberLoans(string memberNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                var loans = await _loanService.GetMemberLoansAsync(memberNo, companyCode);

                ViewBag.Member = member;
                ViewBag.LoanCount = loans.Count;
                ViewBag.TotalLoanAmount = loans.Sum(l => l.PrincipalAmount);
                ViewBag.TotalOutstanding = loans.Sum(l => l.OutstandingBalance);


                return View(loans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading loans for member {memberNo}");
                return View("Error");
            }
        }

        #endregion

        #region Loan Schedule

        [HttpGet]
        public async Task<IActionResult> Schedule(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var loanbal = await _loanService.GetLoanBalanceAsync(loanNo);

                // Get member details for name
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo && m.CompanyCode == companyCode);

                // Get the approved amount from Endmain for display
                var endmain = await _context.Endmain
                    .FirstOrDefaultAsync(e => e.LoanNo == loanNo && e.CompanyCode == companyCode);

                var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                var repayments = await _loanService.GetLoanRepaymentsAsync(loanNo);

                decimal totalPaid = repayments.Sum(r => r.Amount ?? 0);
                decimal totalPrincipalPaid = repayments.Sum(r => r.Principal ?? 0);
                decimal totalInterestPaid = repayments.Sum(r => r.Interest ?? 0);
                decimal totalPenaltyPaid = repayments.Sum(r => r.Penalty ?? 0);

                // Use approved amount from Endmain, fallback to loan.LoanAmt
                decimal approvedAmount = endmain?.AmtApproved ?? loan?.LoanAmt ?? 0;

                // Calculate totals from schedule correctly
                decimal totalPrincipalFromSchedule = schedule.Sum(s => s.PrincipalAmount);
                decimal totalInterestFromSchedule = schedule.Sum(s => s.InterestAmount);
                decimal totalRepayableFromSchedule = schedule.Sum(s => s.TotalInstallment);

                // Outstanding calculations
                decimal totalPrincipalOutstanding = approvedAmount - totalPrincipalPaid;
                decimal totalInterestOutstanding = totalInterestFromSchedule - totalInterestPaid;
                decimal totalPenaltyOutstanding = (loanbal?.Penalty ?? 0) - totalPenaltyPaid;
                decimal totalOutstanding = totalPrincipalOutstanding + totalInterestOutstanding + totalPenaltyOutstanding;

                // Ensure no negative values
                totalPrincipalOutstanding = Math.Max(0, totalPrincipalOutstanding);
                totalInterestOutstanding = Math.Max(0, totalInterestOutstanding);
                totalPenaltyOutstanding = Math.Max(0, totalPenaltyOutstanding);
                totalOutstanding = Math.Max(0, totalOutstanding);

                ViewBag.Loan = loan;
                ViewBag.Member = member;
                ViewBag.LoanBalance = loanbal;
                ViewBag.Endmain = endmain;
                ViewBag.ApprovedAmount = approvedAmount;
                ViewBag.Repayments = repayments;
                ViewBag.TotalPrincipal = approvedAmount;
                ViewBag.TotalInterest = totalInterestFromSchedule;
                ViewBag.TotalRepayable = totalRepayableFromSchedule;
                ViewBag.TotalPaid = totalPaid;
                ViewBag.TotalPrincipalPaid = totalPrincipalPaid;
                ViewBag.TotalInterestPaid = totalInterestPaid;
                ViewBag.TotalPenaltyPaid = totalPenaltyPaid;
                ViewBag.TotalOutstanding = totalOutstanding;
                ViewBag.RepaymentMethod = loan?.RepayMethod ?? "AMT";

                return View(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading schedule for {loanNo}");
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportSchedule(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                var repayments = await _loanService.GetLoanRepaymentsAsync(loanNo);

                var csv = new StringBuilder();
                csv.AppendLine("Installment,Due Date,Principal,Interest,Total,Paid,Outstanding,Penalty,Status,Paid Date");

                foreach (var inst in schedule)
                {
                    csv.AppendLine($"\"{inst.InstallmentNo}\",\"{inst.DueDate:dd/MM/yyyy}\",{inst.PrincipalAmount:N2},{inst.InterestAmount:N2},{inst.TotalInstallment:N2},{inst.PaidAmount:N2},{inst.OutstandingAmount:N2},{inst.PenaltyAmount:N2},\"{inst.Status}\",\"{inst.PaidDate?.ToString("dd/MM/yyyy") ?? ""}\"");
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", $"Schedule_{loanNo}_{DateTime.Now:yyyyMMdd}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error exporting schedule for {loanNo}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region Loan Offset with Shares

        [HttpGet]
        public async Task<IActionResult> OffsetLoan()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                ViewBag.CompanyCode = companyCode;

                // Load GL accounts for dropdown (optional, for display)
                var glAccounts = await _context.GlSetup
                    .Where(g => g.CompanyCode == companyCode && g.Status == true)
                    .Select(g => new
                    {
                        AccNo = g.AccNo,
                        Glaccname = g.Glaccname,
                        DisplayText = $"{g.AccNo} - {g.Glaccname}"
                    })
                    .ToListAsync();

                ViewBag.GlAccounts = glAccounts;

                var offsetDto = new LoanOffsetDTO
                {
                    CompanyCode = companyCode,
                    ProcessedBy = User.Identity?.Name ?? "SYSTEM"
                };

                return View(offsetDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading loan offset page");
                TempData["ErrorMessage"] = $"Error loading loan offset page: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableShares(string memberNo, string companyCode)
        {
            try
            {
                if (string.IsNullOrEmpty(companyCode))
                {
                    companyCode = GetUserCompanyCode();
                }

                var availableShares = await _loanService.GetAvailableSharesForOffsetAsync(memberNo, companyCode);

                return Json(new
                {
                    success = true,
                    shares = availableShares
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting available shares for member {memberNo}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CalculateOffset(string loanNo, decimal amount, string sharesCode, string companyCode)
        {
            try
            {
                if (string.IsNullOrEmpty(companyCode))
                {
                    companyCode = GetUserCompanyCode();
                }

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var loanbal = await _loanService.GetLoanBalanceAsync(loanNo);

                if (loan == null)
                {
                    return Json(new { success = false, message = "Loan not found" });
                }

                // GET CURRENT SCHEDULE - first unpaid installment
                var currentSchedule = await _context.LoanSchedules
                    .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
                    .OrderBy(s => s.InstallmentNo)
                    .FirstOrDefaultAsync();

                if (currentSchedule == null)
                {
                    return Json(new { success = false, message = "No active installment found for this loan" });
                }

                var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);

                // ✅ CALCULATE FULL LOAN BALANCE (Total remaining)
                // This is the TOTAL amount needed to close the loan completely
                decimal totalRemainingPrincipal = loanbal?.Balance ?? 0;
                decimal totalRemainingInterest = loanbal?.IntrOwed ?? 0;
                decimal totalRemainingPenalty = loanbal?.Penalty ?? 0;
                decimal fullLoanBalance = totalRemainingPrincipal + totalRemainingInterest + totalRemainingPenalty;

                // ✅ Calculate current monthly installment due (for display only)
                decimal monthlyDue = currentSchedule.OutstandingTotal;

                // ✅ Calculate penalty for current installment if overdue
                decimal penaltyAmount = 0;
                int daysOverdue = 0;

                if (loanType != null && loanType.Penalty == true && DateTime.Now > currentSchedule.DueDate)
                {
                    daysOverdue = (DateTime.Now - currentSchedule.DueDate).Days;
                    int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;

                    if (daysOverdue > gracePeriodDays)
                    {
                        int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
                        int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);

                        // ✅ USE THE ACTUAL PENALTYVALUE FROM LOANTYPE (NOT HARDCODED)
                        decimal penaltyRatePercent = 0;
                        if (loanType.PenaltyValue != null)
                        {
                            decimal.TryParse(loanType.PenaltyValue.ToString(), out penaltyRatePercent);
                        }
                        decimal monthlyPenaltyRate = penaltyRatePercent / 100;

                        if (monthlyPenaltyRate > 0)
                        {
                            penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
                            _logger.LogInformation($"Penalty calculated: {penaltyAmount:C} for {daysOverdue} days overdue " +
                                $"(Grace: {gracePeriodDays} days, Rate: {monthlyPenaltyRate:P}, Months: {overdueMonths})");
                        }
                    }
                }

                // ✅ Determine if payment is for monthly due or full balance
                bool isFullBalancePayment = amount >= fullLoanBalance - 0.01m;
                bool isMonthlyDuePayment = amount >= monthlyDue - 0.01m && amount < fullLoanBalance;

                decimal penaltyAllocated = 0;
                decimal interestAllocated = 0;
                decimal principalAllocated = 0;
                decimal overpayment = 0;
                decimal balanceAfter = 0;

                if (isFullBalancePayment)
                {
                    // FULL BALANCE PAYMENT - Pay off everything
                    penaltyAllocated = totalRemainingPenalty;
                    interestAllocated = totalRemainingInterest;
                    principalAllocated = totalRemainingPrincipal;
                    overpayment = amount - fullLoanBalance;
                    balanceAfter = 0;

                    _logger.LogInformation($"Full balance offset: Amount={amount:C}, Full Balance={fullLoanBalance:C}");
                }
                else
                {
                    // Apply to current installment first (Penalty -> Interest -> Principal)
                    decimal remainingAmount = amount;

                    // Apply to penalty
                    if (remainingAmount > 0 && penaltyAmount > 0)
                    {
                        penaltyAllocated = Math.Min(remainingAmount, penaltyAmount);
                        remainingAmount -= penaltyAllocated;
                    }

                    // Apply to interest (current schedule)
                    if (remainingAmount > 0 && currentSchedule.OutstandingInterest > 0)
                    {
                        interestAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingInterest);
                        remainingAmount -= interestAllocated;
                    }

                    // Apply to principal (current schedule)
                    if (remainingAmount > 0 && currentSchedule.OutstandingPrincipal > 0)
                    {
                        principalAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingPrincipal);
                        remainingAmount -= principalAllocated;
                    }

                    overpayment = remainingAmount;

                    // Calculate remaining balance after payment
                    decimal remainingPrincipalAfter = totalRemainingPrincipal - principalAllocated;
                    decimal remainingInterestAfter = totalRemainingInterest - interestAllocated;
                    decimal remainingPenaltyAfter = totalRemainingPenalty - penaltyAllocated;
                    balanceAfter = remainingPrincipalAfter + remainingInterestAfter + remainingPenaltyAfter;
                }

                _logger.LogInformation($"Offset Calculation: Monthly Due={monthlyDue:C}, Full Balance={fullLoanBalance:C}, Amount={amount:C}");

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        penaltyAllocated = Math.Round(penaltyAllocated, 2),
                        interestAllocated = Math.Round(interestAllocated, 2),
                        principalAllocated = Math.Round(principalAllocated, 2),
                        overpayment = Math.Round(overpayment, 2),
                        balanceAfter = Math.Round(balanceAfter, 2),
                        penaltyAmount = Math.Round(penaltyAmount, 2),
                        daysOverdue,
                        installmentNo = currentSchedule.InstallmentNo,
                        dueDate = currentSchedule.DueDate,
                        monthlyDue = Math.Round(monthlyDue, 2),
                        fullLoanBalance = Math.Round(fullLoanBalance, 2),
                        isFullBalancePayment,
                        isMonthlyDuePayment
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating offset");
                return Json(new { success = false, message = ex.Message });
            }
        }

        //[HttpGet]
        //public async Task<IActionResult> CalculateOffset(string loanNo, decimal amount, string sharesCode, string companyCode)
        //{
        //    try
        //    {
        //        if (string.IsNullOrEmpty(companyCode))
        //        {
        //            companyCode = GetUserCompanyCode();
        //        }

        //        var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
        //        var loanbal = await _loanService.GetLoanBalanceAsync(loanNo);

        //        if (loan == null)
        //        {
        //            return Json(new { success = false, message = "Loan not found" });
        //        }

        //        // GET CURRENT SCHEDULE - first unpaid installment
        //        var currentSchedule = await _context.LoanSchedules
        //            .Where(s => s.LoanNo == loanNo && s.Status != "Paid")
        //            .OrderBy(s => s.InstallmentNo)
        //            .FirstOrDefaultAsync();

        //        if (currentSchedule == null)
        //        {
        //            return Json(new { success = false, message = "No active installment found for this loan" });
        //        }

        //        var loanType = await _loanTypeService.GetLoanTypeByCodeAsync(loan.LoanCode, companyCode);

        //        // 7. CALCULATE PENALTY - USING ACTUAL LOANTYPE VALUES
        //        decimal penaltyAmount = 0;
        //        int daysOverdue = 0;

        //        if (loanType != null && loanType.Penalty == true && DateTime.Now > currentSchedule.DueDate)
        //        {
        //            daysOverdue = (DateTime.Now - currentSchedule.DueDate).Days;
        //            int gracePeriodDays = loanType.GracePeriod > 0 ? loanType.GracePeriod : 0;

        //            if (daysOverdue > gracePeriodDays)
        //            {
        //                int overdueDaysAfterGrace = daysOverdue - gracePeriodDays;
        //                int overdueMonths = (int)Math.Ceiling(overdueDaysAfterGrace / 30.0);

        //                // ✅ USE THE ACTUAL PENALTYVALUE FROM LOANTYPE (NOT HARDCODED)
        //                decimal penaltyRatePercent = 0;
        //                if (loanType.PenaltyValue != null)
        //                {
        //                    decimal.TryParse(loanType.PenaltyValue.ToString(), out penaltyRatePercent);
        //                }
        //                decimal monthlyPenaltyRate = penaltyRatePercent / 100;

        //                if (monthlyPenaltyRate > 0)
        //                {
        //                    penaltyAmount = currentSchedule.OutstandingTotal * monthlyPenaltyRate * overdueMonths;
        //                    _logger.LogInformation($"Penalty calculated: {penaltyAmount:C} for {daysOverdue} days overdue " +
        //                        $"(Grace: {gracePeriodDays} days, Rate: {monthlyPenaltyRate:P}, Months: {overdueMonths})");
        //                }
        //            }
        //        }

        //        // Calculate TOTAL remaining balance (principal + interest + penalty)
        //        decimal totalRemainingPrincipal = currentSchedule.OutstandingPrincipal;
        //        decimal totalRemainingInterest = currentSchedule.OutstandingInterest;
        //        decimal totalRemainingPenalty = (loanbal?.Penalty ?? 0) + penaltyAmount;
        //        decimal totalFullBalance = totalRemainingPrincipal + totalRemainingInterest + totalRemainingPenalty;

        //        // Determine if payment is for current installment or full balance
        //        bool isFullBalancePayment = amount >= totalFullBalance - 0.01m;

        //        decimal penaltyAllocated = 0;
        //        decimal interestAllocated = 0;
        //        decimal principalAllocated = 0;
        //        decimal overpayment = 0;
        //        decimal balanceAfter = 0;

        //        if (isFullBalancePayment)
        //        {
        //            // Full balance payment - pay off everything
        //            penaltyAllocated = totalRemainingPenalty;
        //            interestAllocated = totalRemainingInterest;
        //            principalAllocated = totalRemainingPrincipal;
        //            overpayment = amount - totalFullBalance;
        //            balanceAfter = 0;

        //            _logger.LogInformation($"Full balance offset: Amount={amount:C}, Total Due={totalFullBalance:C}");
        //        }
        //        else
        //        {
        //            // Regular payment - apply to current installment first
        //            decimal remainingAmount = amount;

        //            // Apply to penalty
        //            if (remainingAmount > 0 && penaltyAmount > 0)
        //            {
        //                penaltyAllocated = Math.Min(remainingAmount, penaltyAmount);
        //                remainingAmount -= penaltyAllocated;
        //            }

        //            // Apply to interest (current installment)
        //            if (remainingAmount > 0 && currentSchedule.OutstandingInterest > 0)
        //            {
        //                interestAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingInterest);
        //                remainingAmount -= interestAllocated;
        //            }

        //            // Apply to principal (current installment)
        //            if (remainingAmount > 0 && currentSchedule.OutstandingPrincipal > 0)
        //            {
        //                principalAllocated = Math.Min(remainingAmount, currentSchedule.OutstandingPrincipal);
        //                remainingAmount -= principalAllocated;
        //            }

        //            overpayment = remainingAmount;

        //            // Calculate remaining balance after payment
        //            decimal remainingPrincipalAfter = totalRemainingPrincipal - principalAllocated;
        //            decimal remainingInterestAfter = totalRemainingInterest - interestAllocated;
        //            decimal remainingPenaltyAfter = totalRemainingPenalty - penaltyAllocated;
        //            balanceAfter = remainingPrincipalAfter + remainingInterestAfter + remainingPenaltyAfter;
        //        }

        //        _logger.LogInformation($"Offset Calculation: Installment {currentSchedule.InstallmentNo}, " +
        //            $"Amount={amount:C}, FullBalance={totalFullBalance:C}, IsFullPayment={isFullBalancePayment}");

        //        return Json(new
        //        {
        //            success = true,
        //            data = new
        //            {
        //                penaltyAllocated = Math.Round(penaltyAllocated, 2),
        //                interestAllocated = Math.Round(interestAllocated, 2),
        //                principalAllocated = Math.Round(principalAllocated, 2),
        //                overpayment = Math.Round(overpayment, 2),
        //                balanceAfter = Math.Round(balanceAfter, 2),
        //                penaltyAmount = Math.Round(penaltyAmount, 2),
        //                daysOverdue,
        //                installmentNo = currentSchedule.InstallmentNo,
        //                dueDate = currentSchedule.DueDate,
        //                currentInstallmentDue = currentSchedule.OutstandingTotal + penaltyAmount,
        //                totalFullBalance = Math.Round(totalFullBalance, 2),
        //                isFullBalancePayment
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error calculating offset");
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OffsetLoan(LoanOffsetDTO offsetDto)
        {
            try
            {
                _logger.LogInformation($"=== OFFSET LOAN POST CALLED ===");
                _logger.LogInformation($"LoanNo: {offsetDto.LoanNo}");
                _logger.LogInformation($"SharesCode: {offsetDto.SharesCode}");
                _logger.LogInformation($"Amount: {offsetDto.AmountToOffset:C}");

                if (!ModelState.IsValid)
                {
                    var errors = string.Join(", ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                    _logger.LogWarning($"ModelState invalid: {errors}");
                    TempData["ErrorMessage"] = $"Validation error: {errors}";
                    return RedirectToAction("OffsetLoan");
                }

                offsetDto.CompanyCode = GetUserCompanyCode();
                offsetDto.ProcessedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _loanService.OffsetLoanWithSharesAsync(offsetDto);

                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                    //return RedirectToAction("Details", new { loanNo = offsetDto.LoanNo });
                    return RedirectToAction("AllLoans");
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                    return RedirectToAction("OffsetLoan");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing loan offset: {ex.Message}");
                TempData["ErrorMessage"] = $"Error processing loan offset: {ex.Message}";
                return RedirectToAction("OffsetLoan");
            }
        }

        #endregion

        #region Reports

        [HttpGet]
        public async Task<IActionResult> Statement(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                var repayments = await _loanService.GetLoanRepaymentsAsync(loanNo);

                ViewBag.Loan = loan;
                ViewBag.Schedule = schedule;
                ViewBag.Repayments = repayments;


                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating loan statement for {loanNo}");
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> PrintStatement(string loanNo)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var loan = await _loanService.GetLoanByNoAsync(loanNo, companyCode);
                var schedule = await _loanService.GetLoanScheduleAsync(loanNo);
                var repayments = await _loanService.GetLoanRepaymentsAsync(loanNo);

                ViewBag.Loan = loan;
                ViewBag.Schedule = schedule;
                ViewBag.Repayments = repayments;

                ViewBag.PrintMode = true;

                return View("Statement", loan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error printing loan statement for {loanNo}");
                return View("Error");
            }
        }

        #endregion

        #region Helper Methods

        private string GetUserCompanyCode()
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();
            if (string.IsNullOrEmpty(companyCode))
            {
                companyCode = HttpContext.Session.GetString("CompanyCode");
            }

            if (string.IsNullOrEmpty(companyCode))
            {
                throw new Exception("Company code not found. Please log in again.");
            }

            return companyCode;
        }

        private int GetRequiredApprovalLevels()
        {
            // This could be configured in system settings
            return 2; // Two-level approval: Loan Officer -> Manager
        }

        #endregion


        [HttpGet]
        public async Task<IActionResult> GetWorkflowCounts(string companyCode)
        {
            try
            {
                if (string.IsNullOrEmpty(companyCode))
                {
                    companyCode = GetUserCompanyCode();
                }

                var dashboard = await _loanService.GetLoanDashboardAsync(companyCode);

                var counts = new
                {
                    success = true,
                    data = new
                    {
                        underAppraisal = dashboard.UnderAppraisal,
                        pendingApproval = dashboard.PendingApproval,
                        pendingFinalApproval = dashboard.PendingFinalApproval,
                        approvedPendingDisbursement = dashboard.ApprovedPendingDisbursement,
                        pendingApplications = dashboard.PendingApplications,
                        activeLoans = dashboard.ActiveLoans,
                        overdueLoans = dashboard.OverdueLoans
                    }
                };

                return Json(counts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading workflow counts");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchLoans(string searchType, string searchValue, string companyCode)
        {
            try
            {
                var loans = new List<object>();

                switch (searchType.ToLower())
                {
                    case "memberno":
                        var memberLoans = await _context.Loans
                            .Where(l => l.MemberNo.Contains(searchValue) && l.CompanyCode == companyCode)
                            .ToListAsync();
                        loans = await MapLoansToSearchResults(memberLoans);
                        break;

                    case "loanno":
                        var loanByNo = await _context.Loans
                            .Where(l => l.LoanNo.Contains(searchValue) && l.CompanyCode == companyCode)
                            .ToListAsync();
                        loans = await MapLoansToSearchResults(loanByNo);
                        break;

                    case "idno":
                        var membersById = await _context.Members
                            .Where(m => m.Idno == searchValue && m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();

                        var loansById = await _context.Loans
                            .Where(l => membersById.Contains(l.MemberNo) && l.CompanyCode == companyCode)
                            .ToListAsync();
                        loans = await MapLoansToSearchResults(loansById);
                        break;

                    case "fullname":
                        var membersByName = await _context.Members
                            .Where(m => (m.Surname + " " + m.OtherNames).Contains(searchValue) && m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();

                        var loansByName = await _context.Loans
                            .Where(l => membersByName.Contains(l.MemberNo) && l.CompanyCode == companyCode)
                            .ToListAsync();
                        loans = await MapLoansToSearchResults(loansByName);
                        break;

                    case "phoneno":
                        var membersByPhone = await _context.Members
                            .Where(m => m.PhoneNo.Contains(searchValue) && m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();

                        var loansByPhone = await _context.Loans
                            .Where(l => membersByPhone.Contains(l.MemberNo) && l.CompanyCode == companyCode)
                            .ToListAsync();
                        loans = await MapLoansToSearchResults(loansByPhone);
                        break;
                }

                return Json(new { success = true, loans = loans });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching loans");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<List<object>> MapLoansToSearchResults(List<Loan> loans)
        {
            var results = new List<object>();

            foreach (var loan in loans)
            {
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == loan.MemberNo);

                var memberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : loan.MemberNo;

                results.Add(new
                {
                    loanNo = loan.LoanNo,
                    memberNo = loan.MemberNo,
                    memberName = memberName,
                    loanType = loan.LoanCode,
                    principalAmount = loan.LoanAmt ?? 0,
                    status = ((Status)(loan.Status ?? 0)).ToString(),
                    applicationDate = loan.ApplicDate
                });
            }

            return results;
        }
    }
}