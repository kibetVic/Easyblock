using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SACCOBlockChainSystem.Services
{
    public class BlockchainSyncService : BackgroundService
    {
        private readonly ILogger<BlockchainSyncService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        public BlockchainSyncService(ILogger<BlockchainSyncService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Blockchain Sync Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var blockchainService = scope.ServiceProvider.GetRequiredService<IBlockchainService>();

                    // Sync pending transactions
                    await blockchainService.ProcessPendingTransactionsAsync();

                    _logger.LogInformation("Blockchain sync completed at {Time}", DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during blockchain sync");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}