using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class ShareTransferController : Controller
    {
        private readonly IShareTransferService _shareTransferService;
        private readonly IMemberService _memberService;
        private readonly IShareService _shareService;
        private readonly ILogger<ShareTransferController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ICompanyContextService _companyContextService;

        public ShareTransferController(
            IShareTransferService shareTransferService,
            IMemberService memberService,
            IShareService shareService,
            ILogger<ShareTransferController> logger,
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ICompanyContextService companyContextService)
        {
            _shareTransferService = shareTransferService;
            _memberService = memberService;
            _shareService = shareService;
            _logger = logger;
            _context = context;
            _blockchainService = blockchainService;
            _companyContextService = companyContextService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string status = "All")
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                List<ShareTransferResponseDTO> transfers = new List<ShareTransferResponseDTO>();

                var query = _context.ShareTransfers
                    .Include(t => t.Transferor)
                    .Include(t => t.Transferee)
                    .Include(t => t.ShareType)
                    .Where(t => t.CompanyCode == companyCode);

                if (status == "Pending")
                {
                    query = query.Where(t => t.Status == "Pending");
                }
                else if (status == "Approved")
                {
                    query = query.Where(t => t.Status == "Approved");
                }
                else if (status == "Completed")
                {
                    query = query.Where(t => t.Status == "Completed");
                }
                else if (status == "Rejected")
                {
                    query = query.Where(t => t.Status == "Rejected");
                }
                else if (status == "Cancelled")
                {
                    query = query.Where(t => t.Status == "Cancelled");
                }

                var transfersList = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

                transfers = transfersList.Select(t => new ShareTransferResponseDTO
                {
                    Id = t.Id,
                    TransferNo = t.TransferNo,
                    TransferorMemberNo = t.TransferorMemberNo,
                    TransferorName = t.Transferor != null ? $"{t.Transferor.Surname} {t.Transferor.OtherNames}" : "",
                    TransfereeMemberNo = t.TransfereeMemberNo,
                    TransfereeName = t.Transferee != null ? $"{t.Transferee.Surname} {t.Transferee.OtherNames}" : "",
                    SharesCode = t.SharesCode,
                    SharesType = t.ShareType != null ? t.ShareType.SharesType ?? t.SharesCode : t.SharesCode,
                    NumberOfShares = t.NumberOfShares,
                    PricePerShare = t.PricePerShare,
                    TotalAmount = t.TotalAmount,
                    TransferDate = t.TransferDate,
                    TransferType = t.TransferType,
                    Status = t.Status,
                    TransferFee = t.TransferFee,
                    StampDuty = t.StampDuty,
                    TotalCharges = t.TotalCharges,
                    ApprovedBy = t.ApprovedBy,
                    ApprovalDate = t.ApprovalDate,
                    BlockchainTxId = t.BlockchainTxId,
                    CreatedAt = t.CreatedAt
                }).ToList();

                ViewBag.CurrentStatus = status;
                return View(transfers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading share transfers");
                TempData["ErrorMessage"] = "Error loading share transfers";
                return View(new List<ShareTransferResponseDTO>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Get share types
                var shareTypes = await _context.Sharetypes
                    .Where(s => s.CompanyCode == companyCode)
                    .ToListAsync();

                ViewBag.ShareTypes = shareTypes;

                return View(new ShareTransferDTO
                {
                    TransferDate = DateTime.Now,
                    TransferType = "Sale",
                    PricePerShare = 100
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading create share transfer form");
                TempData["ErrorMessage"] = "Error loading form";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ShareTransferDTO dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Validate required fields
                if (string.IsNullOrEmpty(dto.TransferorMemberNo))
                {
                    ModelState.AddModelError("TransferorMemberNo", "Transferor member is required");
                }
                if (string.IsNullOrEmpty(dto.TransfereeMemberNo))
                {
                    ModelState.AddModelError("TransfereeMemberNo", "Transferee member is required");
                }
                if (string.IsNullOrEmpty(dto.SharesCode))
                {
                    ModelState.AddModelError("SharesCode", "Share type is required");
                }
                if (dto.NumberOfShares <= 0)
                {
                    ModelState.AddModelError("NumberOfShares", "Number of shares must be greater than 0");
                }
                if (dto.PricePerShare <= 0)
                {
                    ModelState.AddModelError("PricePerShare", "Price per share must be greater than 0");
                }

                if (!ModelState.IsValid)
                {
                    var shareTypes = await _context.Sharetypes
                        .Where(s => s.CompanyCode == companyCode)
                        .ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Validate transferor exists and is active
                var transferor = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == dto.TransferorMemberNo && m.CompanyCode == companyCode);

                if (transferor == null)
                {
                    ModelState.AddModelError("TransferorMemberNo", $"Transferor member {dto.TransferorMemberNo} not found");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                if (transferor.Withdrawn == true)
                {
                    ModelState.AddModelError("TransferorMemberNo", "Transferor member has already withdrawn");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Validate transferee exists and is active
                var transferee = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == dto.TransfereeMemberNo && m.CompanyCode == companyCode);

                if (transferee == null)
                {
                    ModelState.AddModelError("TransfereeMemberNo", $"Transferee member {dto.TransfereeMemberNo} not found");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                if (transferee.Withdrawn == true)
                {
                    ModelState.AddModelError("TransfereeMemberNo", "Transferee member has already withdrawn");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Check if transferor and transferee are the same
                if (dto.TransferorMemberNo == dto.TransfereeMemberNo)
                {
                    ModelState.AddModelError("", "Transferor and Transferee cannot be the same member");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Get source member shares
                var sourceShare = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == dto.TransferorMemberNo &&
                                              s.Sharescode == dto.SharesCode &&
                                              s.CompanyCode == companyCode);

                var sourceSharesAvailable = sourceShare?.TotalShares ?? 0;

                if (sourceSharesAvailable < dto.NumberOfShares)
                {
                    ModelState.AddModelError("NumberOfShares", $"Insufficient shares. Available: {sourceSharesAvailable}, Requested: {dto.NumberOfShares}");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Get share type details
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(s => s.SharesCode == dto.SharesCode && s.CompanyCode == companyCode);

                if (shareType == null)
                {
                    ModelState.AddModelError("SharesCode", $"Share type {dto.SharesCode} not found");
                    var shareTypes = await _context.Sharetypes.Where(s => s.CompanyCode == companyCode).ToListAsync();
                    ViewBag.ShareTypes = shareTypes;
                    return View(dto);
                }

                // Calculate fees
                var totalAmount = dto.NumberOfShares * dto.PricePerShare;
                var transferFee = totalAmount * 0.01m;
                var stampDuty = totalAmount * 0.005m;
                var totalCharges = transferFee + stampDuty;

                // Get or create destination share record
                var destShare = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == dto.TransfereeMemberNo &&
                                              s.Sharescode == dto.SharesCode &&
                                              s.CompanyCode == companyCode);

                var destBalanceBefore = destShare?.TotalShares ?? 0;

                // Update source shares (deduct)
                if (sourceShare != null)
                {
                    sourceShare.TotalShares = sourceSharesAvailable - dto.NumberOfShares;
                    sourceShare.AuditId = User.Identity?.Name ?? "SYSTEM";
                    sourceShare.AuditTime = DateTime.Now;
                    sourceShare.AuditDateTime = DateTime.Now;
                    sourceShare.TransDate = DateTime.Now;
                }

                // Update or create destination shares (add)
                if (destShare == null)
                {
                    destShare = new Share
                    {
                        MemberNo = dto.TransfereeMemberNo,
                        Sharescode = dto.SharesCode,
                        TotalShares = dto.NumberOfShares,
                        TransDate = DateTime.Now,
                        AuditId = User.Identity?.Name ?? "SYSTEM",
                        AuditTime = DateTime.Now,
                        Initshares = dto.NumberOfShares,
                        CompanyCode = companyCode,
                        AuditDateTime = DateTime.Now
                    };
                    _context.Shares.Add(destShare);
                }
                else
                {
                    destShare.TotalShares = destBalanceBefore + dto.NumberOfShares;
                    destShare.AuditId = User.Identity?.Name ?? "SYSTEM";
                    destShare.AuditTime = DateTime.Now;
                    destShare.AuditDateTime = DateTime.Now;
                    destShare.TransDate = DateTime.Now;
                }

                // Generate transfer number
                var transferNo = await GenerateTransferNumberAsync(companyCode);

                // Create transfer record
                var shareTransfer = new ShareTransfer
                {
                    TransferNo = transferNo,
                    TransferorMemberNo = dto.TransferorMemberNo,
                    TransfereeMemberNo = dto.TransfereeMemberNo,
                    CompanyCode = companyCode,
                    SharesCode = dto.SharesCode,
                    NumberOfShares = dto.NumberOfShares,
                    PricePerShare = dto.PricePerShare,
                    TotalAmount = totalAmount,
                    TransferDate = dto.TransferDate,
                    TransferType = dto.TransferType,
                    Status = "Completed",
                    TransferorBalanceBefore = sourceSharesAvailable,
                    TransferorBalanceAfter = sourceSharesAvailable - dto.NumberOfShares,
                    TransfereeBalanceBefore = destBalanceBefore,
                    TransfereeBalanceAfter = destBalanceBefore + dto.NumberOfShares,
                    PaymentMethod = dto.PaymentMethod,
                    PaymentReference = dto.PaymentReference,
                    TransferFee = transferFee,
                    StampDuty = stampDuty,
                    OtherCharges = 0,
                    TotalCharges = totalCharges,
                    TransferDocumentPath = dto.TransferDocumentPath,
                    Remarks = dto.Remarks,
                    CreatedBy = User.Identity?.Name ?? "SYSTEM",
                    CreatedAt = DateTime.Now,
                    ModifiedBy = User.Identity?.Name ?? "SYSTEM",
                    ModifiedAt = DateTime.Now
                };

                _context.ShareTransfers.Add(shareTransfer);
                await _context.SaveChangesAsync();

                // Create blockchain transaction
                var blockchainData = new
                {
                    TransferNo = transferNo,
                    TransferorMemberNo = dto.TransferorMemberNo,
                    TransferorName = $"{transferor.Surname} {transferor.OtherNames}",
                    TransfereeMemberNo = dto.TransfereeMemberNo,
                    TransfereeName = $"{transferee.Surname} {transferee.OtherNames}",
                    SharesCode = dto.SharesCode,
                    SharesType = shareType.SharesType,
                    NumberOfShares = dto.NumberOfShares,
                    PricePerShare = dto.PricePerShare,
                    TotalAmount = totalAmount,
                    TransferFee = transferFee,
                    StampDuty = stampDuty,
                    TotalCharges = totalCharges,
                    TransferDate = dto.TransferDate,
                    TransferType = dto.TransferType,
                    Remarks = dto.Remarks
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    "SHARE_TRANSFER",
                    dto.TransferorMemberNo,
                    companyCode,
                    totalAmount,
                    transferNo,
                    blockchainData);

                if (blockchainTx != null)
                {
                    shareTransfer.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                _logger.LogInformation($"Share transfer {transferNo} completed successfully from {dto.TransferorMemberNo} to {dto.TransfereeMemberNo}");
                TempData["SuccessMessage"] = $"Share transfer {transferNo} created successfully!";

                return RedirectToAction("Details", new { id = shareTransfer.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating share transfer");
                ModelState.AddModelError("", ex.Message);

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var shareTypes = await _context.Sharetypes
                    .Where(s => s.CompanyCode == companyCode)
                    .ToListAsync();
                ViewBag.ShareTypes = shareTypes;
                return View(dto);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var transfer = await _context.ShareTransfers
                    .Include(t => t.Transferor)
                    .Include(t => t.Transferee)
                    .Include(t => t.ShareType)
                    .Include(t => t.Approvals)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (transfer == null)
                {
                    TempData["ErrorMessage"] = "Share transfer not found";
                    return RedirectToAction("Index");
                }

                return View(transfer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading share transfer details");
                TempData["ErrorMessage"] = "Error loading details";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMemberShares(string memberNo, string sharesCode)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var share = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == memberNo && s.Sharescode == sharesCode && s.CompanyCode == companyCode);

                var balance = share?.TotalShares ?? 0;
                return Json(new { success = true, balance });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting member shares");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<string> GenerateTransferNumberAsync(string companyCode)
        {
            var prefix = "SHT";
            var date = DateTime.Now.ToString("yyyyMMdd");
            var sequence = 1;

            var lastTransfer = await _context.ShareTransfers
                .Where(t => t.CompanyCode == companyCode && t.TransferNo.StartsWith($"{prefix}{date}"))
                .OrderByDescending(t => t.TransferNo)
                .FirstOrDefaultAsync();

            if (lastTransfer != null && lastTransfer.TransferNo.Length > 11)
            {
                var sequenceStr = lastTransfer.TransferNo.Substring(11);
                if (int.TryParse(sequenceStr, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{date}{sequence:D4}";
        }
    }
}