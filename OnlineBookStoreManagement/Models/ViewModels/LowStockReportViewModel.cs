namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class LowStockReportViewModel
    {
        public int Threshold { get; set; } = 5;
        public int TotalBooks { get; set; }
        public int OutOfStockCount => OutOfStockBooks.Count;
        public int LowStockCount => LowStockBooks.Count;
        public int TotalAlertCount => OutOfStockCount + LowStockCount;

        public List<Book> OutOfStockBooks { get; set; } = new();
        public List<Book> LowStockBooks { get; set; } = new();
        public List<string> AdminRecipients { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class LowStockDigestResult
    {
        public bool Success { get; set; }
        public int OutOfStockCount { get; set; }
        public int LowStockCount { get; set; }
        public int TotalAlertCount => OutOfStockCount + LowStockCount;
        public List<string> SentToEmails { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public DateTime ExecutionTime { get; set; } = DateTime.UtcNow;
    }
}
