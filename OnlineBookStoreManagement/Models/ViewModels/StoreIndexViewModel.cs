namespace OnlineBookStoreManagement.Models.ViewModels
{

    public class StoreIndexViewModel
    {
        public IEnumerable<Book> Books { get; set; } = new List<Book>();
        public IEnumerable<Category> Categories { get; set; } = new List<Category>();
        public IEnumerable<string> Authors { get; set; } = new List<string>();

        public List<int> SelectedCategoryIds { get; set; } = new List<int>();
        public List<string> SelectedAuthors { get; set; } = new List<string>();
        public List<double> SelectedRatings { get; set; } = new List<double>();

        public int? SelectedCategoryId
        {
            get => SelectedCategoryIds.Any() ? SelectedCategoryIds.First() : null;
            set { if (value.HasValue && value.Value > 0) SelectedCategoryIds = new List<int> { value.Value }; }
        }
        public string? SelectedAuthor
        {
            get => SelectedAuthors.FirstOrDefault();
            set { if (!string.IsNullOrEmpty(value)) SelectedAuthors = new List<string> { value }; }
        }
        public double? MinRating
        {
            get => SelectedRatings.Any() ? SelectedRatings.Min() : null;
            set { if (value.HasValue && value.Value > 0) SelectedRatings = new List<double> { value.Value }; }
        }

        public string? SearchTerm { get; set; }
        public string? SortBy { get; set; } // "price_asc", "price_desc", "title_asc", "title_desc", "author_asc", "newest", "rating_desc", "most_reviewed"
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }

        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int PageSize { get; set; } = 6;
        public HashSet<int> UserWishlistBookIds { get; set; } = new HashSet<int>();

        public string GetQueryString(
            int? page = null,
            int? removeCategoryId = null,
            string? removeAuthor = null,
            double? removeRating = null,
            bool removeKeyword = false,
            bool removePrice = false)
        {
            var queryParams = new List<string>();

            int p = page ?? CurrentPage;
            if (p > 1) queryParams.Add($"page={p}");

            if (!removeKeyword && !string.IsNullOrWhiteSpace(SearchTerm))
                queryParams.Add($"searchTerm={Uri.EscapeDataString(SearchTerm)}");

            if (!string.IsNullOrWhiteSpace(SortBy))
                queryParams.Add($"sortBy={Uri.EscapeDataString(SortBy)}");

            if (!removePrice && MinPrice.HasValue)
                queryParams.Add($"minPrice={MinPrice.Value}");

            if (!removePrice && MaxPrice.HasValue)
                queryParams.Add($"maxPrice={MaxPrice.Value}");

            if (SelectedCategoryIds != null)
            {
                foreach (var id in SelectedCategoryIds)
                {
                    if (removeCategoryId.HasValue && removeCategoryId.Value == id) continue;
                    queryParams.Add($"categoryIds={id}");
                }
            }

            if (SelectedAuthors != null)
            {
                foreach (var author in SelectedAuthors)
                {
                    if (!string.IsNullOrEmpty(removeAuthor) && removeAuthor.Equals(author, StringComparison.OrdinalIgnoreCase)) continue;
                    queryParams.Add($"authors={Uri.EscapeDataString(author)}");
                }
            }

            if (SelectedRatings != null)
            {
                foreach (var r in SelectedRatings)
                {
                    if (removeRating.HasValue && Math.Abs(removeRating.Value - r) < 0.01) continue;
                    queryParams.Add($"minRatings={r}");
                }
            }

            return queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
        }
    }

    public class BookDetailsViewModel
    {
        public Book Book { get; set; } = null!;
        public IEnumerable<Book> RelatedBooks { get; set; } = new List<Book>();
        public int Quantity { get; set; } = 1;
        public BookReview NewReview { get; set; } = new BookReview();
        public bool UserHasReviewed { get; set; }
        public bool IsInWishlist { get; set; }
    }
}
