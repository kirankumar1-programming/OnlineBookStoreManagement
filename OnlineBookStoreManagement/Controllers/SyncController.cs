using Microsoft.AspNetCore.Mvc;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly ISyncService _syncService;
        private readonly IServerDatabaseSyncService _serverDbSyncService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            ISyncService syncService,
            IServerDatabaseSyncService serverDbSyncService,
            ILogger<SyncController> logger)
        {
            _syncService = syncService;
            _serverDbSyncService = serverDbSyncService;
            _logger = logger;
        }

        // GET: /api/sync/ping
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { status = "online", timestamp = DateTime.UtcNow });
        }

        // GET: /api/sync/server-status
        [HttpGet("server-status")]
        public IActionResult GetServerStatus()
        {
            var status = _serverDbSyncService.GetCurrentSyncStatus();
            return Ok(status);
        }

        // POST: /api/sync/trigger-server-sync
        [HttpPost("trigger-server-sync")]
        public async Task<IActionResult> TriggerServerSync()
        {
            var result = await _serverDbSyncService.SyncWithServerDatabaseAsync();
            return Ok(result);
        }

        // GET: /api/sync/catalog
        [HttpGet("catalog")]
        public async Task<IActionResult> GetCatalog()
        {
            try
            {
                var catalog = await _syncService.GetCatalogForSyncAsync();
                return Ok(catalog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve catalog for sync.");
                return StatusCode(500, new { success = false, message = "Error loading offline catalog data." });
            }
        }

        // POST: /api/sync/process
        [HttpPost("process")]
        public async Task<IActionResult> ProcessBatch([FromBody] SyncBatchRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { success = false, message = "Invalid sync request payload." });
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var result = await _syncService.ProcessBatchSyncAsync(request, userId);

                // Trigger server database sync to push all batch items to Azure SQL Server immediately
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _serverDbSyncService.SyncWithServerDatabaseAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background server database sync failed after batch processing.");
                    }
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch sync request.");
                return StatusCode(500, new { success = false, message = $"Sync error: {ex.Message}" });
            }
        }
    }
}
