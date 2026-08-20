using Microsoft.Extensions.Options;
using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Services
{
    public class LowStockDigestBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly LowStockSettings _settings;
        private readonly ILogger<LowStockDigestBackgroundService> _logger;

        public LowStockDigestBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<LowStockSettings> settings,
            ILogger<LowStockDigestBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Low-Stock Admin Digest Background Service is initializing...");

            // Initial startup delay to allow application startup & database seeding to complete cleanly
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Low-Stock Admin Digest Background Service stopping during startup delay.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_settings.DailyDigestEnabled)
                {
                    _logger.LogInformation("Executing scheduled Low-Stock Admin Digest email run at {Time}...", DateTime.UtcNow);

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var digestService = scope.ServiceProvider.GetRequiredService<ILowStockDigestService>();
                        
                        var result = await digestService.SendLowStockDigestAsync(cancellationToken: stoppingToken);
                        
                        if (result.Success)
                        {
                            _logger.LogInformation("Scheduled Low-Stock Admin Digest completed successfully: {Message}", result.Message);
                        }
                        else
                        {
                            _logger.LogWarning("Scheduled Low-Stock Admin Digest completed with notice: {Message}", result.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An unhandled exception occurred while executing scheduled Low-Stock Admin Digest.");
                    }
                }
                else
                {
                    _logger.LogInformation("Low-Stock Daily Digest is disabled in settings. Skipping execution.");
                }

                int intervalHours = _settings.RunIntervalHours > 0 ? _settings.RunIntervalHours : 24;
                _logger.LogInformation("Next scheduled Low-Stock Admin Digest run in {Hours} hours.", intervalHours);

                try
                {
                    await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Low-Stock Admin Digest Background Service is stopping.");
                    break;
                }
            }
        }
    }
}
