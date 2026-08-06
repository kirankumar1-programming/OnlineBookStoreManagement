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

        // GET: / Home/Index (Storefront Catalog with Search, Category & Price Filtering, Sorting, Pagination)
        public async Task<IActionResult> Index(
            int? categoryId,
            string? searchTerm,
            string? sortBy,
            decimal? minPrice,
            decimal? maxPrice,
            int page = 1)
        {
            int pageSize = 6;
            IQueryable<Book> booksQuery = _db.Books
                .Include(b => b.Category)
                .Include(b => b.Reviews);

            // Filter by Category
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                booksQuery = booksQuery.Where(b => b.CategoryId == categoryId.Value);
            }

            // Filter by Search Term (Title, Author, ISBN)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                booksQuery = booksQuery.Where(b =>
                    b.Title.ToLower().Contains(term) ||
                    b.Author.ToLower().Contains(term) ||
                    b.ISBN.ToLower().Contains(term));
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

            // Sorting
            booksQuery = sortBy switch
            {
                "price_asc" => booksQuery.OrderBy(b => b.Price),
                "price_desc" => booksQuery.OrderByDescending(b => b.Price),
                "title_asc" => booksQuery.OrderBy(b => b.Title),
                "newest" => booksQuery.OrderByDescending(b => b.CreatedAt),
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

            var viewModel = new StoreIndexViewModel
            {
                Books = books,
                Categories = categories,
                SelectedCategoryId = categoryId,
                SearchTerm = searchTerm,
                SortBy = sortBy,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems,
                PageSize = pageSize
            };

            return View(viewModel);
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
        public async Task<IActionResult> AddReview(int bookId, int rating, string comment)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Please sign in to leave a review.";
                return RedirectToAction(nameof(Details), new { id = bookId });
            }

            var book = await _db.Books.FindAsync(bookId);
            if (book == null) return NotFound();

            // Check if user already submitted a review for this book
            var existingReview = await _db.BookReviews.FirstOrDefaultAsync(r => r.BookId == bookId && r.UserId == userId);
            if (existingReview != null)
            {
                existingReview.Rating = rating;
                existingReview.Comment = comment;
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
                    Comment = comment,
                    ReviewDate = DateTime.UtcNow
                };
                await _db.BookReviews.AddAsync(newReview);
                TempData["SuccessMessage"] = "Thank you! Your review and rating have been posted.";
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = bookId });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
