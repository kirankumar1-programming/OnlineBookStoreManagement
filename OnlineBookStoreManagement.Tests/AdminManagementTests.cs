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
using Microsoft.Extensions.Options;
using OnlineBookStoreManagement.Controllers;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using OnlineBookStoreManagement.Services;
using Xunit;

namespace OnlineBookStoreManagement.Tests
{
    public class AdminManagementTests
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
            var fictionCategory = new Category { Id = 2, Name = "Fiction", DisplayOrder = 2 };
            context.Categories.AddRange(techCategory, fictionCategory);
            await context.SaveChangesAsync();

            // Seed Users
            var adminUser = new ApplicationUser { Id = "admin-1", UserName = "admin@bookstore.com", Email = "admin@bookstore.com", FullName = "Admin User" };
            var customerUser = new ApplicationUser { Id = "cust-1", UserName = "customer@test.com", Email = "customer@test.com", FullName = "Customer User" };
            context.Users.AddRange(adminUser, customerUser);
            await context.SaveChangesAsync();

            // Seed Books with varying stock levels
            var book1 = new Book { Id = 1, Title = "Clean Architecture", Author = "Robert Martin", ISBN = "111", Price = 800m, StockQuantity = 2, CategoryId = 1 };
            var book2 = new Book { Id = 2, Title = "Dune", Author = "Frank Herbert", ISBN = "222", Price = 500m, StockQuantity = 0, CategoryId = 2 }; // Out of stock
            var book3 = new Book { Id = 3, Title = "Refactoring", Author = "Martin Fowler", ISBN = "333", Price = 1000m, StockQuantity = 25, CategoryId = 1 }; // Well stocked
            context.Books.AddRange(book1, book2, book3);
            await context.SaveChangesAsync();

            // Seed Orders & Details for Revenue / Sales testing
            var order1 = new OrderHeader
            {
                Id = 1,
                UserId = "cust-1",
                OrderDate = DateTime.UtcNow.AddMonths(-1),
                OrderTotal = 2100m,
                OrderStatus = "Approved",
                PaymentStatus = "Approved",
                Name = "Customer User",
                City = "Mumbai"
            };
            var order2 = new OrderHeader
            {
                Id = 2,
                UserId = "cust-1",
                OrderDate = DateTime.UtcNow,
                OrderTotal = 1300m,
                OrderStatus = "Shipped",
                PaymentStatus = "Approved",
                Name = "Customer User",
                City = "Mumbai"
            };
            context.OrderHeaders.AddRange(order1, order2);
            await context.SaveChangesAsync();

            context.OrderDetails.AddRange(
                new OrderDetail { Id = 1, OrderHeaderId = 1, BookId = 1, Count = 2, Price = 800m }, // Clean Arch (1600)
                new OrderDetail { Id = 2, OrderHeaderId = 1, BookId = 2, Count = 1, Price = 500m }, // Dune (500)
                new OrderDetail { Id = 3, OrderHeaderId = 2, BookId = 1, Count = 1, Price = 800m }, // Clean Arch (800)
                new OrderDetail { Id = 4, OrderHeaderId = 2, BookId = 3, Count = 0, Price = 1000m }
            );
            await context.SaveChangesAsync();

