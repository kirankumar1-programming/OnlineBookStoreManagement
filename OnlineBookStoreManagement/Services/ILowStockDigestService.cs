using OnlineBookStoreManagement.Models.ViewModels;

namespace OnlineBookStoreManagement.Services
{
    public interface ILowStockDigestService
    {
        Task<LowStockReportViewModel> GetLowStockReportAsync(int? customThreshold = null);
        Task<LowStockDigestResult> SendLowStockDigestAsync(int? customThreshold = null, CancellationToken cancellationToken = default);
    }
}
