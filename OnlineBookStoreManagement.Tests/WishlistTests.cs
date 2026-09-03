using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineBookStoreManagement.Controllers;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using OnlineBookStoreManagement.Services;
using Xunit;

namespace OnlineBookStoreManagement.Tests
{
    public class WishlistTests
    {
        private async Task<ApplicationDbContext> GetDatabaseContextAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared")
                .Options;

            var context = new ApplicationDbContext(options);
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();

            // Seed Categories
            var techCategory = new Category { Id = 1, Name = "Technology", DisplayOrder = 1 };
            context.Categories.Add(techCategory);
            await context.SaveChangesAsync();

            // Seed Users
            var customerUser = new ApplicationUser
            {
                Id = "user-1",
                UserName = "customer@test.com",
                Email = "customer@test.com",
                FullName = "Rohan Sharma",
                Address = "123 MG Road",
                City = "Bengaluru",
                PostalCode = "560001",
                PhoneNumber = "9876543210"
            };
            context.Users.Add(customerUser);
            await context.SaveChangesAsync();

            // Seed Books
            var book1 = new Book { Id = 1, Title = "Clean Architecture", Author = "Robert Martin", ISBN = "111", Price = 800m, StockQuantity = 5, CategoryId = 1 };
            var book2 = new Book { Id = 2, Title = "Dune", Author = "Frank Herbert", ISBN = "222", Price = 500m, StockQuantity = 2, CategoryId = 1 };
            var book3 = new Book { Id = 3, Title = "Out of Stock Book", Author = "Unknown", ISBN = "333", Price = 300m, StockQuantity = 0, CategoryId = 1 };
            context.Books.AddRange(book1, book2, book3);
            await context.SaveChangesAsync();

            return context;
        }