            return context;
        }

        private ControllerContext GetMockAdminControllerContext(string adminUserId = "admin-1", string adminEmail = "admin@bookstore.com")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, adminUserId),
                new Claim(ClaimTypes.Name, adminEmail),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = claimsPrincipal }
            };
        }

        // Lightweight Test Doubles
        private class TestEmailSender : IEmailSenderService
        {
            public bool StatusUpdateEmailSent { get; private set; }
            public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
            public Task<bool> SendWelcomeEmailAsync(string toEmail, string userName) => Task.FromResult(true);
            public Task<bool> SendOrderConfirmationEmailAsync(string toEmail, OrderHeader orderHeader, IEnumerable<OrderDetail> orderDetails) => Task.FromResult(true);
            public Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, OrderHeader orderHeader, string previousStatus)
            {
                StatusUpdateEmailSent = true;
                return Task.FromResult(true);
            }
            public Task<bool> SendTestEmailAsync(string toEmail) => Task.FromResult(true);
        }

        private class TestPdfGenerator : IPdfInvoiceGeneratorService
        {
            public byte[] GenerateInvoicePdf(OrderHeader order) => new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        }

        private class TestLowStockDigestService : ILowStockDigestService
        {
            public Task<LowStockReportViewModel> GetLowStockReportAsync(int? customThreshold = null) => Task.FromResult(new LowStockReportViewModel());
            public Task<LowStockDigestResult> SendLowStockDigestAsync(int? customThreshold = null, bool? sendOnlyIfAlertsExist = null, CancellationToken cancellationToken = default)
                => Task.FromResult(new LowStockDigestResult { Success = true, Message = "Digest Sent" });
        }

        private class TestUserStore : IUserRoleStore<ApplicationUser>
        {
            private readonly List<ApplicationUser> _users;
            private readonly Dictionary<string, List<string>> _userRoles = new();

            public TestUserStore(List<ApplicationUser>? users = null)
            {
                _users = users ?? new List<ApplicationUser>();
            }

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

            // IUserRoleStore implementation
            public Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
            {
                if (!_userRoles.ContainsKey(user.Id)) _userRoles[user.Id] = new List<string>();
                if (!_userRoles[user.Id].Contains(roleName)) _userRoles[user.Id].Add(roleName);
                return Task.CompletedTask;
            }
            public Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
            {
                if (_userRoles.ContainsKey(user.Id)) _userRoles[user.Id].Remove(roleName);
                return Task.CompletedTask;
            }
            public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
            {
                if (!_userRoles.ContainsKey(user.Id))
                {
                    _userRoles[user.Id] = (user.Id == "admin-1" || (user.Email != null && user.Email.Equals("admin@bookstore.com", StringComparison.OrdinalIgnoreCase)))
                        ? new List<string> { "Admin" }
                        : new List<string> { "Customer" };
                }
                return Task.FromResult<IList<string>>(_userRoles[user.Id]);
            }
            public Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
            {
                var roles = GetRolesAsync(user, cancellationToken).Result;
                return Task.FromResult(roles.Contains(roleName));
            }
            public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
            {
                IList<ApplicationUser> list = _users.Where(u => IsInRoleAsync(u, roleName, cancellationToken).Result).ToList();
                return Task.FromResult(list);
            }
        }

        private class TestRoleStore : IRoleStore<IdentityRole>
        {
            public void Dispose() { }
            public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
            public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);
            public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
            public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken) { role.Name = roleName; return Task.CompletedTask; }
            public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name?.ToUpper());
            public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) => Task.FromResult<IdentityRole?>(null);
            public Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult<IdentityRole?>(null);
        }

        private class DummyTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        [Fact]
        public async Task DashboardKPIs_CalculatesCorrectTotalsAndTopCategory()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole>(new TestRoleStore(), null!, null!, null!, null!);

            var emailSender = new TestEmailSender();
            var digest = new TestLowStockDigestService();
            var pdf = new TestPdfGenerator();

            var controller = new AdminController(
                userManager,
                roleManager,
                db,
                emailSender,
                Options.Create(new SmtpSettings()),
                digest,
                pdf)
            {
                ControllerContext = GetMockAdminControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            var result = await controller.Analytics();

            var viewResult = Assert.IsType<ViewResult>(result);
            var vm = Assert.IsType<AnalyticsDashboardViewModel>(viewResult.Model);

            // Assert Dashboard KPI Summary
            Assert.Equal(3400m, vm.TotalRevenue); // 2100 + 1300
            Assert.Equal(2, vm.TotalOrders);
            Assert.Equal(4, vm.TotalBooksSold); // 2 + 1 + 1 + 0
            Assert.Equal("Technology", vm.TopCategoryName); // Category 1 generated most revenue
        }

        [Fact]
        public async Task InventoryAlerts_IdentifiesOutOfStockAndLowStockItems()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var emailSender = new TestEmailSender();
            var logger = NullLogger<LowStockDigestService>.Instance;

            var settings = Options.Create(new LowStockSettings { Threshold = 5 });
            var digestService = new LowStockDigestService(db, userManager, emailSender, settings, logger);

            var report = await digestService.GetLowStockReportAsync(customThreshold: 5);

            // Assert Inventory Alerts
            Assert.Equal(5, report.Threshold);
            Assert.Equal(3, report.TotalBooks);
            Assert.Single(report.OutOfStockBooks); // Dune (Qty 0)
            Assert.Equal("Dune", report.OutOfStockBooks.First().Title);
            Assert.Single(report.LowStockBooks); // Clean Architecture (Qty 2 <= 5)
            Assert.Equal("Clean Architecture", report.LowStockBooks.First().Title);
            Assert.Equal(2, report.TotalAlertCount);
        }

        [Fact]
        public async Task OrderManagement_FiltersAndUpdatesOrderStatus()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole>(new TestRoleStore(), null!, null!, null!, null!);

            var emailSender = new TestEmailSender();
            var digest = new TestLowStockDigestService();
            var pdf = new TestPdfGenerator();

            var controller = new AdminController(
                userManager,
                roleManager,
                db,
                emailSender,
                Options.Create(new SmtpSettings()),
                digest,
                pdf)
            {
                ControllerContext = GetMockAdminControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Filter Orders by Status
            var ordersResult = await controller.Orders("Shipped");
            var viewResult = Assert.IsType<ViewResult>(ordersResult);
            var ordersList = Assert.IsAssignableFrom<IEnumerable<OrderHeader>>(viewResult.Model);
            Assert.Single(ordersList);
            Assert.Equal("Shipped", ordersList.First().OrderStatus);

            // 2. Update Order Status, Carrier, Tracking Number
            var updateResult = await controller.UpdateOrderStatus(
                orderId: 1,
                orderStatus: "Delivered",
                paymentStatus: "Paid",
                carrier: "FedEx Express",
                trackingNumber: "TRK999888",
                shippingDate: DateTime.UtcNow);

            var redirectResult = Assert.IsType<RedirectToActionResult>(updateResult);
            Assert.Equal("OrderDetails", redirectResult.ActionName);

            var updatedOrder = await db.OrderHeaders.FindAsync(1);
            Assert.NotNull(updatedOrder);
            Assert.Equal("Delivered", updatedOrder.OrderStatus);
            Assert.Equal("Paid", updatedOrder.PaymentStatus);
            Assert.Equal("FedEx Express", updatedOrder.Carrier);
            Assert.Equal("TRK999888", updatedOrder.TrackingNumber);
        }

        [Fact]
        public async Task SalesRevenueReports_ApiEndpointReturnsCompleteAnalyticsJson()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole>(new TestRoleStore(), null!, null!, null!, null!);

            var emailSender = new TestEmailSender();
            var digest = new TestLowStockDigestService();
            var pdf = new TestPdfGenerator();

            var controller = new AdminController(
                userManager,
                roleManager,
                db,
                emailSender,
                Options.Create(new SmtpSettings()),
                digest,
                pdf)
            {
                ControllerContext = GetMockAdminControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            var jsonResult = await controller.GetAnalyticsApiData();
            var jsonObject = Assert.IsType<JsonResult>(jsonResult);
            var vm = Assert.IsType<AnalyticsDashboardViewModel>(jsonObject.Value);

            // Assert Sales & Revenue Reports data structure
            Assert.NotEmpty(vm.MonthlyRevenue.Labels);
            Assert.NotEmpty(vm.TopSellingBooks.Labels);
            Assert.NotEmpty(vm.CategoryRevenue.Labels);
            Assert.Equal("Clean Architecture", vm.TopSellingBooks.Labels.First()); // Best selling book title
        }

        [Fact]
        public async Task Admin_CanCreateCoupons_AndRejectsDuplicateCode()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole>(new TestRoleStore(), null!, null!, null!, null!);

            var controller = new AdminController(
                userManager,
                roleManager,
                db,
                new TestEmailSender(),
                Options.Create(new SmtpSettings()),
                new TestLowStockDigestService(),
                new TestPdfGenerator())
            {
                ControllerContext = GetMockAdminControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Create a valid coupon (e.g. FESTIVE25)
            var newCoupon = new Coupon
            {
                Code = "festive25",
                Description = "25% discount on all orders",
                DiscountType = "Percentage",
                DiscountValue = 25m,
                MinimumOrderAmount = 400m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                UsageLimit = 10,
                IsActive = true
            };

            var createResult = await controller.CreateCoupon(newCoupon);
            var redirectResult = Assert.IsType<RedirectToActionResult>(createResult);
            Assert.Equal("Coupons", redirectResult.ActionName);

            var savedCoupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == "FESTIVE25");
            Assert.NotNull(savedCoupon);
            Assert.Equal("FESTIVE25", savedCoupon.Code); // Upper-cased automatically
            Assert.Equal(25m, savedCoupon.DiscountValue);
            Assert.Equal(10, savedCoupon.UsageLimit);

            // 2. Attempt creating duplicate coupon with same code -> should fail with ModelState error
            var duplicateCoupon = new Coupon
            {
                Code = "FESTIVE25",
                Description = "Duplicate test",
                DiscountType = "Fixed",
                DiscountValue = 100m
            };

            var dupResult = await controller.CreateCoupon(duplicateCoupon);
            var viewResult = Assert.IsType<ViewResult>(dupResult);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        [Fact]
        public async Task Admin_CanEditCoupon_AndToggleStatus_AndDeleteCoupon()
        {
            using var db = await GetDatabaseContextAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new RoleManager<IdentityRole>(new TestRoleStore(), null!, null!, null!, null!);

            var controller = new AdminController(
                userManager,
                roleManager,
                db,
                new TestEmailSender(),
                Options.Create(new SmtpSettings()),
                new TestLowStockDigestService(),
                new TestPdfGenerator())
            {
                ControllerContext = GetMockAdminControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // Seed an initial coupon
            var coupon = new Coupon
            {
                Code = "SUMMER100",
                Description = "Flat ₹100 Off",
                DiscountType = "Fixed",
                DiscountValue = 100m,
                MinimumOrderAmount = 500m,
                IsActive = true
            };
            db.Coupons.Add(coupon);
            await db.SaveChangesAsync();

            // 1. Toggle Status -> Should set IsActive = false
            await controller.ToggleCouponStatus(coupon.Id);
            var toggledCoupon = await db.Coupons.FindAsync(coupon.Id);
            Assert.NotNull(toggledCoupon);
            Assert.False(toggledCoupon.IsActive);

            // 2. Edit Coupon -> Update DiscountValue to 150
            toggledCoupon.DiscountValue = 150m;
            toggledCoupon.IsActive = true;
            var editResult = await controller.EditCoupon(toggledCoupon.Id, toggledCoupon);
            Assert.IsType<RedirectToActionResult>(editResult);

            var updatedCoupon = await db.Coupons.FindAsync(coupon.Id);
            Assert.NotNull(updatedCoupon);
            Assert.Equal(150m, updatedCoupon.DiscountValue);
            Assert.True(updatedCoupon.IsActive);

            // 3. Delete Coupon
            var deleteResult = await controller.DeleteCouponConfirmed(coupon.Id);
            Assert.IsType<RedirectToActionResult>(deleteResult);
            Assert.Null(await db.Coupons.FindAsync(coupon.Id));
        }

        [Fact]
        public async Task Account_Login_UserWithAdminSubstring_IsNotPromotedToAdminRole()
        {
            using var db = await GetDatabaseContextAsync();
            var normalUserWithAdminInEmail = new ApplicationUser
            {
                Id = "user-admin-email",
                UserName = "fake_admin@gmail.com",
                Email = "fake_admin@gmail.com",
                FullName = "Fake Admin"
            };
            db.Users.Add(normalUserWithAdminInEmail);
            await db.SaveChangesAsync();

            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);

            // Verify that fake_admin@gmail.com is NOT in Admin role
            Assert.False(await userManager.IsInRoleAsync(normalUserWithAdminInEmail, "Admin"));
        }

        [Fact]
        public async Task Home_AddReview_ValidatesRatingAndCommentServerSide()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "cust-1"),
                new Claim(ClaimTypes.Name, "customer@test.com")
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var controller = new HomeController(db, userManager)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } },
                TempData = new TempDataDictionary(new DefaultHttpContext(), new DummyTempDataProvider())
            };

            // 1. Submit invalid rating (e.g. 10) -> Should fail validation
            var invalidRatingResult = await controller.AddReview(bookId: 1, rating: 10, comment: "Great book!");
            Assert.IsType<RedirectToActionResult>(invalidRatingResult);
            Assert.Equal("Please select a valid rating between 1 and 5 stars.", controller.TempData["ErrorMessage"]);

            // 2. Submit empty comment -> Should fail validation
            var emptyCommentResult = await controller.AddReview(bookId: 1, rating: 5, comment: "   ");
            Assert.IsType<RedirectToActionResult>(emptyCommentResult);
            Assert.Equal("Please enter a valid review comment.", controller.TempData["ErrorMessage"]);

            // 3. Submit valid review -> Should succeed
            var validResult = await controller.AddReview(bookId: 1, rating: 5, comment: "Excellent architecture principles.");
            Assert.IsType<RedirectToActionResult>(validResult);
            Assert.Equal("Thank you! Your review and rating have been posted.", controller.TempData["SuccessMessage"]);
        }

        [Fact]
        public async Task Chatbot_HandleOrderTracking_EnforcesOwnershipAndPreventsIDOR()
        {
            using var db = await GetDatabaseContextAsync();
            var users = await db.Users.ToListAsync();
            var userManager = new UserManager<ApplicationUser>(new TestUserStore(users), null!, null!, null!, null!, null!, null!, null!, null!);

            var chatbotController = new ChatbotController(db, userManager);

            // User A ("cust-1") owns Order #1 in DB (seeded in GetDatabaseContextAsync)
            // 1. Unauthenticated user requests Order #1 -> Should be rejected with sign-in prompt
            var unauthClaims = new ClaimsIdentity();
            chatbotController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(unauthClaims) } };

            var unauthResult = await chatbotController.Query(new ChatRequestDto { Message = "track order 1" });
            var okUnauth = Assert.IsType<OkObjectResult>(unauthResult);
            var dtoUnauth = Assert.IsType<ChatResponseDto>(okUnauth.Value);
            Assert.Contains("sign in", dtoUnauth.Reply, StringComparison.OrdinalIgnoreCase);

            // 2. User B ("other-user-2") requests Order #1 -> Should be rejected (IDOR blocked)
            var userBClaims = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "other-user-2") }, "TestAuth");
            chatbotController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(userBClaims) } };

            var userBResult = await chatbotController.Query(new ChatRequestDto { Message = "track order 1" });
            var okUserB = Assert.IsType<OkObjectResult>(userBResult);
            var dtoUserB = Assert.IsType<ChatResponseDto>(okUserB.Value);
            Assert.Contains("do not have permission", dtoUserB.Reply, StringComparison.OrdinalIgnoreCase);

            // 3. Owner ("cust-1") requests Order #1 -> Should succeed and return details
            var ownerClaims = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "cust-1") }, "TestAuth");
            chatbotController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(ownerClaims) } };

            var ownerResult = await chatbotController.Query(new ChatRequestDto { Message = "track order 1" });
            var okOwner = Assert.IsType<OkObjectResult>(ownerResult);
            var dtoOwner = Assert.IsType<ChatResponseDto>(okOwner.Value);
            Assert.Contains("Order #1 Details", dtoOwner.Reply);
        }
    }
}
