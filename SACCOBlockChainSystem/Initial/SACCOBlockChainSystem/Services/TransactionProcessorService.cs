using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;

namespace SACCOBlockChainSystem.Services
{
    public class TransactionProcessorService : BackgroundService
    {
        private readonly ILogger<TransactionProcessorService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public TransactionProcessorService(ILogger<TransactionProcessorService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Transaction Processor Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var blockchainService = scope.ServiceProvider.GetRequiredService<IBlockchainService>();

                    // Process pending blockchain transactions for Transactions2
                    var pendingTransactions = await context.Transactions2
                        .Where(t => string.IsNullOrEmpty(t.BlockchainTxId) && t.Status == "COMPLETED")
                        .Take(10)
                        .ToListAsync();

                    foreach (var transaction in pendingTransactions)
                    {
                        try
                        {
                            // Create blockchain transaction
                            var blockchainData = new
                            {
                                TransactionNo = transaction.TransactionNo,
                                MemberNo = transaction.MemberNo,
                                Amount = transaction.Amount,
                                TransactionType = transaction.TransactionType,
                                PaymentMode = transaction.PaymentMode,
                                Timestamp = transaction.ContributionDate
                            };

                            var blockchainTx = await blockchainService.CreateTransaction(
                                transaction.TransactionType,
                                transaction.MemberNo,
                                transaction.Companycode ?? "DEFAULT",
                                transaction.Amount,
                                transaction.Id.ToString(),
                                blockchainData
                            );

                            // Update transaction with blockchain ID
                            transaction.BlockchainTxId = blockchainTx.TransactionId;
                            await context.SaveChangesAsync();

                            // Add to blockchain
                            await blockchainService.AddToBlockchain(blockchainTx);

                            _logger.LogInformation("Processed transaction {TransactionId} to blockchain", transaction.TransactionNo);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing transaction {TransactionId} to blockchain", transaction.TransactionNo);
                        }
                    }

                    // Process pending Contrib records
                    var pendingContribs = await context.Contribs
                        .Where(c => string.IsNullOrEmpty(c.BlockchainTxId) && c.Amount.HasValue)
                        .Take(10)
                        .ToListAsync();

                    foreach (var contrib in pendingContribs)
                    {
                        try
                        {
                            var blockchainData = new
                            {
                                Table = "Contrib",
                                TransactionNo = contrib.TransactionNo,
                                MemberNo = contrib.MemberNo,
                                Amount = contrib.Amount,
                                ReceiptNo = contrib.ReceiptNo,
                                Purpose = contrib.Remarks,
                                Timestamp = contrib.ContrDate ?? DateTime.Now
                            };

                            var blockchainTx = await blockchainService.CreateTransaction(
                                contrib.Amount > 0 ? "DEPOSIT_CONTRIB" : "WITHDRAWAL_CONTRIB",
                                contrib.MemberNo,
                                contrib.CompanyCode ?? "DEFAULT",
                                contrib.Amount ?? 0,
                                contrib.Id.ToString(),
                                blockchainData
                            );

                            contrib.BlockchainTxId = blockchainTx.TransactionId;
                            await context.SaveChangesAsync();

                            await blockchainService.AddToBlockchain(blockchainTx);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing Contrib record {Id} to blockchain", contrib.Id);
                        }
                    }

                    _logger.LogDebug("Transaction processor processed {Count} transactions at {Time}",
                        pendingTransactions.Count + pendingContribs.Count, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in transaction processor");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}