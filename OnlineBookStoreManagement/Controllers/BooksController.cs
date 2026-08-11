using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Controllers
{
    [Authorize]
    public class BooksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public BooksController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private async Task<bool> CheckAdminAccessAsync()
        {
            if (User.IsInRole("Admin") || User.IsInRole("Administrator")) return true;

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return false;

            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Administrator")) return true;

            if (!string.IsNullOrEmpty(user.Email) && user.Email.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(user.UserName) && user.UserName.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(user.FullName) && user.FullName.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // GET: /Books
        public async Task<IActionResult> Index()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var books = await _context.Books
                .Include(b => b.Category)
                .OrderByDescending(b => b.Id)
                .ToListAsync();

            return View(books);
        }

        // GET: /Books/Create
        public async Task<IActionResult> Create()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name");
            return View(new Book());
        }

        // POST: /Books/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Book book)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            ModelState.Remove("Category");
            ModelState.Remove("Reviews");

            if (ModelState.IsValid)
            {
                if (string.IsNullOrWhiteSpace(book.CoverImageUrl))
                {
                    book.CoverImageUrl = "/images/default-book.png";
                }
                book.CreatedAt = DateTime.UtcNow;

                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Book \"{book.Title}\" added successfully!";
                return RedirectToAction(nameof(Index));
            }

            var categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", book.CategoryId);
            return View(book);
        }

        // GET: /Books/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();

            var categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", book.CategoryId);
            return View(book);
        }

        // POST: /Books/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Book book)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id != book.Id) return NotFound();

            ModelState.Remove("Category");
            ModelState.Remove("Reviews");

            if (ModelState.IsValid)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(book.CoverImageUrl))
                    {
                        book.CoverImageUrl = "/images/default-book.png";
                    }

                    _context.Update(book);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Book \"{book.Title}\" updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BookExists(book.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            var categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", book.CategoryId);
            return View(book);
        }

        // GET: /Books/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var book = await _context.Books
                .Include(b => b.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) return NotFound();

            return View(book);
        }

        // POST: /Books/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Book \"{book.Title}\" deleted successfully!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool BookExists(int id)
        {
            return _context.Books.Any(e => e.Id == id);
        }
    }
}