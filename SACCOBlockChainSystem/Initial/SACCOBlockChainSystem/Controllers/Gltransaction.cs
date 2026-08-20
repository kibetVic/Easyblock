using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GLController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GLController> _logger;
        private readonly AuditTrailService _auditService;

        public GLController(
            ApplicationDbContext context,
            ILogger<GLController> logger,
            AuditTrailService auditService)
        {
            _context = context;
            _logger = logger;
            _auditService = auditService;
        }

        // GET: api/GL/transactions
        [HttpGet("transactions")]
        public async Task<ActionResult<IEnumerable<Gltransaction>>> GetGLTransactions(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? accountNo = null,
            [FromQuery] string? documentNo = null,
            [FromQuery] string? source = null,
            [FromQuery] string? companyCode = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var query = _context.Gltransactions.AsQueryable();

                // Apply filters
                if (startDate.HasValue)
                {
                    query = query.Where(t => t.TransDate >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.TransDate <= endDate.Value);
                }

                if (!string.IsNullOrEmpty(accountNo))
                {
                    query = query.Where(t => t.DrAccNo == accountNo || t.CrAccNo == accountNo);
                }

                if (!string.IsNullOrEmpty(documentNo))
                {
                    query = query.Where(t => t.DocumentNo.Contains(documentNo));
                }

                if (!string.IsNullOrEmpty(source))
                {
                    query = query.Where(t => t.Source == source);
                }

                if (!string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(t => t.CompanyCode == companyCode);
                }

                // Get total count for pagination
                var totalCount = await query.CountAsync();

                // Apply pagination
                var transactions = await query
                    .OrderByDescending(t => t.TransDate)
                    .ThenByDescending(t => t.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Return with pagination metadata
                var result = new
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                    Data = transactions
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/GL/transactions/{id}
        [HttpGet("transactions/{id}")]
        public async Task<ActionResult<Gltransaction>> GetGLTransaction(long id)
        {
            try
            {
                var transaction = await _context.Gltransactions.FindAsync(id);

                if (transaction == null)
                {
                    return NotFound($"GL transaction with ID {id} not found.");
                }

                return Ok(transaction);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // POST: api/GL/transactions
        [HttpPost("transactions")]
        public async Task<ActionResult<Gltransaction>> CreateGLTransaction([FromBody] CreateGLTransactionDto transactionDto)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Create new transaction
                var transaction = new Gltransaction
                {
                    TransDate = transactionDto.TransDate,
                    Amount = transactionDto.Amount,
                    DrAccNo = transactionDto.DrAccNo,
                    CrAccNo = transactionDto.CrAccNo,
                    Temp = transactionDto.Temp,
                    DocumentNo = transactionDto.DocumentNo,
                    Source = transactionDto.Source,
                    CompanyCode = transactionDto.CompanyCode,
                    TransDescript = transactionDto.TransDescript,
                    AuditTime = DateTime.Now,
                    AuditId = transactionDto.AuditId ?? "SYSTEM",
                    Cash = transactionDto.Cash,
                    DocPosted = transactionDto.DocPosted,
                    ChequeNo = transactionDto.ChequeNo,
                    Dregard = transactionDto.Dregard,
                    Recon = transactionDto.Recon,
                    TransactionNo = transactionDto.TransactionNo,
                    Module = transactionDto.Module,
                    ReconId = transactionDto.ReconId,
                    AuditDateTime = DateTime.Now
                };

                // Validate business rules
                var validationResult = ValidateGLTransaction(transaction);
                if (!validationResult.IsValid)
                {
                    return BadRequest(new { errors = validationResult.Errors });
                }

                _context.Gltransactions.Add(transaction);
                await _context.SaveChangesAsync();

                // ============================================================
                // SAVE AUDIT TRAIL FOR GL TRANSACTION
                // ============================================================

                // Get user details for audit
                var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? transactionDto.AuditId ?? "SYSTEM";
                var userName = User?.Identity?.Name ?? transactionDto.AuditId ?? "SYSTEM";

                // Audit Extra Data
                var auditExtraData = new
                {
                    glTransactionId = transaction.Id,
                    transDate = transaction.TransDate,
                    amount = transaction.Amount,
                    drAccountNo = transaction.DrAccNo,
                    crAccountNo = transaction.CrAccNo,
                    temp = transaction.Temp,
                    documentNo = transaction.DocumentNo,
                    source = transaction.Source,
                    companyCode = transaction.CompanyCode,
                    transDescription = transaction.TransDescript,
                    cash = transaction.Cash,
                    docPosted = transaction.DocPosted,
                    chequeNo = transaction.ChequeNo,
                    transactionNo = transaction.TransactionNo,
                    module = transaction.Module,
                    createdBy = transaction.AuditId,
                    createdDate = transaction.AuditTime,
                    ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent = Request.Headers["User-Agent"].ToString()
                };

                // GL Transaction for Audit (NewValue)
                var glForAudit = new
                {
                    transaction.Id,
                    transaction.TransDate,
                    transaction.Amount,
                    transaction.DrAccNo,
                    transaction.CrAccNo,
                    transaction.Temp,
                    transaction.DocumentNo,
                    transaction.Source,
                    transaction.CompanyCode,
                    transaction.TransDescript,
                    transaction.AuditId,
                    transaction.AuditTime,
                    transaction.Cash,
                    transaction.DocPosted,
                    transaction.ChequeNo,
                    transaction.Dregard,
                    transaction.Recon,
                    transaction.TransactionNo,
                    transaction.Module,
                    transaction.ReconId,
                    transaction.AuditDateTime,
                    transaction.BlockchainTxId
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: glForAudit,
                    tableName: "Gltransactions",
                    recordId: transaction.Id.ToString(),
                    userId: userId,
                    userName: userName,
                    companyCode: transaction.CompanyCode,
                    module: "GLManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: transaction.BlockchainTxId
                );

                _logger.LogInformation($"GL Transaction audit completed for ID: {transaction.Id}, Document: {transaction.DocumentNo}");

                return CreatedAtAction(
                    nameof(GetGLTransaction),
                    new { id = transaction.Id },
                    transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating GL transaction");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        // POST: api/GL/transactions/batch
        [HttpPost("transactions/batch")]
        public async Task<ActionResult> CreateGLTransactionsBatch([FromBody] List<CreateGLTransactionDto> transactionDtos)
        {
            try
            {
                if (transactionDtos == null || !transactionDtos.Any())
                {
                    return BadRequest("No transactions provided.");
                }

                var transactions = new List<Gltransaction>();
                var errors = new List<string>();
                var auditTime = DateTime.Now;

                // Get user details for audit
                var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM";
                var userName = User?.Identity?.Name ?? "SYSTEM";

                for (int i = 0; i < transactionDtos.Count; i++)
                {
                    var dto = transactionDtos[i];

                    // Basic validation
                    if (dto.Amount <= 0)
                    {
                        errors.Add($"Transaction at index {i}: Amount must be greater than 0");
                        continue;
                    }

                    if (string.IsNullOrEmpty(dto.DrAccNo) || string.IsNullOrEmpty(dto.CrAccNo))
                    {
                        errors.Add($"Transaction at index {i}: Both debit and credit accounts are required");
                        continue;
                    }

                    var transaction = new Gltransaction
                    {
                        TransDate = dto.TransDate,
                        Amount = dto.Amount,
                        DrAccNo = dto.DrAccNo,
                        CrAccNo = dto.CrAccNo,
                        Temp = dto.Temp,
                        DocumentNo = dto.DocumentNo,
                        Source = dto.Source,
                        CompanyCode = dto.CompanyCode,
                        TransDescript = dto.TransDescript,
                        AuditTime = auditTime,
                        AuditId = dto.AuditId ?? "SYSTEM",
                        Cash = dto.Cash,
                        DocPosted = dto.DocPosted,
                        ChequeNo = dto.ChequeNo,
                        Dregard = dto.Dregard,
                        Recon = dto.Recon,
                        TransactionNo = dto.TransactionNo,
                        Module = dto.Module,
                        ReconId = dto.ReconId,
                        AuditDateTime = auditTime
                    };

                    transactions.Add(transaction);
                }

                if (errors.Any())
                {
                    return BadRequest(new
                    {
                        message = "Some transactions failed validation",
                        errors = errors,
                        validTransactionsCount = transactions.Count
                    });
                }

                await _context.Gltransactions.AddRangeAsync(transactions);
                await _context.SaveChangesAsync();

                // ============================================================
                // SAVE AUDIT TRAIL FOR BATCH GL TRANSACTIONS
                // ============================================================

                // Get the first transaction's company code (assuming all are same for batch)
                var companyCode = transactions.FirstOrDefault()?.CompanyCode ?? "UNKNOWN";

                // Audit Extra Data for Batch
                var batchAuditExtraData = new
                {
                    batchId = Guid.NewGuid().ToString(),
                    totalTransactions = transactions.Count,
                    totalAmount = transactions.Sum(t => t.Amount),
                    documentNos = transactions.Select(t => t.DocumentNo).Distinct().ToList(),
                    sources = transactions.Select(t => t.Source).Distinct().ToList(),
                    transactionNos = transactions.Select(t => t.TransactionNo).Distinct().ToList(),
                    createdBy = transactions.FirstOrDefault()?.AuditId ?? "SYSTEM",
                    createdDate = auditTime,
                    ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent = Request.Headers["User-Agent"].ToString(),
                    transactionIds = transactions.Select(t => t.Id).ToList(),
                    transactionSummaries = transactions.Select(t => new
                    {
                        t.Id,
                        t.DocumentNo,
                        t.Amount,
                        t.DrAccNo,
                        t.CrAccNo,
                        t.Source,
                        t.TransDescript
                    }).ToList()
                };

                // Batch GL Transactions for Audit
                var batchForAudit = new
                {
                    batchId = Guid.NewGuid().ToString(),
                    transactionCount = transactions.Count,
                    totalAmount = transactions.Sum(t => t.Amount),
                    transactions = transactions.Select(t => new
                    {
                        t.Id,
                        t.TransDate,
                        t.Amount,
                        t.DrAccNo,
                        t.CrAccNo,
                        t.Temp,
                        t.DocumentNo,
                        t.Source,
                        t.CompanyCode,
                        t.TransDescript,
                        t.AuditId,
                        t.AuditTime,
                        t.Cash,
                        t.DocPosted,
                        t.ChequeNo,
                        t.TransactionNo,
                        t.Module
                    }).ToList(),
                    createdBy = userId,
                    createdDate = auditTime
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,
                    newModel: batchForAudit,
                    tableName: "Gltransactions",
                    recordId: $"BATCH-{DateTime.Now:yyyyMMddHHmmss}",
                    userId: userId,
                    userName: userName,
                    companyCode: companyCode,
                    module: "GLManagement",
                    extraData: System.Text.Json.JsonSerializer.Serialize(batchAuditExtraData),
                    blockchainTxId: null
                );

                // Also create individual audit records for each transaction for detailed tracking
                foreach (var transaction in transactions)
                {
                    var individualAuditExtraData = new
                    {
                        glTransactionId = transaction.Id,
                        transDate = transaction.TransDate,
                        amount = transaction.Amount,
                        drAccountNo = transaction.DrAccNo,
                        crAccountNo = transaction.CrAccNo,
                        temp = transaction.Temp,
                        documentNo = transaction.DocumentNo,
                        source = transaction.Source,
                        companyCode = transaction.CompanyCode,
                        transDescription = transaction.TransDescript,
                        cash = transaction.Cash,
                        docPosted = transaction.DocPosted,
                        chequeNo = transaction.ChequeNo,
                        transactionNo = transaction.TransactionNo,
                        module = transaction.Module,
                        batchCreated = true,
                        batchTime = auditTime,
                        createdBy = transaction.AuditId,
                        ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                    };

                    var individualGlForAudit = new
                    {
                        transaction.Id,
                        transaction.TransDate,
                        transaction.Amount,
                        transaction.DrAccNo,
                        transaction.CrAccNo,
                        transaction.Temp,
                        transaction.DocumentNo,
                        transaction.Source,
                        transaction.CompanyCode,
                        transaction.TransDescript,
                        transaction.AuditId,
                        transaction.AuditTime,
                        transaction.Cash,
                        transaction.DocPosted,
                        transaction.ChequeNo,
                        transaction.Dregard,
                        transaction.Recon,
                        transaction.TransactionNo,
                        transaction.Module,
                        transaction.ReconId,
                        transaction.AuditDateTime,
                        transaction.BlockchainTxId,
                        BatchId = batchAuditExtraData.batchId
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        oldModel: null,
                        newModel: individualGlForAudit,
                        tableName: "Gltransactions",
                        recordId: transaction.Id.ToString(),
                        userId: userId,
                        userName: userName,
                        companyCode: transaction.CompanyCode,
                        module: "GLManagement",
                        extraData: System.Text.Json.JsonSerializer.Serialize(individualAuditExtraData),
                        blockchainTxId: transaction.BlockchainTxId
                    );
                }

                _logger.LogInformation($"Batch GL Transactions audit completed. Total transactions: {transactions.Count}");

                return Ok(new
                {
                    message = "Batch transactions created successfully",
                    count = transactions.Count,
                    auditTime = auditTime,
                    transactionIds = transactions.Select(t => t.Id).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating batch GL transactions");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        //// POST: api/GL/transactions
        //[HttpPost("transactions")]
        //public async Task<ActionResult<Gltransaction>> CreateGLTransaction([FromBody] CreateGLTransactionDto transactionDto)
        //{
        //    try
        //    {
        //        // Validate the request
        //        if (!ModelState.IsValid)
        //        {
        //            return BadRequest(ModelState);
        //        }

        //        // Create new transaction
        //        var transaction = new Gltransaction
        //        {
        //            TransDate = transactionDto.TransDate,
        //            Amount = transactionDto.Amount,
        //            DrAccNo = transactionDto.DrAccNo,
        //            CrAccNo = transactionDto.CrAccNo,
        //            Temp = transactionDto.Temp,
        //            DocumentNo = transactionDto.DocumentNo,
        //            Source = transactionDto.Source,
        //            CompanyCode = transactionDto.CompanyCode,
        //            TransDescript = transactionDto.TransDescript,
        //            AuditTime = DateTime.Now,
        //            AuditId = transactionDto.AuditId ?? "SYSTEM",
        //            Cash = transactionDto.Cash,
        //            DocPosted = transactionDto.DocPosted,
        //            ChequeNo = transactionDto.ChequeNo,
        //            Dregard = transactionDto.Dregard,
        //            Recon = transactionDto.Recon,
        //            TransactionNo = transactionDto.TransactionNo,
        //            Module = transactionDto.Module,
        //            ReconId = transactionDto.ReconId,
        //            AuditDateTime = DateTime.Now
        //        };

        //        // Validate business rules
        //        var validationResult = ValidateGLTransaction(transaction);
        //        if (!validationResult.IsValid)
        //        {
        //            return BadRequest(new { errors = validationResult.Errors });
        //        }

        //        _context.Gltransactions.Add(transaction);
        //        await _context.SaveChangesAsync();

        //        return CreatedAtAction(
        //            nameof(GetGLTransaction),
        //            new { id = transaction.Id },
        //            transaction);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, $"Internal server error: {ex.Message}");
        //    }
        //}

        //// POST: api/GL/transactions/batch
        //[HttpPost("transactions/batch")]
        //public async Task<ActionResult> CreateGLTransactionsBatch([FromBody] List<CreateGLTransactionDto> transactionDtos)
        //{
        //    try
        //    {
        //        if (transactionDtos == null || !transactionDtos.Any())
        //        {
        //            return BadRequest("No transactions provided.");
        //        }

        //        var transactions = new List<Gltransaction>();
        //        var errors = new List<string>();
        //        var auditTime = DateTime.Now;

        //        for (int i = 0; i < transactionDtos.Count; i++)
        //        {
        //            var dto = transactionDtos[i];

        //            // Basic validation
        //            if (dto.Amount <= 0)
        //            {
        //                errors.Add($"Transaction at index {i}: Amount must be greater than 0");
        //                continue;
        //            }

        //            if (string.IsNullOrEmpty(dto.DrAccNo) || string.IsNullOrEmpty(dto.CrAccNo))
        //            {
        //                errors.Add($"Transaction at index {i}: Both debit and credit accounts are required");
        //                continue;
        //            }

        //            var transaction = new Gltransaction
        //            {
        //                TransDate = dto.TransDate,
        //                Amount = dto.Amount,
        //                DrAccNo = dto.DrAccNo,
        //                CrAccNo = dto.CrAccNo,
        //                Temp = dto.Temp,
        //                DocumentNo = dto.DocumentNo,
        //                Source = dto.Source,
        //                CompanyCode = dto.CompanyCode,
        //                TransDescript = dto.TransDescript,
        //                AuditTime = auditTime,
        //                AuditId = dto.AuditId ?? "SYSTEM",
        //                Cash = dto.Cash,
        //                DocPosted = dto.DocPosted,
        //                ChequeNo = dto.ChequeNo,
        //                Dregard = dto.Dregard,
        //                Recon = dto.Recon,
        //                TransactionNo = dto.TransactionNo,
        //                Module = dto.Module,
        //                ReconId = dto.ReconId,
        //                AuditDateTime = auditTime
        //            };

        //            transactions.Add(transaction);
        //        }

        //        if (errors.Any())
        //        {
        //            return BadRequest(new
        //            {
        //                message = "Some transactions failed validation",
        //                errors = errors,
        //                validTransactionsCount = transactions.Count
        //            });
        //        }

        //        await _context.Gltransactions.AddRangeAsync(transactions);
        //        await _context.SaveChangesAsync();

        //        return Ok(new
        //        {
        //            message = "Batch transactions created successfully",
        //            count = transactions.Count,
        //            auditTime = auditTime
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, $"Internal server error: {ex.Message}");
        //    }
        //}

        // GET: api/GL/account/{accountNo}/balance


        [HttpGet("account/{accountNo}/balance")]
        public async Task<ActionResult<object>> GetAccountBalance(
            string accountNo,
            [FromQuery] DateTime? asOfDate = null)
        {
            try
            {
                var query = _context.Gltransactions
                    .Where(t => t.DrAccNo == accountNo || t.CrAccNo == accountNo);

                if (asOfDate.HasValue)
                {
                    query = query.Where(t => t.TransDate <= asOfDate.Value);
                }

                var transactions = await query.ToListAsync();

                decimal totalDebits = transactions
                    .Where(t => t.DrAccNo == accountNo)
                    .Sum(t => t.Amount);

                decimal totalCredits = transactions
                    .Where(t => t.CrAccNo == accountNo)
                    .Sum(t => t.Amount);

                decimal balance = totalDebits - totalCredits;

                return Ok(new
                {
                    AccountNo = accountNo,
                    TotalDebits = totalDebits,
                    TotalCredits = totalCredits,
                    Balance = balance,
                    AsOfDate = asOfDate ?? DateTime.Now.Date,
                    TransactionCount = transactions.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/GL/trialbalance
        [HttpGet("trialbalance")]
        public async Task<ActionResult> GetTrialBalance(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var start = startDate ?? DateTime.Now.AddMonths(-1).Date;
                var end = endDate ?? DateTime.Now.Date;

                var transactions = await _context.Gltransactions
                    .Where(t => t.TransDate >= start && t.TransDate <= end)
                    .ToListAsync();

                // Group by account
                var accountBalances = transactions
                    .SelectMany(t => new[]
                    {
                        new { AccountNo = t.DrAccNo, Amount = t.Amount, Type = "DR" },
                        new { AccountNo = t.CrAccNo, Amount = t.Amount, Type = "CR" }
                    })
                    .GroupBy(x => x.AccountNo)
                    .Select(g => new
                    {
                        AccountNo = g.Key,
                        Debits = g.Where(x => x.Type == "DR").Sum(x => x.Amount),
                        Credits = g.Where(x => x.Type == "CR").Sum(x => x.Amount),
                        Balance = g.Where(x => x.Type == "DR").Sum(x => x.Amount) -
                                 g.Where(x => x.Type == "CR").Sum(x => x.Amount)
                    })
                    .OrderBy(x => x.AccountNo)
                    .ToList();

                var totalDebits = accountBalances.Sum(x => x.Debits);
                var totalCredits = accountBalances.Sum(x => x.Credits);

                return Ok(new
                {
                    Period = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
                    Accounts = accountBalances,
                    TotalDebits = totalDebits,
                    TotalCredits = totalCredits,
                    IsBalanced = totalDebits == totalCredits,
                    Difference = totalDebits - totalCredits
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/GL/reconciliation/{reconId}
        [HttpGet("reconciliation/{reconId}")]
        public async Task<ActionResult> GetReconciliation(int reconId)
        {
            try
            {
                var transactions = await _context.Gltransactions
                    .Where(t => t.ReconId == reconId)
                    .OrderBy(t => t.TransDate)
                    .ThenBy(t => t.Id)
                    .ToListAsync();

                if (!transactions.Any())
                {
                    return NotFound($"No transactions found for reconciliation ID {reconId}");
                }

                var summary = new
                {
                    ReconciliationId = reconId,
                    TransactionCount = transactions.Count,
                    TotalAmount = transactions.Sum(t => t.Amount),
                    EarliestDate = transactions.Min(t => t.TransDate),
                    LatestDate = transactions.Max(t => t.TransDate),
                    Transactions = transactions
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // PUT: api/GL/transactions/{id}/reconcile
        [HttpPut("transactions/{id}/reconcile")]
        public async Task<ActionResult> ReconcileTransaction(long id, [FromBody] ReconcileDto reconcileDto)
        {
            try
            {
                var transaction = await _context.Gltransactions.FindAsync(id);

                if (transaction == null)
                {
                    return NotFound($"GL transaction with ID {id} not found.");
                }

                transaction.Recon = reconcileDto.Recon;
                transaction.ReconId = reconcileDto.ReconId;
                transaction.AuditDateTime = DateTime.Now;
                transaction.AuditId = reconcileDto.AuditId ?? transaction.AuditId;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Transaction reconciled successfully",
                    transactionId = id,
                    reconciled = reconcileDto.Recon,
                    reconciliationId = reconcileDto.ReconId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/GL/sources
        [HttpGet("sources")]
        public async Task<ActionResult<IEnumerable<string>>> GetTransactionSources()
        {
            try
            {
                var sources = await _context.Gltransactions
                    .Select(t => t.Source)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToListAsync();

                return Ok(sources);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/GL/modules
        [HttpGet("modules")]
        public async Task<ActionResult<IEnumerable<string>>> GetModules()
        {
            try
            {
                var modules = await _context.Gltransactions
                    .Select(t => t.Module)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToListAsync();

                return Ok(modules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Helper method for validation
        private ValidationResult ValidateGLTransaction(Gltransaction transaction)
        {
            var errors = new List<string>();

            if (transaction.Amount <= 0)
            {
                errors.Add("Amount must be greater than 0.");
            }

            if (string.IsNullOrEmpty(transaction.DrAccNo))
            {
                errors.Add("Debit account number is required.");
            }

            if (string.IsNullOrEmpty(transaction.CrAccNo))
            {
                errors.Add("Credit account number is required.");
            }

            if (transaction.DrAccNo == transaction.CrAccNo)
            {
                errors.Add("Debit and credit accounts cannot be the same.");
            }

            if (string.IsNullOrEmpty(transaction.DocumentNo))
            {
                errors.Add("Document number is required.");
            }

            if (string.IsNullOrEmpty(transaction.Source))
            {
                errors.Add("Transaction source is required.");
            }

            if (string.IsNullOrEmpty(transaction.TransDescript))
            {
                errors.Add("Transaction description is required.");
            }

            if (string.IsNullOrEmpty(transaction.Module))
            {
                errors.Add("Module is required.");
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors
            };
        }
    }

    // DTO for creating GL transactions
    public class CreateGLTransactionDto
    {
        public DateTime TransDate { get; set; }
        public decimal Amount { get; set; }
        public string DrAccNo { get; set; } = null!;
        public string CrAccNo { get; set; } = null!;
        public string Temp { get; set; } = null!;
        public string DocumentNo { get; set; } = null!;
        public string Source { get; set; } = null!;
        public string? CompanyCode { get; set; }
        public string TransDescript { get; set; } = null!;
        public string? AuditId { get; set; }
        public int Cash { get; set; }
        public int DocPosted { get; set; }
        public string? ChequeNo { get; set; }
        public bool? Dregard { get; set; }
        public bool? Recon { get; set; }
        public string? TransactionNo { get; set; }
        public string Module { get; set; } = null!;
        public int ReconId { get; set; }
    }

    // DTO for reconciliation
    public class ReconcileDto
    {
        public bool Recon { get; set; }
        public int ReconId { get; set; }
        public string? AuditId { get; set; }
    }

    // Validation result class
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}