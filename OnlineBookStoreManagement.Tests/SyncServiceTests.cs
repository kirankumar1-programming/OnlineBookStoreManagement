using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineBookStoreManagement.Controllers;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Services;
using Xunit;

namespace OnlineBookStoreManagement.Tests
{
    public class SyncServiceTests
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
            var cat1 = new Category { Id = 1, Name = "Technology", DisplayOrder = 1 };
            var cat2 = new Category { Id = 2, Name = "Fiction", DisplayOrder = 2 };
            context.Categories.AddRange(cat1, cat2);
            await context.SaveChangesAsync();

            // Seed Users
            var user1 = new ApplicationUser
            {
                Id = "user-1",
                UserName = "john@example.com",
                Email = "john@example.com",
                FullName = "John Doe",
                Address = "123 Main St",
                City = "Metropolis",
                PostalCode = "12345",
                PhoneNumber = "9988776655"
            };
            context.Users.Add(user1);
            await context.SaveChangesAsync();

            // Seed Books
            var book1 = new Book { Id = 1, Title = "C# Mastery", Author = "Jane Dev", ISBN = "ISBN-111", Price = 499.00m, StockQuantity = 10, CategoryId = 1 };
            var book2 = new Book { Id = 2, Title = "Sci-Fi Odyssey", Author = "Arthur C.", ISBN = "ISBN-222", Price = 299.00m, StockQuantity = 2, CategoryId = 2 };
            context.Books.AddRange(book1, book2);
            await context.SaveChangesAsync();

            // Seed Reviews
            var review1 = new BookReview { Id = 1, BookId = 1, UserId = "user-1", Rating = 5, Comment = "Great book!", ReviewDate = DateTime.UtcNow };
            context.BookReviews.Add(review1);
            await context.SaveChangesAsync();

