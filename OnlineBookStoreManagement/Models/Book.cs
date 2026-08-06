using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookStoreManagement.Models
{
    public class Book
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ISBN { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }

        public string Description { get; set; } = string.Empty;
        public string CoverImageUrl { get; set; } = "/images/default-book.png";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int CategoryId { get; set; }
        public Category? Category { get; set; } = null!;

        public ICollection<BookReview> Reviews { get; set; } = new List<BookReview>();

        [NotMapped]
        public double AverageRating => Reviews != null && Reviews.Any() ? Math.Round(Reviews.Average(r => r.Rating), 1) : 0;

        [NotMapped]
        public int ReviewCount => Reviews != null ? Reviews.Count : 0;
    }
}