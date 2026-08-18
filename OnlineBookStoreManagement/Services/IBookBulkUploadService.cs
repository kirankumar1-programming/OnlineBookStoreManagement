using Microsoft.AspNetCore.Http;
using OnlineBookStoreManagement.Models.ViewModels;

namespace OnlineBookStoreManagement.Services
{
    public interface IBookBulkUploadService
    {
        Task<BulkUploadResultViewModel> ProcessBulkUploadAsync(IFormFile file);
        byte[] GenerateSampleCsvTemplate();
        byte[] GenerateSampleExcelTemplate();
    }
}