            return context;
        }

        private UserManager<ApplicationUser> GetMockUserManager(ApplicationDbContext context)
        {
            var users = context.Users.ToList();
            var userStore = new TestUserStore(users);
            return new UserManager<ApplicationUser>(
                userStore,
                null!, null!, null!, null!, null!, null!, null!, null!
            );
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
        public async Task GetCatalogForSyncAsync_ReturnsAllBooksAndCategories_WithCorrectData()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            // Act
            var catalog = await syncService.GetCatalogForSyncAsync();

            // Assert
            Assert.True(catalog.Success);
            Assert.Equal(2, catalog.Books.Count);
            Assert.Equal(2, catalog.Categories.Count);

            var book1 = catalog.Books.FirstOrDefault(b => b.Id == 1);
            Assert.NotNull(book1);
            Assert.Equal("C# Mastery", book1.Title);
            Assert.Equal(10, book1.StockQuantity);
            Assert.Equal(5.0, book1.AverageRating);
            Assert.Equal(1, book1.ReviewCount);
            Assert.Equal("Technology", book1.CategoryName);
        }

        [Fact]
        public async Task ProcessBatchSyncAsync_ProcessesOfflineOrder_DeductsStockAndPersistsOrder()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            var clientSyncId = "OFFLINE-TEST-ORD-001";
            var request = new SyncBatchRequest
            {
                BatchId = "BATCH-001",
                Orders = new List<OfflineOrderDto>
                {
                    new OfflineOrderDto
                    {
                        ClientSyncId = clientSyncId,
                        Name = "John Doe",
                        PhoneNumber = "9988776655",
                        StreetAddress = "123 Main St",
                        City = "Metropolis",
                        PostalCode = "12345",
                        PaymentType = "upi",
                        OrderTotal = 499.00m,
                        OrderDate = DateTime.UtcNow,
                        Items = new List<OfflineOrderItemDto>
                        {
                            new OfflineOrderItemDto { BookId = 1, Title = "C# Mastery", Count = 3, Price = 499.00m }
                        }
                    }
                }
            };

            // Act
            var response = await syncService.ProcessBatchSyncAsync(request, "user-1");

            // Assert
            Assert.True(response.Success);
            Assert.Equal(1, response.SyncedOrdersCount);

            var result = response.Results.FirstOrDefault(r => r.ClientSyncId == clientSyncId);
            Assert.NotNull(result);
            Assert.Equal("Success", result.Status);
            Assert.NotNull(result.ServerId);

            // Verify Database changes
            var order = await context.OrderHeaders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.ClientSyncId == clientSyncId);

            Assert.NotNull(order);
            Assert.Equal("user-1", order.UserId);
            Assert.Equal("John Doe", order.Name);
            Assert.Single(order.OrderDetails);
            Assert.Equal(3, order.OrderDetails.First().Count);

            // Verify stock deduction: 10 - 3 = 7
            var book = await context.Books.FindAsync(1);
            Assert.NotNull(book);
            Assert.Equal(7, book.StockQuantity);
        }

        [Fact]
        public async Task ProcessBatchSyncAsync_DuplicateClientSyncId_IdempotentSkipping()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            var clientSyncId = "OFFLINE-TEST-DUP-002";
            var orderDto = new OfflineOrderDto
            {
                ClientSyncId = clientSyncId,
                Name = "John Doe",
                PhoneNumber = "9988776655",
                StreetAddress = "123 Main St",
                City = "Metropolis",
                PostalCode = "12345",
                PaymentType = "card",
                OrderTotal = 499.00m,
                Items = new List<OfflineOrderItemDto>
                {
                    new OfflineOrderItemDto { BookId = 1, Title = "C# Mastery", Count = 2, Price = 499.00m }
                }
            };

            var request = new SyncBatchRequest { Orders = new List<OfflineOrderDto> { orderDto } };

            // Act 1: Initial Sync
            var response1 = await syncService.ProcessBatchSyncAsync(request, "user-1");
            Assert.Equal(1, response1.SyncedOrdersCount);
            Assert.Equal("Success", response1.Results[0].Status);

            // Act 2: Retry Sync with same clientSyncId
            var response2 = await syncService.ProcessBatchSyncAsync(request, "user-1");

            // Assert 2: Skipped idempotently
            Assert.Equal(0, response2.SyncedOrdersCount);
            Assert.Equal("Skipped", response2.Results[0].Status);

            // Verify stock deducted only once: 10 - 2 = 8
            var book = await context.Books.FindAsync(1);
            Assert.Equal(8, book!.StockQuantity);

            // Verify exactly one order exists
            var orderCount = await context.OrderHeaders.CountAsync(o => o.ClientSyncId == clientSyncId);
            Assert.Equal(1, orderCount);
        }

        [Fact]
        public async Task ProcessBatchSyncAsync_InsufficientStock_ReturnsConflict()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            var clientSyncId = "OFFLINE-TEST-STOCK-003";
            var request = new SyncBatchRequest
            {
                Orders = new List<OfflineOrderDto>
                {
                    new OfflineOrderDto
                    {
                        ClientSyncId = clientSyncId,
                        Name = "John Doe",
                        PhoneNumber = "9988776655",
                        StreetAddress = "123 Main St",
                        City = "Metropolis",
                        PostalCode = "12345",
                        OrderTotal = 2990.00m,
                        Items = new List<OfflineOrderItemDto>
                        {
                            // Book 2 has stockQuantity = 2, requesting 10
                            new OfflineOrderItemDto { BookId = 2, Title = "Sci-Fi Odyssey", Count = 10, Price = 299.00m }
                        }
                    }
                }
            };

            // Act
            var response = await syncService.ProcessBatchSyncAsync(request, "user-1");

            // Assert
            Assert.Equal(0, response.SyncedOrdersCount);
            var result = response.Results.First();
            Assert.Equal("Conflict", result.Status);
            Assert.Contains("Insufficient stock", result.Message);

            // Verify book stock untouched
            var book2 = await context.Books.FindAsync(2);
            Assert.Equal(2, book2!.StockQuantity);
        }

        [Fact]
        public async Task ProcessBatchSyncAsync_ProcessesOfflineReviews_InsertsOrUpdatesReview()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            var request = new SyncBatchRequest
            {
                Reviews = new List<OfflineReviewDto>
                {
                    // Update review for book 1
                    new OfflineReviewDto
                    {
                        ClientSyncId = "REV-UPDATE-1",
                        BookId = 1,
                        Rating = 4,
                        Comment = "Updated review comment while offline.",
                        ReviewDate = DateTime.UtcNow
                    },
                    // Add new review for book 2
                    new OfflineReviewDto
                    {
                        ClientSyncId = "REV-NEW-2",
                        BookId = 2,
                        Rating = 5,
                        Comment = "Amazing sci-fi thriller!",
                        ReviewDate = DateTime.UtcNow
                    }
                }
            };

            // Act
            var response = await syncService.ProcessBatchSyncAsync(request, "user-1");

            // Assert
            Assert.Equal(2, response.SyncedReviewsCount);

            var reviewBook1 = await context.BookReviews.FirstOrDefaultAsync(r => r.BookId == 1 && r.UserId == "user-1");
            Assert.NotNull(reviewBook1);
            Assert.Equal(4, reviewBook1.Rating);
            Assert.Equal("Updated review comment while offline.", reviewBook1.Comment);

            var reviewBook2 = await context.BookReviews.FirstOrDefaultAsync(r => r.BookId == 2 && r.UserId == "user-1");
            Assert.NotNull(reviewBook2);
            Assert.Equal(5, reviewBook2.Rating);
            Assert.Equal("Amazing sci-fi thriller!", reviewBook2.Comment);
        }

        [Fact]
        public async Task ProcessBatchSyncAsync_SyncsCartAndWishlistItems_PersistsToDb()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);

            var request = new SyncBatchRequest
            {
                CartItems = new List<OfflineCartItemDto>
                {
                    new OfflineCartItemDto { BookId = 1, Count = 2 }
                },
                WishlistItems = new List<OfflineWishlistItemDto>
                {
                    new OfflineWishlistItemDto { BookId = 2 }
                }
            };

            // Act
            var response = await syncService.ProcessBatchSyncAsync(request, "user-1");

            // Assert
            Assert.True(response.Success);

            var cartItem = await context.ShoppingCartItems.FirstOrDefaultAsync(c => c.UserId == "user-1" && c.BookId == 1);
            Assert.NotNull(cartItem);
            Assert.Equal(2, cartItem.Count);

            var wishlistItem = await context.WishlistItems.FirstOrDefaultAsync(w => w.UserId == "user-1" && w.BookId == 2);
            Assert.NotNull(wishlistItem);
        }

        private class TestServerDatabaseSyncService : IServerDatabaseSyncService
        {
            public Task<bool> CheckServerConnectivityAsync() => Task.FromResult(true);
            public Task<SyncSummaryResult> SyncWithServerDatabaseAsync() => Task.FromResult(new SyncSummaryResult { Success = true, IsConnected = true });
            public SyncStatusDto GetCurrentSyncStatus() => new SyncStatusDto { IsServerOnline = true, ServerDatabaseProvider = "TestProvider" };
        }

        [Fact]
        public async Task SyncController_PingAndCatalogAndProcessEndpoints_WorkCorrectly()
        {
            // Arrange
            var context = await GetDatabaseContextAsync();
            var userManager = GetMockUserManager(context);
            var syncService = new SyncService(context, userManager, new TestEmailSender(), NullLogger<SyncService>.Instance);
            var serverSyncService = new TestServerDatabaseSyncService();
            var controller = new SyncController(syncService, serverSyncService, NullLogger<SyncController>.Instance);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Name, "john@example.com")
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            // Test Ping
            var pingResult = controller.Ping() as OkObjectResult;
            Assert.NotNull(pingResult);

            // Test Catalog
            var catalogResult = await controller.GetCatalog() as OkObjectResult;
            Assert.NotNull(catalogResult);
            var catalog = catalogResult.Value as SyncCatalogResponse;
            Assert.NotNull(catalog);
            Assert.Equal(2, catalog.Books.Count);

            // Test Process Batch
            var batchRequest = new SyncBatchRequest
            {
                Orders = new List<OfflineOrderDto>
                {
                    new OfflineOrderDto
                    {
                        ClientSyncId = "CTRL-ORD-01",
                        Name = "API User",
                        PhoneNumber = "1234567890",
                        StreetAddress = "789 Tech Park",
                        City = "Pune",
                        PostalCode = "411001",
                        OrderTotal = 499m,
                        Items = new List<OfflineOrderItemDto>
                        {
                            new OfflineOrderItemDto { BookId = 1, Title = "C# Mastery", Count = 1, Price = 499m }
                        }
                    }
                }
            };

            var processResult = await controller.ProcessBatch(batchRequest) as OkObjectResult;
            Assert.NotNull(processResult);
            var processResponse = processResult.Value as SyncBatchResponse;
            Assert.NotNull(processResponse);
            Assert.Equal(1, processResponse.SyncedOrdersCount);
        }

        [Fact]
        public async Task ServerDatabaseSyncService_WhenServerOffline_ReturnsOfflineGracefullyWithoutThrowing()
        {
            // Arrange: Service provider with unconfigured / invalid server db to simulate offline state
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            var context = await GetDatabaseContextAsync();
            services.AddScoped(_ => context);

            // ServerDbContext with an unreachable connection string to simulate being offline
            var serverDbOptions = new DbContextOptionsBuilder<ServerDbContext>()
                .UseSqlServer("Server=tcp:127.0.0.1,9999;Database=BookStore;User ID=sa;Password=Pass@123;Connection Timeout=1;Encrypt=False;")
                .Options;
            services.AddScoped(_ => new ServerDbContext(serverDbOptions));
            var serviceProvider = services.BuildServiceProvider();

            var serverSyncService = new ServerDatabaseSyncService(serviceProvider, NullLogger<ServerDatabaseSyncService>.Instance);

            // Act
            var result = await serverSyncService.SyncWithServerDatabaseAsync();

            // Assert: Handles offline gracefully without throwing
            Assert.True(result.Success);
            Assert.False(result.IsConnected);
            var status = serverSyncService.GetCurrentSyncStatus();
            Assert.NotNull(status);
            Assert.False(status.IsServerOnline);
        }

        [Fact]
        public async Task ServerDatabaseSyncService_WhenOnline_SyncsCartItemsToServer()
        {
            // Arrange: Local and Server DbContexts sharing in-memory databases
            var localConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            await localConnection.OpenAsync();

            var serverConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            await serverConnection.OpenAsync();

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(localConnection));
            services.AddDbContext<ServerDbContext>(options => options.UseSqlite(serverConnection));

            var serviceProvider = services.BuildServiceProvider();

            // Seed Local DB
            using (var scope = serviceProvider.CreateScope())
            {
                var localDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await localDb.Database.EnsureCreatedAsync();

                localDb.Categories.Add(new Category { Id = 1, Name = "Technology" });
                localDb.Users.Add(new ApplicationUser { Id = "user-1", UserName = "john@example.com", Email = "john@example.com" });
                localDb.Books.Add(new Book { Id = 1, Title = "C# Mastery", ISBN = "ISBN-111", CategoryId = 1, Price = 499m, StockQuantity = 10 });
                localDb.ShoppingCartItems.Add(new ShoppingCartItem { UserId = "user-1", BookId = 1, Count = 2 });
                await localDb.SaveChangesAsync();
            }

            // Seed Server DB
            using (var scope = serviceProvider.CreateScope())
            {
                var serverDb = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                await serverDb.Database.EnsureCreatedAsync();

                serverDb.Users.Add(new ApplicationUser { Id = "user-1", UserName = "john@example.com", Email = "john@example.com" });
                serverDb.Categories.Add(new Category { Id = 1, Name = "Technology" });
                serverDb.Books.Add(new Book { Id = 1, Title = "C# Mastery", ISBN = "ISBN-111", CategoryId = 1, Price = 499m, StockQuantity = 10 });
                await serverDb.SaveChangesAsync();
            }

            var serverSyncService = new ServerDatabaseSyncService(serviceProvider, NullLogger<ServerDatabaseSyncService>.Instance);

            // Act
            var result = await serverSyncService.SyncWithServerDatabaseAsync();

            // Assert
            Assert.True(result.Success, $"Sync failed: {result.Message}");
            Assert.True(result.IsConnected);

            // Verify server has the shopping cart item
            using (var scope = serviceProvider.CreateScope())
            {
                var serverDb = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                var sCart = await serverDb.ShoppingCartItems.FirstOrDefaultAsync(c => c.UserId == "user-1" && c.BookId == 1);
                Assert.NotNull(sCart);
                Assert.Equal(2, sCart.Count);
            }
        }
    }
}
