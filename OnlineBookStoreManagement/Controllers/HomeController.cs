using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using System.Diagnostics;
using System.Security.Claims;

namespace OnlineBookStoreManagement.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // GET: / Home/Index (Storefront Catalog with Multi-Keyword Search, Multi-Select Checkboxes for Category, Author, Price & Rating Filtering, Sorting, Pagination)
        public async Task<IActionResult> Index(
            [FromQuery] List<int>? categoryIds,
            int? categoryId,
            [FromQuery] List<string>? authors,
            string? author,
            string? searchTerm,
            string? sortBy,
            decimal? minPrice,
            decimal? maxPrice,
            [FromQuery] List<double>? minRatings,
            double? minRating,
            int page = 1)
        {
            int pageSize = 6;
            IQueryable<Book> booksQuery = _db.Books
                .Include(b => b.Category)
                .Include(b => b.Reviews);

            // Combine list parameters and single parameters for categories
            var selectedCategoryIds = categoryIds?.Where(id => id > 0).ToList() ?? new List<int>();
            if (categoryId.HasValue && categoryId.Value > 0 && !selectedCategoryIds.Contains(categoryId.Value))
            {
                selectedCategoryIds.Add(categoryId.Value);
            }

            // Combine list parameters and single parameters for authors
            var selectedAuthors = authors?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(author) && !selectedAuthors.Contains(author))
            {
                selectedAuthors.Add(author);
            }

            // Combine list parameters and single parameters for ratings
            var selectedRatings = minRatings?.Where(r => r > 0).ToList() ?? new List<double>();
            if (minRating.HasValue && minRating.Value > 0 && !selectedRatings.Contains(minRating.Value))
            {
                selectedRatings.Add(minRating.Value);
            }

            // Filter by Multi-Select Categories
            if (selectedCategoryIds.Any())
            {
                booksQuery = booksQuery.Where(b => selectedCategoryIds.Contains(b.CategoryId));
            }

            // Filter by Multi-Select Authors
            if (selectedAuthors.Any())
            {
                booksQuery = booksQuery.Where(b => selectedAuthors.Contains(b.Author));
            }

            // Filter by Multi-Select Ratings (matching books meeting lowest selected rating threshold)
            if (selectedRatings.Any())
            {
                double lowestRatingThreshold = selectedRatings.Min();
                booksQuery = booksQuery.Where(b => b.Reviews.Any() && b.Reviews.Average(r => (double)r.Rating) >= lowestRatingThreshold);
            }

            // Filter by Multi-word Keyword Search (Title, Author, ISBN, Description)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var terms = searchTerm.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var term in terms)
                {
                    var lowercaseTerm = term.ToLower();
                    booksQuery = booksQuery.Where(b =>
                        b.Title.ToLower().Contains(lowercaseTerm) ||
                        b.Author.ToLower().Contains(lowercaseTerm) ||
                        b.ISBN.ToLower().Contains(lowercaseTerm) ||
                        b.Description.ToLower().Contains(lowercaseTerm));
                }
            }

            // Filter by Price Range
            if (minPrice.HasValue && minPrice.Value > 0)
            {
                booksQuery = booksQuery.Where(b => b.Price >= minPrice.Value);
            }
            if (maxPrice.HasValue && maxPrice.Value > 0)
            {
                booksQuery = booksQuery.Where(b => b.Price <= maxPrice.Value);
            }

            // Sanitize sortBy in case multiple values arrive via duplicate query params
            var activeSort = sortBy?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            // Sorting
            booksQuery = activeSort switch
            {
                "price_asc" => booksQuery.OrderBy(b => b.Price).ThenBy(b => b.Title),
                "price_desc" => booksQuery.OrderByDescending(b => b.Price).ThenBy(b => b.Title),
                "title_asc" => booksQuery.OrderBy(b => b.Title),
                "title_desc" => booksQuery.OrderByDescending(b => b.Title),
                "author_asc" => booksQuery.OrderBy(b => b.Author).ThenBy(b => b.Title),
                "newest" => booksQuery.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id),
                "rating_desc" => booksQuery.OrderByDescending(b => b.Reviews.Any() ? b.Reviews.Average(r => (double)r.Rating) : 0).ThenByDescending(b => b.Reviews.Count),
                "most_reviewed" => booksQuery.OrderByDescending(b => b.Reviews.Count).ThenByDescending(b => b.Id),
                _ => booksQuery.OrderByDescending(b => b.Id)
            };

            int totalItems = await booksQuery.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            if (page < 1) page = 1;
            if (totalPages > 0 && page > totalPages) page = totalPages;

            var books = await booksQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var categories = await _db.Categories
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();

            var distinctAuthors = await _db.Books
                .Select(b => b.Author)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            var viewModel = new StoreIndexViewModel
            {
                Books = books,
                Categories = categories,
                Authors = distinctAuthors,
                SelectedCategoryIds = selectedCategoryIds,
                SelectedAuthors = selectedAuthors,
                SelectedRatings = selectedRatings,
                SearchTerm = searchTerm,
                SortBy = activeSort,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems,
                PageSize = pageSize
            };

            return View(viewModel);
        }

        // GET: /Home/LiveSearch?query=...
        [HttpGet]
        public async Task<IActionResult> LiveSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 1)
            {
                return Json(Array.Empty<object>());
            }

            var terms = query.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            IQueryable<Book> matchesQuery = _db.Books.Include(b => b.Category);
            foreach (var term in terms)
            {
                matchesQuery = matchesQuery.Where(b =>
                    b.Title.ToLower().Contains(term) ||
                    b.Author.ToLower().Contains(term) ||
                    b.ISBN.ToLower().Contains(term) ||
                    b.Description.ToLower().Contains(term));
            }

            var matches = await matchesQuery
                .OrderByDescending(b => b.Title.ToLower().StartsWith(terms[0]))
                .ThenByDescending(b => b.Author.ToLower().StartsWith(terms[0]))
                .ThenBy(b => b.Title)
                .Take(6)
                .Select(b => new
                {
                    id = b.Id,
                    title = b.Title,
                    author = b.Author,
                    isbn = b.ISBN,
                    category = b.Category != null ? b.Category.Name : "",
                    price = b.Price.ToString("F2"),
                    coverImageUrl = string.IsNullOrEmpty(b.CoverImageUrl) ? "/images/default-book.svg" : b.CoverImageUrl,
                    inStock = b.StockQuantity > 0,
                    stockQuantity = b.StockQuantity
                })
                .ToListAsync();

            return Json(matches);
        }

        // GET: /Home/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var book = await _db.Books
                .Include(b => b.Category)
                .Include(b => b.Reviews)
                .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (book == null)
            {
                return NotFound();
            }

            var relatedBooks = await _db.Books
                .Include(b => b.Reviews)
                .Where(b => b.CategoryId == book.CategoryId && b.Id != book.Id)
                .Take(3)
                .ToListAsync();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            bool userHasReviewed = !string.IsNullOrEmpty(userId) && book.Reviews.Any(r => r.UserId == userId);

            var viewModel = new BookDetailsViewModel
            {
                Book = book,
                RelatedBooks = relatedBooks,
                Quantity = 1,
                UserHasReviewed = userHasReviewed
            };

            return View(viewModel);
        }

        // POST: /Home/AddReview
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReview(int bookId, int rating, string? comment)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Please sign in to leave a review.";
                return RedirectToAction(nameof(Details), new { id = bookId });
            }

            var book = await _db.Books.FindAsync(bookId);
            if (book == null) return NotFound();

            // Server-side validation: Rating must be between 1 and 5 stars
            if (rating < 1 || rating > 5)
            {
                TempData["ErrorMessage"] = "Please select a valid rating between 1 and 5 stars.";
                return RedirectToAction(nameof(Details), new { id = bookId });
            }

            // Server-side validation: Comment required and limited to 1000 characters
            var sanitizedComment = comment?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sanitizedComment))
            {
                TempData["ErrorMessage"] = "Please enter a valid review comment.";
                return RedirectToAction(nameof(Details), new { id = bookId });
            }

            if (sanitizedComment.Length > 1000)
            {
                sanitizedComment = sanitizedComment.Substring(0, 1000);
            }

            // Check if user already submitted a review for this book
            var existingReview = await _db.BookReviews.FirstOrDefaultAsync(r => r.BookId == bookId && r.UserId == userId);
            if (existingReview != null)
            {
                existingReview.Rating = rating;
                existingReview.Comment = sanitizedComment;
                existingReview.ReviewDate = DateTime.UtcNow;
                _db.BookReviews.Update(existingReview);
                TempData["SuccessMessage"] = "Your review has been updated!";
            }
            else
            {
                var newReview = new BookReview
                {
                    BookId = bookId,
                    UserId = userId,
                    Rating = rating,
                    Comment = sanitizedComment,
                    ReviewDate = DateTime.UtcNow
                };
                await _db.BookReviews.AddAsync(newReview);
                TempData["SuccessMessage"] = "Thank you! Your review and rating have been posted.";
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = bookId });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? statusCode = null)
        {
            var model = new Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier };
            if (statusCode.HasValue)
            {
                if (statusCode.Value == 404)
                {
                    ViewData["ErrorMessage"] = "The page or resource you requested could not be found.";
                    ViewData["StatusCode"] = 404;
                }
                else if (statusCode.Value == 403)
                {
                    ViewData["ErrorMessage"] = "You do not have permission to access this resource.";
                    ViewData["StatusCode"] = 403;
                }
                else
                {
                    ViewData["ErrorMessage"] = $"An unexpected error occurred (HTTP {statusCode.Value}).";
                    ViewData["StatusCode"] = statusCode.Value;
                }
            }
            else
            {
                ViewData["ErrorMessage"] = "An error occurred while processing your request.";
                ViewData["StatusCode"] = 500;
            }
            return View(model);
        }
    }
}
