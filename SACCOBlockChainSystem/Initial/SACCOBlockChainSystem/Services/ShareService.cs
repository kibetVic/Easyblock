using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Repositories;

namespace SACCOBlockChainSystem.Services
{
    public class ShareService : IShareService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly IMemberRepository _memberRepository;
        //private readonly IAuditService _auditService;
        private readonly ILogger<ShareService> _logger;
        private readonly ICompanyContextService _companyContextService;

        public ShareService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            IMemberRepository memberRepository,
            //IAuditService auditService,
            ICompanyContextService companyContextService,
            ILogger<ShareService> logger)
        {
            _context = context;
            _blockchainService = blockchainService;
            _memberRepository = memberRepository;
            //_auditService = auditService;
            _companyContextService = companyContextService;
            _logger = logger;
        }

        public async Task<SharePurchaseResponseDTO> PurchaseSharesAsync(SharePurchaseDTO purchase)
        {
            try
            {
                // Validate member exists
                var member = await _memberRepository.GetByMemberNoAsync(purchase.MemberNo);
                if (member == null)
                    throw new Exception($"Member {purchase.MemberNo} not found");

                // Get or create share record
                var share = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == purchase.MemberNo && s.Sharescode == purchase.ShareType);

                if (share == null)
                {
                    share = new Share
                    {
                        MemberNo = purchase.MemberNo,
                        Sharescode = purchase.ShareType,
                        TotalShares = purchase.Amount,
                        TransDate = DateTime.Now,
                        LastDivDate = null,
                        AuditId = purchase.ProcessedBy ?? "SYSTEM",
                        AuditTime = DateTime.Now,
                        Initshares = purchase.Amount,
                        CompanyCode = member.CompanyCode ?? "DEFAULT",
                        AuditDateTime = DateTime.Now
                    };
                    _context.Shares.Add(share);
                }
                else
                {
                    share.TotalShares += purchase.Amount;
                    share.AuditTime = DateTime.Now;
                    share.AuditDateTime = DateTime.Now;
                }

                // Create ContribShare record
                var contribShare = new ContribShare
                {
                    MemberNo = purchase.MemberNo,
                    ContrDate = DateTime.Now,
                    ShareCapitalAmount = purchase.Amount,
                    CompanyCode = member.CompanyCode,
                    ReceiptNo = purchase.ReceiptNo ?? GenerateReceiptNumber(),
                    Remarks = purchase.Remarks ?? "Share purchase",
                    AuditId = purchase.ProcessedBy ?? "SYSTEM",
                    AuditTime = DateTime.Now,
                    Sharescode = purchase.ShareType,
                    TransactionNo = GenerateTransactionNumber(),
                    AuditDateTime = DateTime.Now
                };

                // Create blockchain transaction
                var blockchainData = new
                {
                    MemberNo = purchase.MemberNo,
                    ShareType = purchase.ShareType,
                    Amount = purchase.Amount,
                    ReceiptNo = contribShare.ReceiptNo,
                    TotalSharesAfter = share.TotalShares,
                    PurchaseDate = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateTransaction(
                    "SHARE_PURCHASE",
                    purchase.MemberNo,
                    member.CompanyCode,
                    purchase.Amount,
                    contribShare.Id.ToString(),
                    blockchainData
                );

                contribShare.BlockchainTxId = blockchainTx.TransactionId;

                // Update member's total share capital
                member.ShareCap = (member.ShareCap ?? 0) + purchase.Amount;
                member.AuditTime = DateTime.Now;
                member.AuditDateTime = DateTime.Now;

                // Save all changes
                _context.ContribShares.Add(contribShare);
                await _context.SaveChangesAsync();

                // Add to blockchain
                await _blockchainService.AddToBlockchain(blockchainTx);

                //// Log audit trail
                //await _auditService.LogActivityAsync(
                //    "Shares",
                //    purchase.MemberNo,
                //    "UPDATE",
                //    $"Previous shares: {(share.TotalShares - purchase.Amount)}",
                //    $"New shares: {share.TotalShares}",
                //    purchase.ProcessedBy ?? "SYSTEM",
                //    purchase.ProcessedBy ?? "SYSTEM"
                //);

                return new SharePurchaseResponseDTO
                {
                    Success = true,
                    ReceiptNo = contribShare.ReceiptNo,
                    Amount = purchase.Amount,
                    ShareType = purchase.ShareType,
                    TotalShares = share.TotalShares ?? 0,
                    BlockchainTxId = blockchainTx.TransactionId,
                    PurchaseDate = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing share purchase");
                throw;
            }
        }

        public async Task<List<Share>> GetMemberSharesAsync(string memberNo)
        {
            return await _context.Shares
                .Where(s => s.MemberNo == memberNo)
                .OrderBy(s => s.Sharescode)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalSharesValueAsync(string memberNo)
        {
            return await _context.Shares
                .Where(s => s.MemberNo == memberNo)
                .SumAsync(s => s.TotalShares ?? 0);
        }

        public async Task<bool> TransferSharesAsync(ShareTransferDTO transfer)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get company code
                var companyCode = _companyContextService.GetCurrentCompanyCode();

                // Log incoming data for debugging
                _logger.LogInformation($"TransferSharesAsync - Transferor: {transfer.TransferorMemberNo}, Transferee: {transfer.TransfereeMemberNo}, Shares: {transfer.NumberOfShares}, Price: {transfer.PricePerShare}");

                // Validate source member exists and has enough shares
                var transferor = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == transfer.TransferorMemberNo && m.CompanyCode == companyCode);

                if (transferor == null)
                {
                    throw new InvalidOperationException($"Transferor member {transfer.TransferorMemberNo} not found");
                }

                if (transferor.Withdrawn == true)
                {
                    throw new InvalidOperationException($"Transferor member {transfer.TransferorMemberNo} has already withdrawn");
                }

                // Get source member shares
                var sourceShare = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == transfer.TransferorMemberNo &&
                                              s.Sharescode == transfer.SharesCode &&
                                              s.CompanyCode == companyCode);

                var sourceSharesAvailable = sourceShare?.TotalShares ?? 0;

                if (sourceSharesAvailable < transfer.NumberOfShares)
                {
                    throw new InvalidOperationException($"Insufficient shares. Available: {sourceSharesAvailable}, Requested: {transfer.NumberOfShares}");
                }

                // Validate destination member exists
                var transferee = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == transfer.TransfereeMemberNo && m.CompanyCode == companyCode);

                if (transferee == null)
                {
                    throw new InvalidOperationException($"Transferee member {transfer.TransfereeMemberNo} not found");
                }

                if (transferee.Withdrawn == true)
                {
                    throw new InvalidOperationException($"Transferee member {transfer.TransfereeMemberNo} has already withdrawn");
                }

                // Check if transferor and transferee are the same
                if (transfer.TransferorMemberNo == transfer.TransfereeMemberNo)
                {
                    throw new InvalidOperationException("Transferor and Transferee cannot be the same member");
                }

                // Get or create destination share record
                var destShare = await _context.Shares
                    .FirstOrDefaultAsync(s => s.MemberNo == transfer.TransfereeMemberNo &&
                                              s.Sharescode == transfer.SharesCode &&
                                              s.CompanyCode == companyCode);

                // Get share type details
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(s => s.SharesCode == transfer.SharesCode && s.CompanyCode == companyCode);

                if (shareType == null)
                {
                    throw new InvalidOperationException($"Share type {transfer.SharesCode} not found");
                }

                // Calculate fees
                var totalAmount = transfer.NumberOfShares * transfer.PricePerShare;
                var transferFee = totalAmount * 0.01m; // 1% transfer fee
                var stampDuty = totalAmount * 0.005m; // 0.5% stamp duty
                var totalCharges = transferFee + stampDuty;

                // Update source shares (deduct)
                if (sourceShare != null)
                {
                    sourceShare.TotalShares = sourceShare.TotalShares - transfer.NumberOfShares;
                    sourceShare.AuditId = transfer.ProcessedBy ?? "SYSTEM";
                    sourceShare.AuditTime = DateTime.Now;
                    sourceShare.AuditDateTime = DateTime.Now;
                    sourceShare.TransDate = DateTime.Now;
                }

                // Update or create destination shares (add)
                if (destShare == null)
                {
                    destShare = new Share
                    {
                        MemberNo = transfer.TransfereeMemberNo,
                        Sharescode = transfer.SharesCode,
                        TotalShares = transfer.NumberOfShares,
                        TransDate = DateTime.Now,
                        AuditId = transfer.ProcessedBy ?? "SYSTEM",
                        AuditTime = DateTime.Now,
                        Initshares = transfer.NumberOfShares,
                        CompanyCode = companyCode,
                        AuditDateTime = DateTime.Now
                    };
                    _context.Shares.Add(destShare);
                }
                else
                {
                    destShare.TotalShares = destShare.TotalShares + transfer.NumberOfShares;
                    destShare.AuditId = transfer.ProcessedBy ?? "SYSTEM";
                    destShare.AuditTime = DateTime.Now;
                    destShare.AuditDateTime = DateTime.Now;
                    destShare.TransDate = DateTime.Now;
                }

                // Create transfer record in ShareTransfers table
                var transferNo = await GenerateTransferNumberAsync(companyCode);

                var shareTransfer = new ShareTransfer
                {
                    TransferNo = transferNo,
                    TransferorMemberNo = transfer.TransferorMemberNo,
                    TransfereeMemberNo = transfer.TransfereeMemberNo,
                    CompanyCode = companyCode,
                    SharesCode = transfer.SharesCode,
                    NumberOfShares = transfer.NumberOfShares,
                    PricePerShare = transfer.PricePerShare,
                    TotalAmount = totalAmount,
                    TransferDate = transfer.TransferDate,
                    TransferType = transfer.TransferType,
                    Status = "Completed", // Direct transfer without approval workflow
                    TransferorBalanceBefore = sourceSharesAvailable,
                    TransferorBalanceAfter = sourceSharesAvailable - transfer.NumberOfShares,
                    TransfereeBalanceBefore = (destShare?.TotalShares ?? 0) - transfer.NumberOfShares,
                    TransfereeBalanceAfter = destShare?.TotalShares ?? 0,
                    PaymentMethod = transfer.PaymentMethod,
                    PaymentReference = transfer.PaymentReference,
                    TransferFee = transferFee,
                    StampDuty = stampDuty,
                    OtherCharges = 0,
                    TotalCharges = totalCharges,
                    TransferDocumentPath = transfer.TransferDocumentPath,
                    Remarks = transfer.Remarks,
                    CreatedBy = transfer.ProcessedBy ?? "SYSTEM",
                    CreatedAt = DateTime.Now,
                    ModifiedBy = transfer.ProcessedBy ?? "SYSTEM",
                    ModifiedAt = DateTime.Now
                };

                _context.ShareTransfers.Add(shareTransfer);

                // Create blockchain transaction
                var blockchainData = new
                {
                    TransferNo = transferNo,
                    TransferorMemberNo = transfer.TransferorMemberNo,
                    TransferorName = $"{transferor.Surname} {transferor.OtherNames}",
                    TransfereeMemberNo = transfer.TransfereeMemberNo,
                    TransfereeName = $"{transferee.Surname} {transferee.OtherNames}",
                    SharesCode = transfer.SharesCode,
                    SharesType = shareType.SharesType,
                    NumberOfShares = transfer.NumberOfShares,
                    PricePerShare = transfer.PricePerShare,
                    TotalAmount = totalAmount,
                    TransferFee = transferFee,
                    StampDuty = stampDuty,
                    TotalCharges = totalCharges,
                    TransferDate = transfer.TransferDate,
                    TransferType = transfer.TransferType,
                    Remarks = transfer.Remarks
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    "SHARE_TRANSFER",
                    transfer.TransferorMemberNo,
                    companyCode,
                    totalAmount,
                    transferNo,
                    blockchainData
                );

                // Update transfer with blockchain ID
                if (blockchainTx != null)
                {
                    shareTransfer.BlockchainTxId = blockchainTx.TransactionId;
                }

                // Save all changes
                await _context.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                _logger.LogInformation($"Share transfer {transferNo} completed successfully from {transfer.TransferorMemberNo} to {transfer.TransfereeMemberNo}");

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error transferring shares from {transfer?.TransferorMemberNo} to {transfer?.TransfereeMemberNo}");
                throw;
            }
        }

        // Helper method to generate transfer number
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

        public async Task<DividendDistributionResponseDTO> DistributeDividendsAsync(DividendDistributionDTO distribution)
        {
            try
            {
                var totalDividends = 0m;
                var membersProcessed = 0;

                // Get all active members with shares
                var membersWithShares = await _context.Members
                    .Where(m => m.Status == 1)
                    .Join(_context.Shares,
                        m => m.MemberNo,
                        s => s.MemberNo,
                        (m, s) => new { Member = m, Share = s })
                    .ToListAsync();

                // Group by member
                var memberGroups = membersWithShares
                    .GroupBy(x => x.Member.MemberNo)
                    .Select(g => new
                    {
                        MemberNo = g.Key,
                        Member = g.First().Member,
                        TotalShares = g.Sum(x => x.Share.TotalShares ?? 0)
                    })
                    .Where(x => x.TotalShares > 0)
                    .ToList();

                foreach (var memberGroup in memberGroups)
                {
                    // Calculate dividend for this member
                    var dividend = memberGroup.TotalShares * (distribution.DividendRate / 100);
                    totalDividends += dividend;
                    membersProcessed++;

                    // Update member's share record (add dividend as additional shares)
                    var share = await _context.Shares
                        .FirstOrDefaultAsync(s => s.MemberNo == memberGroup.MemberNo && s.Sharescode == "DIV01");

                    if (share == null)
                    {
                        share = new Share
                        {
                            MemberNo = memberGroup.MemberNo,
                            Sharescode = "DIV01",
                            TotalShares = dividend,
                            TransDate = DateTime.Now,
                            LastDivDate = DateTime.Now,
                            AuditId = distribution.ProcessedBy ?? "SYSTEM",
                            AuditTime = DateTime.Now,
                            Initshares = dividend,
                            CompanyCode = memberGroup.Member.CompanyCode ?? "DEFAULT",
                            AuditDateTime = DateTime.Now
                        };
                        _context.Shares.Add(share);
                    }
                    else
                    {
                        share.TotalShares += dividend;
                        share.LastDivDate = DateTime.Now;
                        share.AuditTime = DateTime.Now;
                        share.AuditDateTime = DateTime.Now;
                    }

                    // Create blockchain transaction for each member
                    var blockchainData = new
                    {
                        MemberNo = memberGroup.MemberNo,
                        DividendRate = distribution.DividendRate,
                        TotalShares = memberGroup.TotalShares,
                        DividendAmount = dividend,
                        DistributionDate = DateTime.Now
                    };

                    var blockchainTx = await _blockchainService.CreateTransaction(
                        "DIVIDEND_DISTRIBUTION",
                        memberGroup.MemberNo,
                        memberGroup.Member.CompanyCode,
                        dividend,
                        $"DIV_{DateTime.Now:yyyyMMdd}_{memberGroup.MemberNo}",
                        blockchainData
                    );

                    // Add to blockchain
                    await _blockchainService.AddToBlockchain(blockchainTx);
                }

                await _context.SaveChangesAsync();

                // Log audit trail
                //await _auditService.LogActivityAsync(
                //    "Shares",
                //    "ALL",
                //    "DIVIDEND_DISTRIBUTION",
                //    null,
                //    $"Dividend distribution at {distribution.DividendRate}% to {membersProcessed} members. Total: {totalDividends}",
                //    distribution.ProcessedBy ?? "SYSTEM",
                //    distribution.ProcessedBy ?? "SYSTEM"
                //);

                return new DividendDistributionResponseDTO
                {
                    Success = true,
                    TotalDividends = totalDividends,
                    MembersProcessed = membersProcessed,
                    DividendRate = distribution.DividendRate,
                    DistributionDate = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error distributing dividends");
                throw;
            }
        }

        private string GenerateTransactionNumber()
        {
            return $"SH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        private string GenerateReceiptNumber()
        {
            return $"SR{DateTime.Now:yyyyMMdd}{new Random().Next(10000, 99999)}";
        }
    }
}