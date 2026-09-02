using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookStoreManagement.Models
{
    public class Book
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Author is required")]
        [StringLength(100, ErrorMessage = "Author cannot exceed 100 characters")]
        public string Author { get; set; } = string.Empty;

        [Required(ErrorMessage = "ISBN is required")]
        [StringLength(20, ErrorMessage = "ISBN cannot exceed 20 characters")]
        public string ISBN { get; set; } = string.Empty;

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, 1000000.00, ErrorMessage = "Price must be between ₹0.01 and ₹1,000,000.00")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Stock quantity is required")]
        [Range(0, 100000, ErrorMessage = "Stock quantity must be between 0 and 100,000")]
        public int StockQuantity { get; set; }

        [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters")]
        public string Description { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Cover image URL cannot exceed 500 characters")]
        public string CoverImageUrl { get; set; } = "/images/default-book.png";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "Category is required")]
        public int CategoryId { get; set; }
        public Category? Category { get; set; } = null!;

        public ICollection<BookReview> Reviews { get; set; } = new List<BookReview>();

        [NotMapped]
        public double AverageRating => Reviews != null && Reviews.Any() ? Math.Round(Reviews.Average(r => r.Rating), 1) : 0;

        [NotMapped]
        public int ReviewCount => Reviews != null ? Reviews.Count : 0;
    }
}