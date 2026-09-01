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
    public class CartAndCheckoutTests
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
            var book2 = new Book { Id = 2, Title = "Dune", Author = "Frank Herbert", ISBN = "222", Price = 500m, StockQuantity = 1, CategoryId = 1 };
            var book3 = new Book { Id = 3, Title = "Out of Stock Book", Author = "Unknown", ISBN = "333", Price = 300m, StockQuantity = 0, CategoryId = 1 };
            context.Books.AddRange(book1, book2, book3);
            await context.SaveChangesAsync();

            // Seed Coupons
            var coupon1 = new Coupon { Id = 1, Code = "WELCOME10", Description = "10% Off", DiscountType = "Percentage", DiscountValue = 10m, MinimumOrderAmount = 0m, IsActive = true };
            var coupon2 = new Coupon { Id = 2, Code = "FLAT100", Description = "Flat 100 Off", DiscountType = "Flat", DiscountValue = 100m, MinimumOrderAmount = 600m, IsActive = true };
            var coupon3 = new Coupon { Id = 3, Code = "BOOKWORM20", Description = "20% Off Capped at 200", DiscountType = "Percentage", DiscountValue = 20m, MinimumOrderAmount = 500m, MaximumDiscountAmount = 200m, IsActive = true };
            context.Coupons.AddRange(coupon1, coupon2, coupon3);
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
                User = claimsPrincipal,
                Session = new TestSession()
            };

            return new ControllerContext
            {
                HttpContext = httpContext
            };
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

        private class DummyTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class TestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new();
            public bool IsAvailable => true;
            public string Id => "test-session-id";
            public IEnumerable<string> Keys => _store.Keys;
            public void Clear() => _store.Clear();
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Remove(string key) => _store.Remove(key);
            public void Set(string key, byte[] value) => _store[key] = value;
            public bool TryGetValue(string key, out byte[]? value)
            {
                if (_store.TryGetValue(key, out var bytes))
                {
                    value = bytes;
                    return true;
                }
                value = null;
                return false;
            }
        }

        [Fact]
        public async Task AddToCart_And_UpdateQuantity_EnforcesStockLimits()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var controller = new CartController(db, userManager, new TestEmailSender(), NullLogger<CartController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Add 2 units of Book 1 (Stock = 5)
            var addResult = await controller.AddToCart(bookId: 1, quantity: 2);
            Assert.IsType<RedirectToActionResult>(addResult);

            var cartItem = await db.ShoppingCartItems.FirstOrDefaultAsync(c => c.UserId == "user-1" && c.BookId == 1);
            Assert.NotNull(cartItem);
            Assert.Equal(2, cartItem.Count);

            // 2. Update Quantity to 10 (exceeds stock limit 5) -> Should cap at 5
            var updateResult = await controller.UpdateQuantity(cartItem.Id, quantity: 10);
            Assert.IsType<RedirectToActionResult>(updateResult);

            var updatedCartItem = await db.ShoppingCartItems.FindAsync(cartItem.Id);
            Assert.NotNull(updatedCartItem);
            Assert.Equal(5, updatedCartItem.Count); // Capped at max stock 5
        }

        [Fact]
        public async Task StockValidation_CheckoutFails_WhenItemQuantityExceedsAvailableStock()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var controller = new CartController(db, userManager, new TestEmailSender(), NullLogger<CartController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add item with count = 10 when stock = 5 directly in DB
            db.ShoppingCartItems.Add(new ShoppingCartItem { UserId = "user-1", BookId = 1, Count = 10 });
            await db.SaveChangesAsync();

            var checkoutVm = new ShoppingCartViewModel
            {
                OrderHeader = new OrderHeader
                {
                    Name = "Rohan Sharma",
                    PhoneNumber = "9876543210",
                    StreetAddress = "123 MG Road",
                    City = "Bengaluru",
                    PostalCode = "560001"
                }
            };

            var result = await controller.Checkout(checkoutVm, paymentType: "upi");

            // Assert view returned with validation errors, order not created
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ShoppingCartViewModel>(viewResult.Model);
            Assert.NotEmpty(model.StockValidationErrors);
            Assert.Equal(0, await db.OrderHeaders.CountAsync()); // No order saved to DB
        }

        [Fact]
        public async Task ApplyCoupon_ValidatesPercentageAndFlatDiscounts_And_MinimumOrderLimits()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var controller = new CartController(db, userManager, new TestEmailSender(), NullLogger<CartController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 2 (Price = 500) to cart
            db.ShoppingCartItems.Add(new ShoppingCartItem { UserId = "user-1", BookId = 2, Count = 1 });
            await db.SaveChangesAsync();

            // 1. Attempt FLAT100 (Requires min order of 600) -> Should fail because subtotal = 500
            await controller.ApplyCoupon("FLAT100");
            Assert.Null(controller.HttpContext.Session.GetString("AppliedCouponCode"));

            // 2. Apply WELCOME10 (10% off, no min order) -> Should succeed
            await controller.ApplyCoupon("WELCOME10");
            Assert.Equal("WELCOME10", controller.HttpContext.Session.GetString("AppliedCouponCode"));

            var indexResult = await controller.Index();
            var viewResult = Assert.IsType<ViewResult>(indexResult);
            var vm = Assert.IsType<ShoppingCartViewModel>(viewResult.Model);

            Assert.Equal("WELCOME10", vm.CouponCode);
            Assert.Equal(50m, vm.DiscountAmount); // 10% of 500 = 50
            Assert.Equal(450m, vm.SubTotalAfterDiscount);
        }

        [Fact]
        public async Task ShoppingCartViewModel_CalculatesCorrectTotals_WithDiscountsTaxAndShipping()
        {
            var vm = new ShoppingCartViewModel
            {
                CartItems = new List<ShoppingCartItem>
                {
                    new ShoppingCartItem { Count = 1, Book = new Book { Price = 800m } }
                },
                DiscountAmount = 100m
            };

            Assert.Equal(800m, vm.SubTotal);
            Assert.Equal(700m, vm.SubTotalAfterDiscount); // 800 - 100
            Assert.Equal(56m, vm.EstimatedTax); // 8% of 700 = 56
            Assert.Equal(99m, vm.ShippingFee); // Subtotal < 999 -> Shipping 99
            Assert.Equal(855m, vm.GrandTotal); // 700 + 56 + 99 = 855
        }

        [Fact]
        public async Task CheckoutPost_ValidatesAddress_And_DeductsStockAtomically()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var controller = new CartController(db, userManager, new TestEmailSender(), NullLogger<CartController>.Instance)
            {
                ControllerContext = GetMockControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Add Book 1 (Price = 800, Stock = 5) with count = 2
            db.ShoppingCartItems.Add(new ShoppingCartItem { UserId = "user-1", BookId = 1, Count = 2 });
            await db.SaveChangesAsync();

            // Set session coupon WELCOME10
            controller.HttpContext.Session.SetString("AppliedCouponCode", "WELCOME10");

            var checkoutVm = new ShoppingCartViewModel
            {
                OrderHeader = new OrderHeader
                {
                    Name = "Rohan Sharma",
                    PhoneNumber = "9876543210",
                    StreetAddress = "123 MG Road",
                    City = "Bengaluru",
                    PostalCode = "560001"
                }
            };

            var result = await controller.Checkout(checkoutVm, paymentType: "upi");

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("OrderConfirmation", redirectResult.ActionName);

            // Verify OrderHeader in DB
            var orderHeader = await db.OrderHeaders.FirstOrDefaultAsync();
            Assert.NotNull(orderHeader);
            Assert.Equal("WELCOME10", orderHeader.CouponCode);
            Assert.Equal(160m, orderHeader.DiscountAmount); // 10% of 1600 (800 * 2)

            // Verify Stock deducted: original 5 - 2 = 3
            var updatedBook = await db.Books.FindAsync(1);
            Assert.NotNull(updatedBook);
            Assert.Equal(3, updatedBook.StockQuantity);

            // Verify cart cleared
            Assert.Equal(0, await db.ShoppingCartItems.CountAsync(c => c.UserId == "user-1"));
        }
    }
}
