namespace OnlineBookStoreManagement.Models
{
    public class LowStockSettings
    {
        public int Threshold { get; set; } = 5;
        public bool DailyDigestEnabled { get; set; } = true;
        public int RunIntervalHours { get; set; } = 24;
        public string? RecipientEmailOverride { get; set; }
    }
}
