using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using SACCOBlockChainSystem.ViewModels; // Add this namespace for ViewModels

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class BlockchainController : Controller
    {
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<BlockchainController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ICompanyContextService _companyContext;

        public BlockchainController(
            IBlockchainService blockchainService,
            ILogger<BlockchainController> logger,
            ApplicationDbContext context,
            ICompanyContextService companyContext)
        {
            _blockchainService = blockchainService;
            _logger = logger;
            _context = context;
            _companyContext = companyContext;
        }

        #region Explorer

        // GET: /Blockchain/Explorer
        public async Task<IActionResult> Explorer(int page = 1, int pageSize = 20)
        {
            try
            {
                var companyCode = _companyContext.GetCurrentCompanyCode();

                // Get blockchain status
                var status = await _blockchainService.GetBlockchainStatus();

                // Get paginated blocks
                var blocks = await _blockchainService.GetAllBlocksAsync();
                var totalBlocks = blocks.Count;

                var paginatedBlocks = blocks
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Get recent transactions
                var recentTransactions = await _context.BlockchainTransactions
                    .Where(t => t.CompanyCode == companyCode)
                    .OrderByDescending(t => t.Timestamp)
                    .Take(10)
                    .Select(t => new TransactionSummaryViewModel
                    {
                        TransactionId = t.TransactionId,
                        TransactionType = t.TransactionType,
                        MemberNo = t.MemberNo,
                        Amount = t.Amount,
                        Timestamp = t.Timestamp,
                        Status = t.Status,
                        BlockHash = t.BlockHash
                    })
                    .ToListAsync();

                var viewModel = new BlockchainExplorerViewModel
                {
                    Blocks = paginatedBlocks,
                    RecentTransactions = recentTransactions,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalBlocks = totalBlocks,
                    TotalPages = (int)Math.Ceiling(totalBlocks / (double)pageSize),
                    Status = new BlockchainStatusViewModel
                    {
                        TotalBlocks = status.TotalBlocks,
                        TotalTransactions = status.TotalTransactions,
                        PendingTransactions = status.PendingTransactions,
                        LatestBlockHash = status.LatestBlockHash,
                        LatestBlockTimestamp = status.LatestBlockTimestamp,
                        IsValid = await _blockchainService.VerifyBlockchainAsync()
                    }
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading blockchain explorer");
                return View("Error");
            }
        }

        // GET: /Blockchain/Explorer/Block/{blockHash}
        public async Task<IActionResult> BlockDetails(string blockHash)
        {
            try
            {
                var block = await _blockchainService.GetBlockAsync(blockHash);
                if (block == null)
                {
                    return NotFound();
                }

                var transactions = await _context.BlockchainTransactions
                    .Where(t => t.BlockHash == blockHash)
                    .OrderBy(t => t.Timestamp)
                    .ToListAsync();

                var viewModel = new BlockDetailsViewModel
                {
                    Block = block,
                    Transactions = transactions,
                    TransactionCount = transactions.Count,
                    TotalAmount = transactions.Sum(t => t.Amount),
                    IsValid = await ValidateBlock(block)
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading block {blockHash}");
                return View("Error");
            }
        }

        // GET: /Blockchain/Explorer/Transaction/{transactionId}
        public async Task<IActionResult> TransactionDetails(string transactionId)
        {
            try
            {
                var transaction = await _blockchainService.GetTransactionAsync(transactionId);
                if (transaction == null)
                {
                    return NotFound();
                }

                // Get related data based on transaction type
                object relatedData = null;
                switch (transaction.TransactionType)
                {
                    case "MEMBER_REGISTRATION":
                    case "MEMBER_UPDATE":
                        relatedData = await _context.Members
                            .FirstOrDefaultAsync(m => m.MemberNo == transaction.MemberNo);
                        break;
                    case "CONTRIBUTION":
                        relatedData = await _context.Contribs
                            .FirstOrDefaultAsync(c => c.TransactionNo == transaction.OffChainReferenceId);
                        break;
                    case "LOAN_DISBURSEMENT":
                    case "LOAN_REPAYMENT":
                        relatedData = await _context.Loans
                            .FirstOrDefaultAsync(l => l.LoanNo == transaction.OffChainReferenceId);
                        break;
                }

                // Parse payload data
                object payload = null;
                if (!string.IsNullOrEmpty(transaction.PayloadJson))
                {
                    try
                    {
                        payload = System.Text.Json.JsonSerializer.Deserialize<object>(transaction.PayloadJson);
                    }
                    catch { }
                }

                var viewModel = new TransactionDetailsViewModel
                {
                    Transaction = transaction,
                    RelatedData = relatedData,
                    Payload = payload,
                    Block = transaction.BlockHash != null
                        ? await _blockchainService.GetBlockAsync(transaction.BlockHash)
                        : null,
                    VerificationStatus = await _blockchainService.VerifyTransactionAsync(transactionId)
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading transaction {transactionId}");
                return View("Error");
            }
        }

        #endregion

        #region Verify

        // GET: /Blockchain/Verify
        public IActionResult Verify()
        {
            return View(new TransactionVerificationViewModel());
        }

        // POST: /Blockchain/Verify
        [HttpPost]
        public async Task<IActionResult> Verify(TransactionVerificationViewModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.TransactionId))
                {
                    ModelState.AddModelError("TransactionId", "Please enter a transaction ID");
                    return View(model);
                }

                var companyCode = _companyContext.GetCurrentCompanyCode();

                // Get transaction
                var transaction = await _blockchainService.GetTransactionAsync(model.TransactionId);

                if (transaction == null)
                {
                    model.IsValid = false;
                    model.Message = "Transaction not found in the blockchain";
                    return View(model);
                }

                // Verify transaction exists and is confirmed
                var isVerified = await _blockchainService.VerifyTransactionAsync(model.TransactionId);

                // Verify data integrity by recalculating hash
                var dataIntegrity = true;
                if (!string.IsNullOrEmpty(transaction.PayloadJson))
                {
                    var calculatedHash = await _blockchainService.GenerateTransactionHash(transaction.PayloadJson);
                    dataIntegrity = calculatedHash == transaction.DataHash;
                }

                // Verify block integrity if transaction is in a block
                var blockValid = true;
                if (!string.IsNullOrEmpty(transaction.BlockHash))
                {
                    var block = await _blockchainService.GetBlockAsync(transaction.BlockHash);
                    blockValid = block != null && block.Confirmed;
                }

                model.IsValid = isVerified && dataIntegrity && blockValid;
                model.Transaction = transaction;
                model.VerificationDetails = new VerificationDetails
                {
                    FoundInBlock = !string.IsNullOrEmpty(transaction.BlockHash),
                    BlockConfirmed = transaction.Status == "CONFIRMED",
                    DataIntegrity = dataIntegrity,
                    TimestampValid = transaction.Timestamp <= DateTime.UtcNow,
                    CalculatedHash = dataIntegrity ? transaction.DataHash : "MISMATCH"
                };

                model.Message = model.IsValid
                    ? "✓ Transaction verified successfully. The transaction is authentic and recorded on the blockchain."
                    : "✗ Verification failed. The transaction data may have been tampered with.";

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying transaction");
                ModelState.AddModelError("", "An error occurred during verification");
                return View(model);
            }
        }

        // GET: /Blockchain/Verify/ByReference/{referenceId}
        public async Task<IActionResult> VerifyByReference(string referenceId, string type = "member")
        {
            try
            {
                var companyCode = _companyContext.GetCurrentCompanyCode();

                var transactions = await _context.BlockchainTransactions
                    .Where(t => t.TransactionId == referenceId && t.CompanyCode == companyCode)
                    .OrderByDescending(t => t.Timestamp)
                    .ToListAsync();

                if (!transactions.Any())
                {
                    return NotFound();
                }

                var viewModel = new ReferenceVerificationViewModel
                {
                    ReferenceId = referenceId,
                    ReferenceType = type,
                    Transactions = transactions,
                    TotalTransactions = transactions.Count,
                    TotalAmount = transactions.Sum(t => t.Amount),
                    AllVerified = true
                };

                foreach (var tx in transactions)
                {
                    var verified = await _blockchainService.VerifyTransactionAsync(tx.TransactionId);
                    if (!verified)
                    {
                        viewModel.AllVerified = false;
                        break;
                    }
                }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying by reference: {referenceId}");
                return View("Error");
            }
        }

        // GET: /Blockchain/QuickVerify (for page view)
        [HttpGet("Blockchain/QuickVerify")]
        public IActionResult QuickVerify()
        {
            return View(); // Returns the view
        }

        // GET: /Blockchain/QuickVerify/{transactionId} (for API)
        [HttpGet("Blockchain/QuickVerify/{transactionId}")]
        public async Task<IActionResult> QuickVerify(string transactionId)
        {
            try
            {
                var transaction = await _blockchainService.GetTransactionAsync(transactionId);

                if (transaction == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Transaction not found"
                    });
                }

                var isVerified = await _blockchainService.VerifyTransactionAsync(transactionId);

                return Json(new
                {
                    success = true,
                    verified = isVerified,
                    transactionId = transaction.TransactionId,
                    type = transaction.TransactionType,
                    memberNo = transaction.MemberNo,
                    amount = transaction.Amount,
                    timestamp = transaction.Timestamp,
                    status = transaction.Status,
                    blockHash = transaction.BlockHash,
                    dataHash = transaction.DataHash
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in quick verify: {transactionId}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Blockchain/Verify/{transactionId}
        [HttpGet("Blockchain/Verify/{transactionId}")]
        public async Task<IActionResult> Verify(string transactionId)
        {
            try
            {
                _logger.LogInformation($"Verifying blockchain transaction: {transactionId}");

                // Get transaction from blockchain service
                var transaction = await _blockchainService.GetTransactionAsync(transactionId);

                if (transaction == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Transaction not found in blockchain"
                    });
                }

                // Verify the transaction
                var isVerified = await _blockchainService.VerifyTransactionAsync(transactionId);

                return Ok(new
                {
                    success = true,
                    transaction = new
                    {
                        transactionId = transaction.TransactionId,
                        transactionType = transaction.TransactionType,
                        memberNo = transaction.MemberNo,
                        amount = transaction.Amount,
                        timestamp = transaction.Timestamp,
                        status = transaction.Status,
                        dataHash = transaction.DataHash,
                        blockHash = transaction.BlockHash,
                        verified = isVerified
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying transaction: {transactionId}");
                return BadRequest(new
                {
                    success = false,
                    message = "Error verifying transaction",
                    error = ex.Message
                });
            }
        }

        #endregion

        #region My Transactions
        // Add this method to BlockchainController.cs
        private async Task<Dictionary<string, bool>> GetTransactionVerificationStatuses(List<BlockchainTransaction> transactions)
        {
            var statuses = new Dictionary<string, bool>();
            foreach (var tx in transactions)
            {
                var isValid = await _blockchainService.VerifyTransactionAsync(tx.TransactionId);
                statuses[tx.TransactionId] = isValid;
            }
            return statuses;
        }



        // GET: /Blockchain/MyTransactions
        public async Task<IActionResult> MyTransactions(int page = 1, int pageSize = 20, string filter = "all")
        {
            try
            {
                var companyCode = _companyContext.GetCurrentCompanyCode();
                var currentUserId = User.Identity?.Name;
                var memberNo = await GetCurrentMemberNo();

                if (string.IsNullOrEmpty(memberNo))
                {
                    return View("NoMemberAccount");
                }

                // Build query based on filter
                IQueryable<BlockchainTransaction> query = _context.BlockchainTransactions
                    .Where(t => t.CompanyCode == companyCode);

                switch (filter)
                {
                    case "my":
                        query = query.Where(t => t.MemberNo == memberNo);
                        break;
                    case "created":
                        query = query.Where(t => t.CreatedBy == currentUserId);
                        break;
                    case "pending":
                        query = query.Where(t => t.Status == "PENDING" && t.MemberNo == memberNo);
                        break;
                    default: // "all"
                        query = query.Where(t => t.MemberNo == memberNo || t.CreatedBy == currentUserId);
                        break;
                }

                var totalCount = await query.CountAsync();

                var transactions = await query
                    .OrderByDescending(t => t.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(t => new MyTransactionViewModel
                    {
                        TransactionId = t.TransactionId,
                        TransactionType = t.TransactionType,
                        MemberNo = t.MemberNo,
                        Amount = t.Amount,
                        Timestamp = t.Timestamp,
                        Status = t.Status,
                        BlockHash = t.BlockHash,
                        DataHash = t.DataHash,
                        IsVerified = t.Status == "CONFIRMED" && t.BlockHash != null,
                        YourRole = t.MemberNo == memberNo ? "Subject" : "Creator",
                        CanVerify = true
                    })
                    .ToListAsync();

                // Get statistics
                var stats = new MyTransactionsStatistics
                {
                    TotalTransactions = totalCount,
                    TotalAmount = transactions.Sum(t => t.Amount),
                    ConfirmedCount = transactions.Count(t => t.Status == "CONFIRMED"),
                    PendingCount = transactions.Count(t => t.Status == "PENDING"),
                    LastTransactionDate = transactions.FirstOrDefault()?.Timestamp,
                    MemberNo = memberNo
                };

                // Get recent activity summary
                var activitySummary = await GetActivitySummary(memberNo);

                var viewModel = new MyTransactionsViewModel
                {
                    Transactions = transactions,
                    Statistics = stats,
                    ActivitySummary = activitySummary,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                    CurrentFilter = filter,
                    MemberNo = memberNo,
                    UserName = User.Identity?.Name ?? "User"
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading my transactions");
                TempData["ErrorMessage"] = "An error occurred while loading your transactions.";
                return View("Error");
            }
        }

        // GET: /Blockchain/MyTransactions/Statement
        public async Task<IActionResult> DownloadStatement(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var companyCode = _companyContext.GetCurrentCompanyCode();
                var memberNo = await GetCurrentMemberNo();

                if (string.IsNullOrEmpty(memberNo))
                {
                    return NotFound();
                }

                var from = fromDate ?? DateTime.Now.AddMonths(-3);
                var to = toDate ?? DateTime.Now;

                var transactions = await _context.BlockchainTransactions
                    .Where(t => t.MemberNo == memberNo &&
                                t.CompanyCode == companyCode &&
                                t.Timestamp >= from &&
                                t.Timestamp <= to)
                    .OrderBy(t => t.Timestamp)
                    .ToListAsync();

                // Generate PDF statement
                var pdfBytes = await GenerateTransactionStatement(transactions, memberNo, from, to);

                return File(pdfBytes, "application/pdf",
                    $"blockchain_statement_{memberNo}_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading statement");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Blockchain/MyTransactions/AuditTrail
        [HttpGet("Blockchain/AuditTrail")] // Add this explicit route
        [HttpGet("Blockchain/MyTransactions/AuditTrail")] // Also support the original path
        public async Task<IActionResult> AuditTrail()
        {
            try
            {
                var companyCode = _companyContext.GetCurrentCompanyCode();
                var memberNo = await GetCurrentMemberNo();

                if (string.IsNullOrEmpty(memberNo))
                {
                    return NotFound();
                }

                // Get all transactions for this member in chronological order
                var transactions = await _context.BlockchainTransactions
                    .Where(t => t.MemberNo == memberNo && t.CompanyCode == companyCode)
                    .OrderBy(t => t.Timestamp)
                    .ToListAsync();

                // Get verification status for all transactions
                var verificationStatuses = await GetTransactionVerificationStatuses(transactions);

                // Verify chain integrity for member's transactions
                var chainValid = true;
                for (int i = 1; i < transactions.Count; i++)
                {
                    var isValid = await _blockchainService.VerifyTransactionAsync(transactions[i - 1].TransactionId);
                    if (!isValid)
                    {
                        chainValid = false;
                        break;
                    }
                }

                var viewModel = new AuditTrailViewModel
                {
                    MemberNo = memberNo,
                    Transactions = transactions,
                    TotalCount = transactions.Count,
                    TotalAmount = transactions.Sum(t => t.Amount),
                    FirstTransaction = transactions.FirstOrDefault(),
                    LastTransaction = transactions.LastOrDefault(),
                    ChainValid = chainValid,
                    VerificationStatuses = verificationStatuses
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading audit trail");
                return View("Error");
            }
        }

        #endregion

        #region Blocks

        // GET: /Blockchain/Blocks
        public async Task<IActionResult> Blocks()
        {
            try
            {
                var blocks = await _blockchainService.GetAllBlocksAsync();
                return View(blocks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading blocks");
                return View("Error");
            }
        }

        // GET: /Blockchain/VerifyBlock/{blockHash}
        [HttpGet("Blockchain/VerifyBlock/{blockHash}")]
        public async Task<IActionResult> VerifyBlock(string blockHash)
        {
            try
            {
                var isValid = await _blockchainService.VerifyBlockchainAsync();

                if (isValid)
                {
                    var block = await _blockchainService.GetBlockAsync(blockHash);
                    return Ok(new
                    {
                        success = true,
                        message = "Blockchain is valid",
                        block = block?.BlockHash,
                        blockId = block?.BlockId,
                        confirmed = block?.Confirmed ?? false
                    });
                }
                else
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Blockchain integrity check failed"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying blockchain");
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        #endregion

        #region Helper Methods

        // Helper method to get current member number
        private async Task<string?> GetCurrentMemberNo()
        {
            var userName = User.Identity?.Name;

            // Try to find member by email, phone, or username
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.Email == userName ||
                                          m.PhoneNo == userName ||
                                          m.UserName == userName);

            return member?.MemberNo;
        }

        // Helper method to get activity summary
        private async Task<ActivitySummaryViewModel> GetActivitySummary(string memberNo)
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var transactions = await _context.BlockchainTransactions
                .Where(t => t.MemberNo == memberNo)
                .ToListAsync();

            return new ActivitySummaryViewModel
            {
                ThisMonth = transactions.Count(t => t.Timestamp >= startOfMonth),
                LastMonth = transactions.Count(t => t.Timestamp >= startOfMonth.AddMonths(-1) && t.Timestamp < startOfMonth),
                ByType = transactions.GroupBy(t => t.TransactionType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                TotalContributions = transactions.Where(t => t.TransactionType == "CONTRIBUTION")
                    .Sum(t => t.Amount),
                TotalLoans = transactions.Where(t => t.TransactionType.StartsWith("LOAN"))
                    .Sum(t => t.Amount)
            };
        }

        // Helper method to generate PDF statement
        private async Task<byte[]> GenerateTransactionStatement(
            List<BlockchainTransaction> transactions,
            string memberNo,
            DateTime from,
            DateTime to)
        {
            // Implement PDF generation using iTextSharp or similar library
            // This is a placeholder - you'll need to add a PDF library
            throw new NotImplementedException("PDF generation not implemented yet");
        }

        // Helper method to validate block
        private async Task<bool> ValidateBlock(Block block)
        {
            try
            {
                // Recalculate block hash
                var blockData = $"{block.PreviousHash}{block.Timestamp:yyyy-MM-dd HH:mm:ss.fff}{block.MerkleRoot}{block.Nonce}";
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(blockData);
                var hashBytes = sha256.ComputeHash(bytes);
                var calculatedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                // Check if hash matches
                if (calculatedHash != block.BlockHash)
                    return false;

                // Check if block is confirmed
                if (!block.Confirmed)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}