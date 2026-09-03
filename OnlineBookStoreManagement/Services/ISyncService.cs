using OnlineBookStoreManagement.Models;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Services
{
    public interface ISyncService
    {
        Task<SyncCatalogResponse> GetCatalogForSyncAsync();
        Task<SyncBatchResponse> ProcessBatchSyncAsync(SyncBatchRequest request, string? currentUserId);
    }
}