        private ControllerContext GetMockControllerContext(string userId = "user-1", string userEmail = "customer@test.com")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userEmail)
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            };

            return new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        private class DummyTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class TestEmailSender : IEmailSenderService
        {
            public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
            public Task<bool> SendWelcomeEmailAsync(string toEmail, string userName) => Task.FromResult(true);
            public Task<bool> SendOrderConfirmationEmailAsync(string toEmail, OrderHeader orderHeader, IEnumerable<OrderDetail> orderDetails) => Task.FromResult(true);
            public Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, OrderHeader orderHeader, string previousStatus) => Task.FromResult(true);
            public Task<bool> SendTestEmailAsync(string toEmail) => Task.FromResult(true);
        }

        private class TestUserStore : IUserRoleStore<ApplicationUser>
        {
            private readonly List<ApplicationUser> _users;
            public TestUserStore(List<ApplicationUser> users) => _users = users;
            public void Dispose() { }
            public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
            public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
            public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
            public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName?.ToUpper());
            public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(_users.FirstOrDefault(u => u.Id == userId));
            public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult(_users.FirstOrDefault(u => (u.UserName ?? "").Equals(normalizedUserName, StringComparison.OrdinalIgnoreCase)));
            public Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult<IList<string>>(new List<string> { "Customer" });
            public Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken) => Task.FromResult(false);
            public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) => Task.FromResult<IList<ApplicationUser>>(new List<ApplicationUser>());
        }

        [Fact]
        public async Task AddToWishlist_And_RemoveFromWishlist_WorksCorrectly()
        {
            using var db = await GetDatabaseContextAsync();
            var controller = new WishlistController(db, NullLogger<WishlistController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Add Book 1 to Wishlist
            var addResult = await controller.Add(bookId: 1);
            Assert.IsType<RedirectToActionResult>(addResult);

            var item = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == "user-1" && w.BookId == 1);
            Assert.NotNull(item);

            // 2. View Wishlist Index
            var indexResult = await controller.Index();
            var viewResult = Assert.IsType<ViewResult>(indexResult);
            var model = Assert.IsType<WishlistViewModel>(viewResult.Model);
            Assert.Single(model.WishlistItems);
            Assert.Equal(1, model.TotalItems);
            Assert.Equal(1, model.InStockItemsCount);

            // 3. Remove Book 1 from Wishlist
            var removeResult = await controller.Remove(item.Id);
            Assert.IsType<RedirectToActionResult>(removeResult);

            Assert.Equal(0, await db.WishlistItems.CountAsync(w => w.UserId == "user-1"));
        }

        [Fact]
        public async Task ToggleWishlist_AddsAndRemovesItem()
        {
            using var db = await GetDatabaseContextAsync();
            var controller = new WishlistController(db, NullLogger<WishlistController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Toggle Book 2 -> Should add
            var toggleResult1 = await controller.Toggle(bookId: 2);
            var json1 = Assert.IsType<JsonResult>(toggleResult1);
            var inWishlistProp1 = json1.Value?.GetType().GetProperty("inWishlist")?.GetValue(json1.Value);
            var countProp1 = json1.Value?.GetType().GetProperty("wishlistCount")?.GetValue(json1.Value);

            Assert.Equal(true, inWishlistProp1);
            Assert.Equal(1, countProp1);

            Assert.Equal(1, await db.WishlistItems.CountAsync(w => w.UserId == "user-1" && w.BookId == 2));

            // 2. Toggle Book 2 again -> Should remove
            var toggleResult2 = await controller.Toggle(bookId: 2);
            var json2 = Assert.IsType<JsonResult>(toggleResult2);
            var inWishlistProp2 = json2.Value?.GetType().GetProperty("inWishlist")?.GetValue(json2.Value);
            var countProp2 = json2.Value?.GetType().GetProperty("wishlistCount")?.GetValue(json2.Value);

            Assert.Equal(false, inWishlistProp2);
            Assert.Equal(0, countProp2);

            Assert.Equal(0, await db.WishlistItems.CountAsync(w => w.UserId == "user-1" && w.BookId == 2));
        }

        [Fact]
        public async Task MoveToCart_TransfersItemFromWishlistToCart()
        {
            using var db = await GetDatabaseContextAsync();
            var controller = new WishlistController(db, NullLogger<WishlistController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 1 to Wishlist
            var wishlistItem = new WishlistItem { UserId = "user-1", BookId = 1 };
            db.WishlistItems.Add(wishlistItem);
            await db.SaveChangesAsync();

            // Move to Cart
            var moveResult = await controller.MoveToCart(id: wishlistItem.Id, quantity: 2);
            var redirectResult = Assert.IsType<RedirectToActionResult>(moveResult);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Cart", redirectResult.ControllerName);

            // Verify item removed from Wishlist
            Assert.Equal(0, await db.WishlistItems.CountAsync(w => w.UserId == "user-1"));

            // Verify item added to ShoppingCartItems
            var cartItem = await db.ShoppingCartItems.FirstOrDefaultAsync(c => c.UserId == "user-1" && c.BookId == 1);
            Assert.NotNull(cartItem);
            Assert.Equal(2, cartItem.Count);
        }

        [Fact]
        public async Task MoveToCart_Fails_WhenItemIsOutOfStock()
        {
            using var db = await GetDatabaseContextAsync();
            var controller = new WishlistController(db, NullLogger<WishlistController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 3 (Out of stock) to Wishlist
            var wishlistItem = new WishlistItem { UserId = "user-1", BookId = 3 };
            db.WishlistItems.Add(wishlistItem);
            await db.SaveChangesAsync();

            // Attempt Move to Cart
            var moveResult = await controller.MoveToCart(id: wishlistItem.Id);
            var redirectResult = Assert.IsType<RedirectToActionResult>(moveResult);
            Assert.Equal("Index", redirectResult.ActionName);

            // Verify item still remains in Wishlist
            Assert.Equal(1, await db.WishlistItems.CountAsync(w => w.UserId == "user-1"));

            // Verify item was NOT added to cart
            Assert.Equal(0, await db.ShoppingCartItems.CountAsync(c => c.UserId == "user-1"));
        }

        [Fact]
        public async Task MoveAllToCart_TransfersOnlyInStockItems()
        {
            using var db = await GetDatabaseContextAsync();
            var controller = new WishlistController(db, NullLogger<WishlistController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 1 (In Stock = 5) and Book 3 (Out of Stock = 0)
            db.WishlistItems.AddRange(
                new WishlistItem { UserId = "user-1", BookId = 1 },
                new WishlistItem { UserId = "user-1", BookId = 3 }
            );
            await db.SaveChangesAsync();

            // Move All to Cart
            var moveAllResult = await controller.MoveAllToCart();
            var redirectResult = Assert.IsType<RedirectToActionResult>(moveAllResult);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Cart", redirectResult.ControllerName);

            // Verify Book 1 was moved to cart, Book 3 remained in wishlist
            var cartItems = await db.ShoppingCartItems.Where(c => c.UserId == "user-1").ToListAsync();
            Assert.Single(cartItems);
            Assert.Equal(1, cartItems[0].BookId);

            var remainingWishlist = await db.WishlistItems.Where(w => w.UserId == "user-1").ToListAsync();
            Assert.Single(remainingWishlist);
            Assert.Equal(3, remainingWishlist[0].BookId); // Out of stock book remains
        }

        [Fact]
        public async Task MoveToWishlist_FromCart_SavesForLater()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var cartController = new CartController(db, userManager, new TestEmailSender(), NullLogger<CartController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 1 to cart
            var cartItem = new ShoppingCartItem { UserId = "user-1", BookId = 1, Count = 2 };
            db.ShoppingCartItems.Add(cartItem);
            await db.SaveChangesAsync();

            // Move to Wishlist ("Save for Later")
            var moveResult = await cartController.MoveToWishlist(cartItem.Id);
            var redirectResult = Assert.IsType<RedirectToActionResult>(moveResult);
            Assert.Equal("Index", redirectResult.ActionName);

            // Verify removed from Cart
            Assert.Equal(0, await db.ShoppingCartItems.CountAsync(c => c.UserId == "user-1"));

            // Verify added to Wishlist
            var wishlistItem = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == "user-1" && w.BookId == 1);
            Assert.NotNull(wishlistItem);
        }
    }
}
