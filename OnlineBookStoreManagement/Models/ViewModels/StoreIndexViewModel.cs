namespace OnlineBookStoreManagement.Models.ViewModels
{

    public class StoreIndexViewModel
    {
        public IEnumerable<Book> Books { get; set; } = new List<Book>();
        public IEnumerable<Category> Categories { get; set; } = new List<Category>();

        public int? SelectedCategoryId { get; set; }
        public string? SearchTerm { get; set; }
        public string? SortBy { get; set; } // "price_asc", "price_desc", "title_asc", "newest"
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }

        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int PageSize { get; set; } = 6;
    }

    public class BookDetailsViewModel
    {
        public Book Book { get; set; } = null!;
        public IEnumerable<Book> RelatedBooks { get; set; } = new List<Book>();
        public int Quantity { get; set; } = 1;
        public BookReview NewReview { get; set; } = new BookReview();
        public bool UserHasReviewed { get; set; }
    }
}
