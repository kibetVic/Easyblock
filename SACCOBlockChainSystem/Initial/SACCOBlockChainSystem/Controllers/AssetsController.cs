using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]

    public class AssetsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AssetsController> _logger;

        public AssetsController(ApplicationDbContext context, ILogger<AssetsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET:
        public async Task<IActionResult> Index(string searchString, string assetType, string companyCode)
        {
            // Load dropdown lists for the form
            await LoadDropdownLists();

            // Get current user's company code for data filtering
            var userCompanyCode = User.FindFirst("CompanyCode")?.Value;

            var query = _context.AssetsRegisters
                .Where(a => a.CompanyCode == userCompanyCode)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(a => a.AssetName.Contains(searchString) ||
                                        a.TagNo.Contains(searchString) ||
                                        a.SerialNo.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(assetType))
            {
                query = query.Where(a => a.AssetType == assetType);
            }

            if (!string.IsNullOrEmpty(companyCode) && User.IsInRole("Admin"))
            {
                query = query.Where(a => a.CompanyCode == companyCode);
            }

            var assets = await query.OrderByDescending(a => a.ID).ToListAsync();

            // Populate filter dropdowns
            ViewBag.AssetTypes = new SelectList(await _context.AssetsRegisters
                .Where(a => a.CompanyCode == userCompanyCode)
                .Select(a => a.AssetType)
                .Distinct()
                .ToListAsync());

            return View(assets);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AssetsRegisterVm viewModel)
        {
            _logger.LogInformation("===== CREATE ACTION CALLED =====");

            // Always force CompanyCode from logged-in user
            viewModel.CompanyCode = User.FindFirst("CompanyCode")?.Value;

            // Calculate total server-side (never trust UI)
            if (viewModel.ActualValue.HasValue && viewModel.Quantity.HasValue)
                viewModel.TotalValue = viewModel.ActualValue * viewModel.Quantity;

            // Remove dropdown validation noise
            ModelState.Remove("ClassList");
            ModelState.Remove("CompanyList");

            // Explicit validation
            if (string.IsNullOrWhiteSpace(viewModel.Class))
                ModelState.AddModelError("Class", "Class is required");

            if (string.IsNullOrWhiteSpace(viewModel.AssetType))
                ModelState.AddModelError("AssetType", "Asset type is required");

            if (string.IsNullOrWhiteSpace(viewModel.AssetName))
                ModelState.AddModelError("AssetName", "Asset name is required");

            if (!viewModel.Quantity.HasValue || viewModel.Quantity <= 0)
                ModelState.AddModelError("Quantity", "Quantity must be greater than 0");

            if (!viewModel.ActualValue.HasValue || viewModel.ActualValue <= 0)
                ModelState.AddModelError("ActualValue", "Actual value must be greater than 0");

            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys)
                {
                    foreach (var err in ModelState[key].Errors)
                        _logger.LogWarning($"{key}: {err.ErrorMessage}");
                }

                TempData["ErrorMessage"] = "Please correct the highlighted fields.";

                await LoadDropdownLists(viewModel);

                return View("Index",
                    await _context.AssetsRegisters
                        .Where(a => a.CompanyCode == viewModel.CompanyCode)
                        .OrderByDescending(a => a.ID)
                        .ToListAsync());
            }

            try
            {
                // Get current user's username (AuditId)
                var auditId = User.Identity?.Name ?? "System";
                var currentTime = DateTime.Now;

                // Generate TransactionNo in format: {auditId}{HH:mm:ss}:a{mm}{p{ss}?
                // Based on your screenshot: "supervisor11:22:50:a22p22"
                var transactionNo = $"{auditId}{currentTime:HH:mm:ss}:a{currentTime:mm}p{currentTime:ss}";

                // Alternative format if the above doesn't match exactly:
                // var transactionNo = $"{auditId}{currentTime:HH:mm:ss}:a{currentTime:mm}p{currentTime:ss}";

                var asset = new AssetsRegister
                {
                    Class = viewModel.Class,
                    AssetType = viewModel.AssetType,
                    AssetName = viewModel.AssetName,
                    TagNo = viewModel.TagNo,
                    SerialNo = viewModel.SerialNo,
                    Quantity = viewModel.Quantity,
                    ActualValue = viewModel.ActualValue,
                    MarketValue = viewModel.MarketValue,
                    TotalValue = viewModel.TotalValue,
                    DateOfManufacture = viewModel.DateOfManufacture,
                    DatePurchased = viewModel.DatePurchased ?? DateTime.Now,
                    TransactionNo = transactionNo, // Auto-generated
                    CompanyCode = viewModel.CompanyCode,
                    Location = viewModel.Location,
                    posted = false,
                    AuditId = auditId, // Add this if your AssetsRegister model has AuditId
                    AuditTime = currentTime // Add this if your AssetsRegister model has AuditTime
                };

                _context.Add(asset);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Asset created with TransactionNo: {transactionNo}");
                TempData["SuccessMessage"] = "Asset created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Create failed");
                TempData["ErrorMessage"] = "Unable to save asset.";

                await LoadDropdownLists(viewModel);

                return View("Index",
                    await _context.AssetsRegisters
                        .Where(a => a.CompanyCode == viewModel.CompanyCode)
                        .OrderByDescending(a => a.ID)
                        .ToListAsync());
            }
        }

        // POST: Assets/Edit - Handle form submission for updating asset
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AssetsRegisterVm viewModel)
        {
            if (viewModel.ID == null || viewModel.ID == 0)
            {
                return NotFound();
            }

            // Check permission
            var userCompanyCode = User.FindFirst("CompanyCode")?.Value;
            var existingAsset = await _context.AssetsRegisters.AsNoTracking()
                .FirstOrDefaultAsync(a => a.ID == viewModel.ID);

            if (existingAsset == null)
            {
                return NotFound();
            }

            if (existingAsset.CompanyCode != userCompanyCode && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Preserve CompanyCode
            viewModel.CompanyCode = existingAsset.CompanyCode;

            // Recalculate Total Value
            if (viewModel.ActualValue.HasValue && viewModel.Quantity.HasValue)
            {
                viewModel.TotalValue = viewModel.ActualValue * viewModel.Quantity;
            }

            // Remove validation for dropdown lists
            //ModelState.Remove("AssetTypeList");
            ModelState.Remove("ClassList");
            ModelState.Remove("CompanyList");
            //ModelState.Remove("LocationList");

            if (ModelState.IsValid)
            {
                try
                {
                    // Map ViewModel to Entity
                    var asset = new AssetsRegister
                    {
                        ID = viewModel.ID,
                        Class = viewModel.Class,
                        AssetType = viewModel.AssetType,
                        AssetName = viewModel.AssetName,
                        TagNo = viewModel.TagNo,
                        SerialNo = viewModel.SerialNo,
                        Quantity = viewModel.Quantity,
                        ActualValue = viewModel.ActualValue,
                        MarketValue = viewModel.MarketValue,
                        TotalValue = viewModel.TotalValue,
                        DateOfManufacture = viewModel.DateOfManufacture,
                        DatePurchased = viewModel.DatePurchased,
                        TransactionNo = viewModel.TransactionNo,
                        CompanyCode = viewModel.CompanyCode,
                        Location = viewModel.Location,
                        posted = viewModel.posted ?? false
                    };

                    _context.Update(asset);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Asset updated: {asset.AssetName} (ID: {asset.ID})");
                    TempData["SuccessMessage"] = "Asset updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AssetExists(viewModel.ID.Value))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating asset");
                    TempData["ErrorMessage"] = "Unable to update asset. Please try again.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Please fill in all required fields correctly.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Assets/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(long id)
        {
            var asset = await _context.AssetsRegisters.FindAsync(id);

            if (asset == null)
            {
                return NotFound();
            }

            var userCompanyCode = User.FindFirst("CompanyCode")?.Value;
            if (asset.CompanyCode != userCompanyCode && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            try
            {
                _context.AssetsRegisters.Remove(asset);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Asset deleted: {asset.AssetName} (ID: {asset.ID})");
                TempData["SuccessMessage"] = "Asset deleted successfully!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting asset");
                TempData["ErrorMessage"] = "Unable to delete asset. Please try again.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Assets/GetAsset/5
        [HttpGet]
        public async Task<JsonResult> GetAsset(long id)
        {
            var asset = await _context.AssetsRegisters.FindAsync(id);
            if (asset == null)
            {
                return Json(null);
            }

            return Json(new
            {
                id = asset.ID,
                assetClass = asset.Class,
                assetType = asset.AssetType,
                assetName = asset.AssetName,
                tagNo = asset.TagNo,
                serialNo = asset.SerialNo,
                marketValue = asset.MarketValue,
                quantity = asset.Quantity,  // This will now be int
                actualValue = asset.ActualValue,
                totalValue = asset.TotalValue,
                datePurchased = asset.DatePurchased?.ToString("yyyy-MM-dd")
            });
        }

        #region Helper Methods

        private async Task LoadDropdownLists(AssetsRegisterVm viewModel = null)
        {
            // Asset Classes Dropdown
            var classList = new List<SelectListItem>
            {
                new SelectListItem { Value = "FIRE - MATERIAL DAMAGE", Text = "FIRE - MATERIAL DAMAGE" },
                new SelectListItem { Value = "BURGLARY INSUARANCE", Text = "BURGLARY INSUARANCE" },
                new SelectListItem { Value = "ALL RISK", Text = "ALL RISK" },

            };
            ViewBag.ClassList = new SelectList(classList, "Value", "Text", viewModel?.Class);

            //// Asset Types Dropdown
            //var assetTypeList = new List<SelectListItem>
            //{
            //    new SelectListItem { Value = "EQUIPMENT", Text = "EQUIPMENT" },
            //    new SelectListItem { Value = "FURNITURE", Text = "FURNITURE" },
            //    new SelectListItem { Value = "VEHICLE", Text = "VEHICLE" },
            //    new SelectListItem { Value = "MACHINERY", Text = "MACHINERY" },
            //    new SelectListItem { Value = "COMPUTER", Text = "COMPUTER" },
            //    new SelectListItem { Value = "SOFTWARE", Text = "SOFTWARE" },
            //    new SelectListItem { Value = "OTHER", Text = "OTHER" }
            //};
            //ViewBag.AssetTypeList = new SelectList(assetTypeList, "Value", "Text", viewModel?.AssetType);

            // Companies Dropdown
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            {
                var companies = await _context.Companies
                    .Where(c => c.Project == true)
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new SelectListItem
                    {
                        Value = c.CompanyCode,
                        Text = $"{c.CompanyCode} - {c.CompanyName}"
                    })
                    .ToListAsync();
                ViewBag.CompanyList = new SelectList(companies, "Value", "Text", viewModel?.CompanyCode);
            }
        }

        // Locations Dropdown
        //    var locationList = new List<SelectListItem>
        //    {
        //        new SelectListItem { Value = "Head Office", Text = "Head Office" },
        //        new SelectListItem { Value = "Branch 1", Text = "Branch 1" },
        //        new SelectListItem { Value = "Branch 2", Text = "Branch 2" },
        //        new SelectListItem { Value = "Warehouse", Text = "Warehouse" },
        //        new SelectListItem { Value = "Store", Text = "Store" }
        //    };
        //    ViewBag.LocationList = new SelectList(locationList, "Value", "Text", viewModel?.Location);
        //}

        private bool AssetExists(long id)
        {
            return _context.AssetsRegisters.Any(e => e.ID == id);
        }

        #endregion
    }
}