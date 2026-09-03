using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using System.Security.Claims;

namespace OnlineBookStoreManagement.Controllers
{
    public class WishlistController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<WishlistController> _logger;

        public WishlistController(ApplicationDbContext db, ILogger<WishlistController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private bool IsAjaxRequest() =>
            string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        // GET: /Wishlist
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Wishlist" });
            }

            var wishlistItems = await _db.WishlistItems
                .Include(w => w.Book)
                    .ThenInclude(b => b!.Category)
                .Include(w => w.Book)
                    .ThenInclude(b => b!.Reviews)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();

            var viewModel = new WishlistViewModel
            {
                WishlistItems = wishlistItems
            };

            return View(viewModel);
        }

        // POST: /Wishlist/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int bookId, string? returnUrl = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, requireLogin = true, message = "Please sign in to save books to your wishlist." });
                }
                TempData["ErrorMessage"] = "Please sign in to save books to your wishlist.";
                return RedirectToAction("Login", "Account", new { returnUrl = returnUrl ?? $"/Home/Details/{bookId}" });
            }

            var book = await _db.Books.FindAsync(bookId);
            if (book == null)
            {
                if (IsAjaxRequest()) return Json(new { success = false, message = "Book not found." });
                return NotFound();
            }

            var existingItem = await _db.WishlistItems
                .FirstOrDefaultAsync(w => w.UserId == userId && w.BookId == bookId);

            if (existingItem == null)
            {
                var wishlistItem = new WishlistItem
                {
                    UserId = userId,
                    BookId = bookId,
                    CreatedAt = DateTime.UtcNow
                };
                await _db.WishlistItems.AddAsync(wishlistItem);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"\"{book.Title}\" added to your wishlist!";
            }
            else
            {
                TempData["SuccessMessage"] = $"\"{book.Title}\" is already in your wishlist.";
            }

            int count = await _db.WishlistItems.CountAsync(w => w.UserId == userId);

            if (IsAjaxRequest())
            {
                return Json(new { success = true, inWishlist = true, message = $"\"{book.Title}\" saved to wishlist!", wishlistCount = count });
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Wishlist/Toggle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int bookId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, requireLogin = true, message = "Please sign in to manage your wishlist." });
            }

            var book = await _db.Books.FindAsync(bookId);
            if (book == null)
            {
                return Json(new { success = false, message = "Book not found." });
            }

            var existingItem = await _db.WishlistItems
                .FirstOrDefaultAsync(w => w.UserId == userId && w.BookId == bookId);

            bool inWishlist;
            string message;

            if (existingItem != null)
            {
                _db.WishlistItems.Remove(existingItem);
                await _db.SaveChangesAsync();
                inWishlist = false;
                message = $"\"{book.Title}\" removed from your wishlist.";
            }
            else
            {
                var wishlistItem = new WishlistItem
                {
                    UserId = userId,
                    BookId = bookId,
                    CreatedAt = DateTime.UtcNow
                };
                await _db.WishlistItems.AddAsync(wishlistItem);
                await _db.SaveChangesAsync();
                inWishlist = true;
                message = $"\"{book.Title}\" added to your wishlist!";
            }

            int count = await _db.WishlistItems.CountAsync(w => w.UserId == userId);
            return Json(new { success = true, inWishlist, message, wishlistCount = count });
        }

        // POST: /Wishlist/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id, string? returnUrl = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                if (IsAjaxRequest()) return Json(new { success = false, requireLogin = true });
                return RedirectToAction("Login", "Account", new { returnUrl = "/Wishlist" });
            }

            // Check if passed 'id' is WishlistItem Id or BookId
            var item = await _db.WishlistItems
                .Include(w => w.Book)
                .FirstOrDefaultAsync(w => (w.Id == id || w.BookId == id) && w.UserId == userId);

            if (item != null)
            {
                var bookTitle = item.Book?.Title ?? "Book";
                _db.WishlistItems.Remove(item);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"\"{bookTitle}\" removed from your wishlist.";
            }

            int count = await _db.WishlistItems.CountAsync(w => w.UserId == userId);

            if (IsAjaxRequest())
            {
                return Json(new { success = true, inWishlist = false, message = "Item removed from wishlist.", wishlistCount = count });
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Wishlist/MoveToCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToCart(int id, int quantity = 1, string? returnUrl = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Wishlist" });
            }

            if (quantity <= 0) quantity = 1;

            var wishlistItem = await _db.WishlistItems
                .Include(w => w.Book)
                .FirstOrDefaultAsync(w => (w.Id == id || w.BookId == id) && w.UserId == userId);

            if (wishlistItem == null || wishlistItem.Book == null)
            {
                TempData["ErrorMessage"] = "Wishlist item not found.";
                return RedirectToAction(nameof(Index));
            }

            var book = wishlistItem.Book;

            if (book.StockQuantity <= 0)
            {
                TempData["ErrorMessage"] = $"Sorry, \"{book.Title}\" is currently out of stock and cannot be moved to your cart.";
                return RedirectToAction(nameof(Index));
            }

            var cartItem = await _db.ShoppingCartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.BookId == book.Id);

            if (cartItem == null)
            {
                cartItem = new ShoppingCartItem
                {
                    UserId = userId,
                    BookId = book.Id,
                    Count = Math.Min(quantity, book.StockQuantity)
                };
                await _db.ShoppingCartItems.AddAsync(cartItem);
            }
            else
            {
                if (cartItem.Count + quantity <= book.StockQuantity)
                {
                    cartItem.Count += quantity;
                }
                else
                {
                    cartItem.Count = book.StockQuantity;
                }
            }

            // Remove from Wishlist
            _db.WishlistItems.Remove(wishlistItem);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"\"{book.Title}\" has been moved to your shopping cart!";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Cart");
        }

        // POST: /Wishlist/MoveAllToCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveAllToCart()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Wishlist" });
            }

            var wishlistItems = await _db.WishlistItems
                .Include(w => w.Book)
                .Where(w => w.UserId == userId)
                .ToListAsync();

            if (!wishlistItems.Any())
            {
                TempData["ErrorMessage"] = "Your wishlist is empty.";
                return RedirectToAction(nameof(Index));
            }

            var inStockItems = wishlistItems.Where(w => w.Book != null && w.Book.StockQuantity > 0).ToList();

            if (!inStockItems.Any())
            {
                TempData["ErrorMessage"] = "None of the items in your wishlist are currently in stock.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var item in inStockItems)
            {
                var book = item.Book!;
                var cartItem = await _db.ShoppingCartItems
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.BookId == book.Id);

                if (cartItem == null)
                {
                    cartItem = new ShoppingCartItem
                    {
                        UserId = userId,
                        BookId = book.Id,
                        Count = 1
                    };
                    await _db.ShoppingCartItems.AddAsync(cartItem);
                }
                else
                {
                    if (cartItem.Count < book.StockQuantity)
                    {
                        cartItem.Count += 1;
                    }
                }

                _db.WishlistItems.Remove(item);
            }

            await _db.SaveChangesAsync();

            int outOfStockCount = wishlistItems.Count - inStockItems.Count;
            if (outOfStockCount > 0)
            {
                TempData["SuccessMessage"] = $"{inStockItems.Count} item(s) moved to your cart. {outOfStockCount} out-of-stock item(s) remain in your wishlist.";
            }
            else
            {
                TempData["SuccessMessage"] = $"All {inStockItems.Count} item(s) have been moved to your shopping cart!";
            }

            return RedirectToAction("Index", "Cart");
        }

        // POST: /Wishlist/Clear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Wishlist" });
            }

            var items = await _db.WishlistItems.Where(w => w.UserId == userId).ToListAsync();
            if (items.Any())
            {
                _db.WishlistItems.RemoveRange(items);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Your wishlist has been cleared.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
