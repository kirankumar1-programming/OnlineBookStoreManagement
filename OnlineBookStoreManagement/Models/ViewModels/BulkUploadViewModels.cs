using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class BulkUploadViewModel
    {
        [Required(ErrorMessage = "Please select an Excel or CSV file to upload.")]
        [Display(Name = "Select Excel or CSV File")]
        public IFormFile? File { get; set; }
    }

    public class BulkUploadRowResult
    {
        public int RowNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ISBN { get; set; } = string.Empty;
        public string Status { get; set; } = "Success"; // "Success", "Failed", "Warning"
        public List<string> Messages { get; set; } = new List<string>();
    }

    public class BulkUploadResultViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public int TotalRowsProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int WarningCount { get; set; }
        public List<BulkUploadRowResult> RowResults { get; set; } = new List<BulkUploadRowResult>();
        public List<Book> ImportedBooks { get; set; } = new List<Book>();
    }
}
