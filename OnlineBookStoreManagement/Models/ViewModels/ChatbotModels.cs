namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
    }

    public class ChatBookDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string PriceFormatted { get; set; } = string.Empty;
        public string CoverImageUrl { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public double AverageRating { get; set; }
        public int StockQuantity { get; set; }
    }

    public class ChatOptionDto
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-chat-text";
    }

    public class ChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<ChatOptionDto> Options { get; set; } = new List<ChatOptionDto>();
        public List<ChatBookDto> Books { get; set; } = new List<ChatBookDto>();
        public string? ActionUrl { get; set; }
        public string? ActionText { get; set; }
    }
}
