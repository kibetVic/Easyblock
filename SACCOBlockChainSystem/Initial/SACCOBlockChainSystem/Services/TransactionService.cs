using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Repositories;

namespace SACCOBlockChainSystem.Services
{
    public class TransactionService : ITransactionService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly IMemberRepository _memberRepository;
        private readonly ITransactionRepository _transactionRepository;
        //private readonly IAuditService _auditService;
        private readonly ILogger<TransactionService> _logger;

        public TransactionService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            IMemberRepository memberRepository,
            ITransactionRepository transactionRepository,
            //IAuditService auditService,
            ILogger<TransactionService> logger)
        {
            _context = context;
            _blockchainService = blockchainService;
            _memberRepository = memberRepository;
            _transactionRepository = transactionRepository;
            //_auditService = auditService;
            _logger = logger;
        }

        public async Task<TransactionResponseDTO> ProcessDepositAsync(DepositDTO deposit)
        {
            try
            {
                // Validate member exists
                var member = await _memberRepository.GetByMemberNoAsync(deposit.MemberNo);
                if (member == null)
                    throw new Exception($"Member {deposit.MemberNo} not found");

                // Generate transaction and receipt numbers if not provided
                var transactionNo = GenerateTransactionNumber();
                var receiptNo = deposit.ReceiptNo ?? GenerateReceiptNumber();

                // Create transaction record
                var transaction = new Transactions2
                {
                    MemberNo = deposit.MemberNo,
                    Companycode = member.CompanyCode ?? "DEFAULT",
                    TransactionNo = transactionNo,
                    ReceiptNo = receiptNo,
                    PaymentMode = deposit.PaymentMode,
                    TransactionType = "DEPOSIT",
                    Amount = deposit.Amount,
                    ContributionDate = DateTime.Now,
                    DepositedDate = DateTime.Now,
                    AuditId = deposit.ProcessedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Status = "COMPLETED",
                    Contact = deposit.ContactInfo,
                    AuditDateTime = DateTime.Now
                };

                // Update member's share balance
                var share = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == deposit.MemberNo);

                decimal newBalance = deposit.Amount;
                if (share != null)
                {
                    share.TotalShares += deposit.Amount;
                    share.AuditTime = DateTime.Now;
                    share.AuditDateTime = DateTime.Now;
                    newBalance = (decimal)share.TotalShares;
                }
                else
                {
                    // Create new share record if doesn't exist
                    share = new Share
                    {
                        MemberNo = deposit.MemberNo,
                        TotalShares = deposit.Amount,
                        AuditId = deposit.ProcessedBy ?? "SYSTEM",
                        AuditTime = DateTime.Now,
                        AuditDateTime = DateTime.Now,
                        CompanyCode = member.CompanyCode
                    };
                    _context.Shares.Add(share);
                }

                // Create ContribShare record
                var contribShare = new ContribShare
                {
                    MemberNo = deposit.MemberNo,
                    ContrDate = DateTime.Now,
                    DepositedDate = DateTime.Now,
                    ReceiptDate = DateTime.Now,
                    DepositsAmount = deposit.Amount,
                    CompanyCode = member.CompanyCode,
                    ReceiptNo = receiptNo,
                    Remarks = $"Deposit via {deposit.PaymentMode}",
                    AuditId = deposit.ProcessedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Sharescode = "DEP01",
                    TransactionNo = transactionNo,
                    AuditDateTime = DateTime.Now
                };

                // Create blockchain transaction
                var blockchainData = new
                {
                    TransactionNo = transactionNo,
                    MemberNo = deposit.MemberNo,
                    Amount = deposit.Amount,
                    PaymentMode = deposit.PaymentMode,
                    Purpose = deposit.Purpose,
                    BalanceAfter = newBalance,
                    Timestamp = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateTransaction(
                    "DEPOSIT",
                    deposit.MemberNo,
                    member.CompanyCode,
                    deposit.Amount,
                    transaction.Id.ToString(),
                    blockchainData
                );

                transaction.BlockchainTxId = blockchainTx.TransactionId;
                contribShare.BlockchainTxId = blockchainTx.TransactionId;

                // Save all changes
                await _transactionRepository.AddAsync(transaction);
                _context.ContribShares.Add(contribShare);
                await _context.SaveChangesAsync();

                // Add to blockchain
                await _blockchainService.AddToBlockchain(blockchainTx);

                //// Log audit trail
                //await _auditService.LogActivityAsync(
                //    "Transactions2",
                //    transaction.Id.ToString(),
                //    "INSERT",
                //    null,
                //    $"Deposit of {deposit.Amount} for {deposit.MemberNo}",
                //    deposit.ProcessedBy ?? "SYSTEM",
                //    deposit.ProcessedBy ?? "SYSTEM"
                //);

                return new TransactionResponseDTO
                {
                    Success = true,
                    TransactionId = transactionNo,
                    ReceiptNo = receiptNo,
                    Amount = deposit.Amount,
                    NewBalance = newBalance,
                    BlockchainTxId = blockchainTx.TransactionId,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deposit");
                throw;
            }
        }

        public async Task<TransactionResponseDTO> ProcessWithdrawalAsync(WithdrawalDTO withdrawal)
        {
            try
            {
                // Validate member exists
                var member = await _memberRepository.GetByMemberNoAsync(withdrawal.MemberNo);
                if (member == null)
                    throw new Exception($"Member {withdrawal.MemberNo} not found");

                // Check sufficient balance
                var shareBalance = await _memberRepository.GetShareBalanceAsync(withdrawal.MemberNo);
                if (shareBalance < withdrawal.Amount)
                    throw new Exception($"Insufficient balance. Available: {shareBalance}, Requested: {withdrawal.Amount}");

                // Generate transaction and receipt numbers if not provided
                var transactionNo = GenerateTransactionNumber();
                var receiptNo = withdrawal.ReceiptNo ?? GenerateReceiptNumber();

                // Create transaction record
                var transaction = new Transactions2
                {
                    MemberNo = withdrawal.MemberNo,
                    Companycode = member.CompanyCode ?? "DEFAULT",
                    TransactionNo = transactionNo,
                    ReceiptNo = receiptNo,
                    PaymentMode = withdrawal.PaymentMode,
                    TransactionType = "WITHDRAWAL",
                    Amount = withdrawal.Amount,
                    ContributionDate = DateTime.Now,
                    DepositedDate = DateTime.Now,
                    AuditId = withdrawal.ProcessedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Status = "COMPLETED",
                    Contact = withdrawal.ContactInfo,
                    AuditDateTime = DateTime.Now
                };

                // Update member's share balance
                var share = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == withdrawal.MemberNo);

                if (share == null)
                    throw new Exception($"No share account found for member {withdrawal.MemberNo}");

                share.TotalShares -= withdrawal.Amount;
                share.AuditTime = DateTime.Now;
                share.AuditDateTime = DateTime.Now;

                var newBalance = share.TotalShares;

                // Create blockchain transaction
                var blockchainData = new
                {
                    TransactionNo = transactionNo,
                    MemberNo = withdrawal.MemberNo,
                    Amount = withdrawal.Amount,
                    PaymentMode = withdrawal.PaymentMode,
                    Purpose = withdrawal.Purpose,
                    BalanceAfter = newBalance,
                    Timestamp = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateTransaction(
                    "WITHDRAWAL",
                    withdrawal.MemberNo,
                    member.CompanyCode,
                    withdrawal.Amount,
                    transaction.Id.ToString(),
                    blockchainData
                );

                transaction.BlockchainTxId = blockchainTx.TransactionId;

                // Save all changes
                await _transactionRepository.AddAsync(transaction);
                await _context.SaveChangesAsync();

                // Add to blockchain
                await _blockchainService.AddToBlockchain(blockchainTx);

                //// Log audit trail
                //await _auditService.LogActivityAsync(
                //    "Transactions2",
                //    transaction.Id.ToString(),
                //    "INSERT",
                //    null,
                //    $"Withdrawal of {withdrawal.Amount} for {withdrawal.MemberNo}",
                //    withdrawal.ProcessedBy ?? "SYSTEM",
                //    withdrawal.ProcessedBy ?? "SYSTEM"
                //);

                return new TransactionResponseDTO
                {
                    Success = true,
                    TransactionId = transactionNo,
                    ReceiptNo = receiptNo,
                    Amount = withdrawal.Amount,
                    NewBalance = (decimal)newBalance,
                    BlockchainTxId = blockchainTx.TransactionId,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing withdrawal");
                throw;
            }
        }

        public async Task<List<Transaction>> GetMemberTransactionsAsync(string memberNo, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.Transactions.AsQueryable();

            if (!string.IsNullOrEmpty(memberNo))
            {
                query = query.Where(t => t.TransactionNo != null && t.TransactionNo.Contains(memberNo));
            }

            if (fromDate.HasValue)
                query = query.Where(t => t.TransDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(t => t.TransDate <= toDate.Value);

            return await query
                .OrderByDescending(t => t.TransDate)
                .ToListAsync();
        }

        public async Task<Transaction> GetTransactionByIdAsync(int transactionId)
        {
            return await _context.Transactions.FindAsync(transactionId);
        }

        public async Task<decimal> GetMemberBalanceAsync(string memberNo)
        {
            return await _memberRepository.GetShareBalanceAsync(memberNo);
        }

        public async Task<IEnumerable<Transactions2>> GetMemberTransactions2Async(string memberNo, DateTime? fromDate, DateTime? toDate)
        {
            return await _transactionRepository.GetMemberTransactionsAsync(memberNo, fromDate, toDate);
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