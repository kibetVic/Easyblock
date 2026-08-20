using SACCOBlockChainSystem.Data;

namespace SACCOBlockChainSystem.Services
{
    public class LoanOverdueUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LoanOverdueUpdateService> _logger;

        public LoanOverdueUpdateService(
            IServiceProvider serviceProvider,
            ILogger<LoanOverdueUpdateService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Loan Overdue Update Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var loanService = scope.ServiceProvider.GetRequiredService<ILoanService>();
                        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextService>();

                        // This would need to be adapted to get all company codes
                        // For now, we'll handle a specific company or loop through them
                        var companyCodes = new List<string> { /* Get all company codes */ };

                        foreach (var companyCode in companyCodes)
                        {
                            await loanService.UpdateOverdueStatusesAsync(companyCode);
                        }

                        _logger.LogDebug("Overdue statuses updated successfully");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating overdue statuses");
                }

                // Run once per day at midnight
                var now = DateTime.Now;
                var nextRun = now.Date.AddDays(1);
                var delay = nextRun - now;

                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}