namespace OnlineBookStoreManagement.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public int StatusCode { get; set; } = 500;
        public string? ErrorMessage { get; set; }
        public string? ErrorPath { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
