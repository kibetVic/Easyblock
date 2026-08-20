// Controllers/ContributionMvcController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class ContributionMvcController : Controller
    {
        private readonly IMemberService _memberService;
        private readonly ILogger<ContributionMvcController> _logger;

        public ContributionMvcController(
            IMemberService memberService,
            ILogger<ContributionMvcController> logger)
        {
            _memberService = memberService;
            _logger = logger;
        }

        // GET: /ContributionMvc/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                ViewBag.CompanyCode = companyCode;

                // Get recent contributions
                var allRecentContributions = await _memberService.SearchContributionsAsync(
                    DateTime.Now.AddDays(-30),
                    DateTime.Now,
                    null,
                    null);

                // Filter by company code after retrieval
                var recentContributions = allRecentContributions
                    .Where(c => c.CompanyCode == companyCode)
                    .ToList();

                // Get share types for filter
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);

                var viewModel = new
                {
                    RecentContributions = recentContributions,
                    ShareTypes = shareTypes,
                    TotalAmount = recentContributions.Sum(c => c.Amount),
                    TodayAmount = recentContributions
                        .Where(c => c.TransactionDate.Date == DateTime.Today)
                        .Sum(c => c.Amount)
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading contributions index");
                return View("Error");
            }
        }

        public async Task<IActionResult> Add(string? memberNo = null)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);

                ViewBag.ShareTypes = shareTypes;
                ViewBag.CompanyCode = companyCode;

                var contributionDto = new ContributionDTO
                {
                    MemberNo = memberNo ?? string.Empty,
                    TransactionDate = DateTime.Now,
                    CreatedBy = User.Identity?.Name ?? "SYSTEM",
                    CompanyCode = companyCode
                };

                return View(contributionDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading add contribution form");
                return View("Error");
            }
        }

        // POST: /ContributionMvc/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ContributionDTO contributionDto)
        {
            try
            {
                _logger.LogInformation("Add contribution POST action called");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid");
                    var companyCode = GetUserCompanyCode();
                    var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                    ViewBag.ShareTypes = shareTypes;
                    return View(contributionDto);
                }

                // Set user information
                contributionDto.CompanyCode = GetUserCompanyCode();
                contributionDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                // Set default transaction date if not provided
                if (contributionDto.TransactionDate == default)
                {
                    contributionDto.TransactionDate = DateTime.Now;
                }

                _logger.LogInformation($"Adding contribution for member: {contributionDto.MemberNo}, Amount: {contributionDto.Amount:C}");

                var result = await _memberService.AddContributionAsync(contributionDto);

                TempData["SuccessMessage"] = $"Contribution of {contributionDto.Amount:C} added successfully! Receipt: {result.ReceiptNo}";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding contribution");

                // Display user-friendly error message
                if (ex.Message.Contains("not found in GL Setup"))
                {
                    ModelState.AddModelError("", ex.Message + " Please contact the administrator to configure the required accounts.");
                }
                else if (ex.Message.Contains("Validation error"))
                {
                    ModelState.AddModelError("", ex.Message.Replace("Validation error: ", ""));
                }
                else if (ex.Message.Contains("cannot be less than minimum") || ex.Message.Contains("cannot exceed maximum"))
                {
                    ModelState.AddModelError("Amount", ex.Message);
                }
                else
                {
                    ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                }

                var companyCode = GetUserCompanyCode();
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                ViewBag.ShareTypes = shareTypes;

                return View(contributionDto);
            }
        }

        //// POST: /ContributionMvc/Add
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Add(ContributionDTO contributionDto)
        //{
        //    try
        //    {
        //        _logger.LogInformation("Add contribution POST action called");

        //        if (!ModelState.IsValid)
        //        {
        //            _logger.LogWarning("Model state is invalid");
        //            var companyCode = GetUserCompanyCode();
        //            var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
        //            ViewBag.ShareTypes = shareTypes;
        //            return View(contributionDto);
        //        }

        //        // Set user information
        //        contributionDto.CompanyCode = GetUserCompanyCode();
        //        contributionDto.CreatedBy = User.Identity?.Name;

        //        _logger.LogInformation($"Adding contribution for member: {contributionDto.MemberNo}");

        //        var result = await _memberService.AddContributionAsync(contributionDto);

        //        TempData["SuccessMessage"] = $"Contribution of {contributionDto.Amount:C} added successfully! Receipt: {result.ReceiptNo}";
        //        return RedirectToAction("Details", new { id = result.Id });
        //    }
        //    catch (Exception ex)
        //    {
        //        var inner = ex.InnerException?.Message ?? ex.Message;

        //        _logger.LogError(ex, "FULL ERROR: " + inner);

        //        ModelState.AddModelError("", $"ERROR: {inner}");

        //        var companyCode = GetUserCompanyCode();
        //        var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
        //        ViewBag.ShareTypes = shareTypes;

        //        return View(contributionDto);
        //    }

        //    //catch (Exception ex)
        //    //{
        //    //    _logger.LogError(ex, "Error adding contribution");

        //    //    if (ex.Message.Contains("not found") ||
        //    //        ex.Message.Contains("Validation error") ||
        //    //        ex.Message.Contains("cannot be less") ||
        //    //        ex.Message.Contains("cannot exceed"))
        //    //    {
        //    //        ModelState.AddModelError("", ex.Message);
        //    //    }
        //    //    else
        //    //    {
        //    //        ModelState.AddModelError("", $"An error occurred: {ex.Message}");
        //    //    }

        //    //    var companyCode = GetUserCompanyCode();
        //    //    var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
        //    //    ViewBag.ShareTypes = shareTypes;

        //    //    return View(contributionDto);
        //    //}
        //}

        // GET: /ContributionMvc/Edit/{id}
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                // Get the contribution to edit
                var contributions = await _memberService.SearchContributionsAsync(null, null, null, null);
                var contribution = contributions.FirstOrDefault(c => c.Id == id);

                if (contribution == null)
                {
                    return NotFound();
                }

                // Check if contribution can be edited
                // (Optional: Add business rules - e.g., only recent contributions can be edited)
                var canEdit = CanEditContribution(contribution);
                if (!canEdit)
                {
                    TempData["ErrorMessage"] = "This contribution cannot be edited. It may be too old or already reconciled.";
                    return RedirectToAction("Details", new { id });
                }

                var companyCode = GetUserCompanyCode();
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);

                ViewBag.ShareTypes = shareTypes;
                ViewBag.CompanyCode = companyCode;
                ViewBag.ContributionId = id;

                // Convert to DTO for editing
                var editDto = new ContributionDTO
                {
                    MemberNo = contribution.MemberNo,
                    TransactionDate = contribution.TransactionDate,
                    SharesCode = contribution.SharesCode,
                    Amount = contribution.Amount,
                    ReceiptNo = contribution.ReceiptNo,
                    Remarks = contribution.Remarks,
                    PaymentMethod = "CASH", // Default, you might want to store this
                    ReferenceNo = "", // You might want to store this
                    CreatedBy = User.Identity?.Name ,
                    CompanyCode = companyCode
                };

                return View(editDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading edit form for contribution {id}");
                return View("Error");
            }
        }

        // POST: /ContributionMvc/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ContributionDTO contributionDto, string editReason)
        {
            try
            {
                _logger.LogInformation($"Edit contribution POST action for ID: {id}");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid for contribution edit");
                    var companyCode = GetUserCompanyCode();
                    var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                    ViewBag.ShareTypes = shareTypes;
                    ViewBag.ContributionId = id;
                    return View(contributionDto);
                }

                if (string.IsNullOrEmpty(editReason))
                {
                    ModelState.AddModelError("", "Reason for edit is required");
                    var companyCode = GetUserCompanyCode();
                    var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                    ViewBag.ShareTypes = shareTypes;
                    ViewBag.ContributionId = id;
                    return View(contributionDto);
                }

                // Set user information
                contributionDto.CompanyCode = GetUserCompanyCode();
                contributionDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                // You'll need to implement an UpdateContributionAsync method in your service
                // For now, we'll create a new contribution and void the old one
                var originalContributions = await _memberService.SearchContributionsAsync(null, null, null, null);
                var originalContribution = originalContributions.FirstOrDefault(c => c.Id == id);

                if (originalContribution == null)
                {
                    return NotFound();
                }

                // Create corrected contribution
                var correctedContribution = new ContributionDTO
                {
                    MemberNo = contributionDto.MemberNo,
                    TransactionDate = contributionDto.TransactionDate,
                    SharesCode = contributionDto.SharesCode,
                    Amount = contributionDto.Amount,
                    ReceiptNo = $"{originalContribution.ReceiptNo}-CORR",
                    Remarks = $"CORRECTION: {editReason}. Original: {originalContribution.Remarks}",
                    PaymentMethod = contributionDto.PaymentMethod,
                    ReferenceNo = contributionDto.ReferenceNo,
                    CreatedBy = contributionDto.CreatedBy,
                    CompanyCode = contributionDto.CompanyCode
                };

                var result = await _memberService.AddContributionAsync(correctedContribution);

                TempData["SuccessMessage"] = $"Contribution corrected successfully! New Receipt: {result.ReceiptNo}";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error editing contribution {id}");

                if (ex.Message.Contains("not found") ||
                    ex.Message.Contains("Validation error") ||
                    ex.Message.Contains("cannot be less") ||
                    ex.Message.Contains("cannot exceed"))
                {
                    ModelState.AddModelError("", ex.Message);
                }
                else
                {
                    ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                }

                var companyCode = GetUserCompanyCode();
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                ViewBag.ShareTypes = shareTypes;
                ViewBag.ContributionId = id;
                return View(contributionDto);
            }
        }

        // Helper method to check if contribution can be edited
        private bool CanEditContribution(ContributionResponseDTO contribution)
        {
            // Add your business rules here
            // Example: Only contributions from the last 7 days can be edited
            var daysSinceContribution = (DateTime.Now - contribution.TransactionDate).TotalDays;

            if (daysSinceContribution > 7)
            {
                return false; // Too old to edit
            }

            // Example: Check if contribution is already reconciled
            if (!string.IsNullOrEmpty(contribution.BlockchainTxId))
            {
                // Already on blockchain - may require special approval to edit
                return true; // or false depending on your rules
            }

            return true;
        }

        // GET: /ContributionMvc/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var contributions = await _memberService.SearchContributionsAsync(null, null, null, null);
                var contribution = contributions.FirstOrDefault(c => c.Id == id);

                if (contribution == null)
                {
                    return NotFound();
                }

                return View(contribution);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading contribution details");
                return View("Error");
            }
        }

        // GET: /ContributionMvc/Member/{memberNo}
        public async Task<IActionResult> Member(string memberNo)
        {
            try
            {
                var history = await _memberService.GetMemberContributionHistoryAsync(memberNo);
                return View(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading contributions for member {memberNo}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Search");
            }
        }

        // GET: /ContributionMvc/Search
        public async Task<IActionResult> Search()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);

                ViewBag.ShareTypes = shareTypes;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading search page");
                return View("Error");
            }
        }

        // GET: /ContributionMvc/SearchResults
        [HttpGet]
        public async Task<IActionResult> SearchResults(
            DateTime? fromDate,
            DateTime? toDate,
            string? memberNo,
            string? shareType)
        {
            try
            {
                var contributions = await _memberService.SearchContributionsAsync(
                    fromDate, toDate, memberNo, shareType);

                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.MemberNo = memberNo;
                ViewBag.ShareType = shareType;
                ViewBag.TotalAmount = contributions.Sum(c => c.Amount);

                return View(contributions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching contributions");
                return View("Error");
            }
        }

        // GET: /ContributionMvc/Report
        public async Task<IActionResult> Report()
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // Get data for the report
                var today = DateTime.Today;
                var monthStart = new DateTime(today.Year, today.Month, 1);
                var yearStart = new DateTime(today.Year, 1, 1);

                var todayContributions = await _memberService.SearchContributionsAsync(today, today, null, null);
                var monthContributions = await _memberService.SearchContributionsAsync(monthStart, today, null, null);
                var yearContributions = await _memberService.SearchContributionsAsync(yearStart, today, null, null);

                var shareTypes = await _memberService.GetShareTypesAsync(companyCode);
                var shareTypeSummary = new List<object>();

                foreach (var shareType in shareTypes)
                {
                    var contributions = await _memberService.SearchContributionsAsync(
                        yearStart, today, null, shareType.SharesCode);

                    shareTypeSummary.Add(new
                    {
                        ShareType = shareType.SharesType,
                        Code = shareType.SharesCode,
                        Count = contributions.Count,
                        Total = contributions.Sum(c => c.Amount)
                    });
                }

                var viewModel = new
                {
                    Today = new
                    {
                        Count = todayContributions.Count,
                        Total = todayContributions.Sum(c => c.Amount)
                    },
                    ThisMonth = new
                    {
                        Count = monthContributions.Count,
                        Total = monthContributions.Sum(c => c.Amount)
                    },
                    ThisYear = new
                    {
                        Count = yearContributions.Count,
                        Total = yearContributions.Sum(c => c.Amount)
                    },
                    ShareTypeSummary = shareTypeSummary
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading contribution report");
                return View("Error");
            }
        }

        private string GetUserCompanyCode()
        {
            // Get company code from user claims or session
            var companyCode = User.FindFirst("CompanyCode")?.Value;
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
    }
}