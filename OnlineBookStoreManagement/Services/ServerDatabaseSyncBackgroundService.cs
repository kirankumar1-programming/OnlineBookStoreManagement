using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Services
{
    public class ServerDatabaseSyncBackgroundService : BackgroundService
    {
        private readonly IServerDatabaseSyncService _syncService;
        private readonly ILogger<ServerDatabaseSyncBackgroundService> _logger;

        public ServerDatabaseSyncBackgroundService(
            IServerDatabaseSyncService syncService,
            ILogger<ServerDatabaseSyncBackgroundService> logger)
        {
            _syncService = syncService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Server Database Background Sync Service started.");

            // Wait 5 seconds after startup before initial sync
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _syncService.SyncWithServerDatabaseAsync();
                    if (result.IsConnected && (result.PulledBooksCount > 0 || result.PushedOrdersCount > 0 || result.PushedReviewsCount > 0))
                    {
                        _logger.LogInformation("Auto-Sync: {Message}", result.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Auto-Sync probe exception (offline mode active): {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Server Database Background Sync Service stopped.");
        }
    }
}
