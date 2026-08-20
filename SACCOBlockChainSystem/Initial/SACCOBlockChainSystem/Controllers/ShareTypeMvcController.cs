// Controllers/ShareTypeMvcController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class ShareTypeMvcController : Controller
    {
        private readonly IShareTypeService _shareTypeService;
        private readonly IGlAccountService _glAccountService;
        private readonly ILogger<ShareTypeMvcController> _logger;

        public ShareTypeMvcController(
            IShareTypeService shareTypeService,
            IGlAccountService glAccountService,
            ILogger<ShareTypeMvcController> logger)
        {
            _shareTypeService = shareTypeService;
            _glAccountService = glAccountService;
            _logger = logger;
        }

        // GET: /ShareTypeMvc/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareTypes = await _shareTypeService.GetShareTypesByCompanyAsync(companyCode);

                // Create a new instance of ShareTypeCreateDTO for the form
                var createDto = new ShareTypeCreateDTO
                {
                    CompanyCode = companyCode,
                    CreatedBy = User.Identity?.Name ?? "SYSTEM",
                    IsMainShares = false,
                    Priority = 1,
                    MinAmount = 0,
                    LowerLimit = 0,
                    ElseRatio = 0
                };

                // Get GL accounts for dropdown
                var glAccounts = await _glAccountService.GetShareGlAccountsAsync(companyCode);

                // Get company name
                var companyName = User.FindFirst("CompanyName")?.Value ??
                                 HttpContext.Session.GetString("CompanyName") ??
                                 "JUHUDI SACCO";

                // Convert share types to ViewModel for the table
                var shareTypeViewModels = shareTypes.Select(st => new ShareTypeViewModel
                {
                    SharesCode = st.SharesCode,
                    SharesType = st.SharesType,
                    SharesAcc = st.SharesAcc,
                    MinAmount = st.MinAmount,
                    MaxAmount = st.MaxAmount ?? 0,
                    LoanToShareRatio = st.LoanToShareRatio.HasValue ? (decimal)st.LoanToShareRatio.Value : 0m,
                    IsMainShares = st.IsMainShares,
                    Withdrawable = st.Withdrawable,
                    UsedToOffset = st.UsedToOffset,
                    UsedToGuarantee = st.UsedToGuarantee,
                    CompanyCode = st.CompanyCode,
                    CompanyName = companyName
                }).ToList();

                // Pass data to view via ViewBag
                ViewBag.GlAccounts = glAccounts;
                ViewBag.ShareTypes = shareTypeViewModels;
                ViewBag.CompanyName = companyName;

                return View(createDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading share types index");
                TempData["ErrorMessage"] = "Error loading share types. Please try again.";
                return View("Error");
            }
        }

        // POST: /ShareTypeMvc/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ShareTypeCreateDTO shareTypeDto, string action)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                // Log the received action
                _logger.LogInformation($"========== CREATE POST ==========");
                _logger.LogInformation($"Received action: '{action}'");
                _logger.LogInformation($"ShareCode: {shareTypeDto.SharesCode}");
                _logger.LogInformation($"ShareType: {shareTypeDto.SharesType}");
                _logger.LogInformation($"Company: {companyCode}");

                // If action is null or empty, default to save
                if (string.IsNullOrEmpty(action))
                {
                    action = "save";
                }

                shareTypeDto.CompanyCode = companyCode;
                shareTypeDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                // Validate required fields
                if (string.IsNullOrWhiteSpace(shareTypeDto.SharesCode))
                {
                    TempData["ErrorMessage"] = "Share Code is required";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrWhiteSpace(shareTypeDto.SharesType))
                {
                    TempData["ErrorMessage"] = "Share Type is required";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrWhiteSpace(shareTypeDto.SharesAcc))
                {
                    TempData["ErrorMessage"] = "Share Account is required";
                    return RedirectToAction("Index");
                }

                //if (string.IsNullOrWhiteSpace(shareTypeDto.Ppacc))
                //{
                //    TempData["ErrorMessage"] = "PP Account is required";
                //    return RedirectToAction("Index");
                //}

                // Process based on action
                if (action.ToLower() == "save")
                {
                    _logger.LogInformation("Processing SAVE operation for {SharesCode}", shareTypeDto.SharesCode);

                    // CHECK FOR DUPLICATE KEY BEFORE SAVING
                    try
                    {
                        var existingShareType = await _shareTypeService.GetShareTypeByCodeAsync(shareTypeDto.SharesCode, companyCode);
                        if (existingShareType != null)
                        {
                            _logger.LogWarning("Duplicate share code attempted: {SharesCode}", shareTypeDto.SharesCode);
                            TempData["ErrorMessage"] = $"Share code '{shareTypeDto.SharesCode}' already exists. Please use a different code.";
                            return RedirectToAction("Index");
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                        // Share type doesn't exist, proceed with save
                    }

                    var result = await _shareTypeService.CreateShareTypeAsync(shareTypeDto);
                    _logger.LogInformation("Successfully created share type: {SharesCode}", shareTypeDto.SharesCode);
                    TempData["SuccessMessage"] = $"Share type '{result.SharesType}' created successfully!";

                    return RedirectToAction("Index");
                }
                else if (action.ToLower() == "update")
                {
                    _logger.LogInformation("Processing UPDATE operation for {SharesCode}", shareTypeDto.SharesCode);

                    // CHECK IF THE RECORD EXISTS BEFORE UPDATING
                    try
                    {
                        var existingShareType = await _shareTypeService.GetShareTypeByCodeAsync(shareTypeDto.SharesCode, companyCode);
                        if (existingShareType == null)
                        {
                            _logger.LogWarning("Share code not found for update: {SharesCode}", shareTypeDto.SharesCode);
                            TempData["ErrorMessage"] = $"Share code '{shareTypeDto.SharesCode}' not found. Please check the code and try again.";
                            return RedirectToAction("Index");
                        }

                        var updateDto = new ShareTypeUpdateDTO
                        {
                            SharesCode = shareTypeDto.SharesCode,
                            SharesType = shareTypeDto.SharesType,
                            SharesAcc = shareTypeDto.SharesAcc,
                            MinAmount = shareTypeDto.MinAmount,
                            MaxAmount = shareTypeDto.MaxAmount,
                            LoanToShareRatio = shareTypeDto.LoanToShareRatio,
                            IsMainShares = shareTypeDto.IsMainShares,
                            Withdrawable = shareTypeDto.Withdrawable,
                            UsedToOffset = shareTypeDto.UsedToOffset,
                            UsedToGuarantee = shareTypeDto.UsedToGuarantee,
                            Ppacc = string.IsNullOrEmpty(shareTypeDto.Ppacc) ? shareTypeDto.SharesAcc : shareTypeDto.Ppacc,
                            CompanyCode = companyCode,
                            CreatedBy = shareTypeDto.CreatedBy,
                            ContraAcc = shareTypeDto.ContraAcc,
                            PlacePeriod = shareTypeDto.PlacePeriod,
                            Issharecapital = shareTypeDto.Issharecapital,
                            Interest = shareTypeDto.Interest,
                            Guarantor = shareTypeDto.Guarantor,
                            Loanquaranto = shareTypeDto.Loanquaranto,
                            Priority = shareTypeDto.Priority,
                            LowerLimit = shareTypeDto.LowerLimit,
                            ElseRatio = shareTypeDto.ElseRatio
                        };

                        await _shareTypeService.UpdateShareTypeAsync(updateDto.SharesCode, updateDto);
                        _logger.LogInformation("Successfully updated share type: {SharesCode}", shareTypeDto.SharesCode);
                        TempData["SuccessMessage"] = $"Share type '{shareTypeDto.SharesType}' updated successfully!";

                        return RedirectToAction("Index");
                    }
                    catch (KeyNotFoundException ex)
                    {
                        _logger.LogWarning(ex, "Share code not found for update: {SharesCode}", shareTypeDto.SharesCode);
                        TempData["ErrorMessage"] = $"Share code '{shareTypeDto.SharesCode}' not found.";
                        return RedirectToAction("Index");
                    }
                }
                else if (action.ToLower() == "delete")
                {
                    _logger.LogInformation("Processing DELETE operation for {SharesCode}", shareTypeDto.SharesCode);

                    // CHECK IF THE RECORD EXISTS BEFORE DELETING
                    try
                    {
                        var existingShareType = await _shareTypeService.GetShareTypeByCodeAsync(shareTypeDto.SharesCode, companyCode);
                        if (existingShareType == null)
                        {
                            _logger.LogWarning("Share code not found for delete: {SharesCode}", shareTypeDto.SharesCode);
                            TempData["ErrorMessage"] = $"Share code '{shareTypeDto.SharesCode}' not found.";
                            return RedirectToAction("Index");
                        }

                        await _shareTypeService.DeleteShareTypeAsync(shareTypeDto.SharesCode, companyCode);
                        _logger.LogInformation("Successfully deleted share type: {SharesCode}", shareTypeDto.SharesCode);
                        TempData["SuccessMessage"] = $"Share type '{shareTypeDto.SharesType}' deleted successfully!";

                        return RedirectToAction("Index");
                    }
                    catch (ValidationException ex)
                    {
                        _logger.LogWarning(ex, "Cannot delete share type: {SharesCode}", shareTypeDto.SharesCode);
                        TempData["ErrorMessage"] = ex.Message;
                        return RedirectToAction("Index");
                    }
                    catch (KeyNotFoundException ex)
                    {
                        _logger.LogWarning(ex, "Share code not found for delete: {SharesCode}", shareTypeDto.SharesCode);
                        TempData["ErrorMessage"] = $"Share code '{shareTypeDto.SharesCode}' not found.";
                        return RedirectToAction("Index");
                    }
                }
                else
                {
                    _logger.LogWarning($"Unknown action: '{action}'");
                    TempData["ErrorMessage"] = $"Unknown action: {action}";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing share type for {SharesCode}", shareTypeDto?.SharesCode);

                // Get the innermost exception message
                var error = ex.InnerException?.InnerException?.Message
                            ?? ex.InnerException?.Message
                            ?? ex.Message;

                // Check if it's a duplicate key error
                if (error.Contains("duplicate key") || error.Contains("PRIMARY KEY") || error.Contains("already exists"))
                {
                    TempData["ErrorMessage"] = $"Share code '{shareTypeDto?.SharesCode}' already exists. Please use a different code.";
                }
                else if (error.Contains("in use") || error.Contains("used by"))
                {
                    TempData["ErrorMessage"] = error;
                }
                else
                {
                    TempData["ErrorMessage"] = $"An error occurred: {error}";
                }

                return RedirectToAction("Index");
            }
        }

        // GET: /ShareTypeMvc/Edit/{sharesCode}
        public async Task<IActionResult> Edit(string sharesCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);

                // Get GL accounts for dropdown
                var glAccounts = await _glAccountService.GetShareGlAccountsAsync(companyCode);
                ViewBag.GlAccounts = glAccounts;

                // Convert to UpdateDTO
                var updateDto = new ShareTypeUpdateDTO
                {
                    SharesCode = shareType.SharesCode,
                    SharesType = shareType.SharesType,
                    SharesAcc = shareType.SharesAcc,
                    ContraAcc = shareType.ContraAcc,
                    PlacePeriod = shareType.PlacePeriod,
                    LoanToShareRatio = shareType.LoanToShareRatio,
                    Issharecapital = shareType.Issharecapital,
                    Interest = shareType.Interest,
                    MaxAmount = shareType.MaxAmount,
                    Guarantor = shareType.Guarantor,
                    IsMainShares = shareType.IsMainShares,
                    UsedToGuarantee = shareType.UsedToGuarantee,
                    UsedToOffset = shareType.UsedToOffset,
                    Withdrawable = shareType.Withdrawable,
                    Loanquaranto = shareType.Loanquaranto,
                    Priority = shareType.Priority,
                    MinAmount = shareType.MinAmount,
                    LowerLimit = shareType.LowerLimit,
                    ElseRatio = shareType.ElseRatio,
                    CompanyCode = shareType.CompanyCode,
                    CreatedBy = User.Identity?.Name ?? "SYSTEM"
                };

                // Get existing share types for the table
                var shareTypes = await _shareTypeService.GetShareTypesByCompanyAsync(companyCode);
                var shareTypeViewModels = shareTypes.Select(st => new ShareTypeViewModel
                {
                    SharesCode = st.SharesCode,
                    SharesType = st.SharesType,
                    SharesAcc = st.SharesAcc,
                    MinAmount = st.MinAmount,
                    MaxAmount = st.MaxAmount ?? 0,
                    LoanToShareRatio = st.LoanToShareRatio.HasValue ? (decimal)st.LoanToShareRatio.Value : 0m,
                   // Ppacc = st.Ppacc,
                    IsMainShares = st.IsMainShares,
                    Withdrawable = st.Withdrawable,
                    UsedToOffset = st.UsedToOffset,
                    UsedToGuarantee = st.UsedToGuarantee,
                    CompanyCode = st.CompanyCode,
                }).ToList();

                ViewBag.ShareTypes = shareTypeViewModels;
                ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "JUHUDI SACCO";

                return View(updateDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading edit form for share type {sharesCode}");
                TempData["ErrorMessage"] = $"Error loading share type {sharesCode} for editing.";
                return RedirectToAction("Index");
            }
        }

        // POST: /ShareTypeMvc/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ShareTypeUpdateDTO updateDto)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                if (!ModelState.IsValid)
                {
                    // Reload data for the view
                    var glAccounts = await _glAccountService.GetShareGlAccountsAsync(companyCode);
                    ViewBag.GlAccounts = glAccounts;

                    var shareTypes = await _shareTypeService.GetShareTypesByCompanyAsync(companyCode);
                    var shareTypeViewModels = shareTypes.Select(st => new ShareTypeViewModel
                    {
                        SharesCode = st.SharesCode,
                        SharesType = st.SharesType,
                        SharesAcc = st.SharesAcc,
                        MinAmount = st.MinAmount,
                        MaxAmount = st.MaxAmount ?? 0,
                        LoanToShareRatio = st.LoanToShareRatio.HasValue ? (decimal)st.LoanToShareRatio.Value : 0m,
                        IsMainShares = st.IsMainShares,
                        Withdrawable = st.Withdrawable,
                        UsedToOffset = st.UsedToOffset,
                        UsedToGuarantee = st.UsedToGuarantee,
                        CompanyCode = st.CompanyCode,
                    }).ToList();

                    ViewBag.ShareTypes = shareTypeViewModels;
                    ViewBag.CompanyName = User.FindFirst("CompanyName")?.Value ?? "JUHUDI SACCO";

                    return View(updateDto);
                }

                updateDto.CompanyCode = companyCode;
                updateDto.CreatedBy = User.Identity?.Name ?? "SYSTEM";

                var result = await _shareTypeService.UpdateShareTypeAsync(updateDto.SharesCode, updateDto);

                TempData["SuccessMessage"] = $"Share type '{result.SharesType}' updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating share type {updateDto?.SharesCode}");
                TempData["ErrorMessage"] = $"Error updating share type: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        // GET: /ShareTypeMvc/Details/{sharesCode}
        public async Task<IActionResult> Details(string sharesCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);

                return View(shareType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading details for share type {sharesCode}");
                TempData["ErrorMessage"] = $"Error loading share type details.";
                return RedirectToAction("Index");
            }
        }

        // GET: /ShareTypeMvc/Delete/{sharesCode}
        public async Task<IActionResult> Delete(string sharesCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);

                return View(shareType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading delete confirmation for share type {sharesCode}");
                TempData["ErrorMessage"] = $"Error loading share type details.";
                return RedirectToAction("Index");
            }
        }

        // POST: /ShareTypeMvc/DeleteConfirmed
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string sharesCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();

                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);
                var shareTypeName = shareType.SharesType;

                await _shareTypeService.DeleteShareTypeAsync(sharesCode, companyCode);

                TempData["SuccessMessage"] = $"Share type '{shareTypeName}' deleted successfully!";
                return RedirectToAction("Index");
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, $"Cannot delete share type {sharesCode}");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting share type {sharesCode}");
                TempData["ErrorMessage"] = $"Error deleting share type: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        // GET: /ShareTypeMvc/GetShareTypeDetails
        [HttpGet]
        public async Task<IActionResult> GetShareTypeDetails(string sharesCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var shareType = await _shareTypeService.GetShareTypeByCodeAsync(sharesCode, companyCode);

                if (shareType == null)
                {
                    return Json(new { success = false, message = "Share type not found" });
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        shareType.SharesCode,
                        shareType.SharesType,
                        shareType.SharesAcc,
                        shareType.MinAmount,
                        MaxAmount = shareType.MaxAmount ?? 0,
                        LoanToShareRatio = shareType.LoanToShareRatio ?? 0,
                       //shareType.Ppacc,
                        shareType.IsMainShares,
                        shareType.Withdrawable,
                        shareType.UsedToOffset,
                        shareType.UsedToGuarantee
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching share type details");
                return Json(new { success = false, message = "Error retrieving data" });
            }
        }

        // GET: /ShareTypeMvc/GetAccountDetails
        [HttpGet]
        public async Task<IActionResult> GetAccountDetails(string accountCode)
        {
            try
            {
                var companyCode = GetUserCompanyCode();
                var account = await _glAccountService.GetGlAccountByCodeAsync(accountCode, companyCode);

                if (account != null)
                {
                    return Json(new
                    {
                        success = true,
                        accountName = account.Glaccname,
                        accountCode = account.AccNo,
                    });
                }

                return Json(new { success = false, message = "Account not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting account details for code: {accountCode}");
                return Json(new { success = false, message = "Error retrieving account details" });
            }
        }

        private string GetUserCompanyCode()
        {
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