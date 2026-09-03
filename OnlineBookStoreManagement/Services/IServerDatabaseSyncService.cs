using OnlineBookStoreManagement.Models;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Services
{
    public interface IServerDatabaseSyncService
    {
        Task<bool> CheckServerConnectivityAsync();
        Task<SyncSummaryResult> SyncWithServerDatabaseAsync();
        SyncStatusDto GetCurrentSyncStatus();
    }
}
