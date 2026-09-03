using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Services
{
    public class ServerDatabaseSyncService : IServerDatabaseSyncService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServerDatabaseSyncService> _logger;
        private static SyncStatusDto _currentStatus = new SyncStatusDto
        {
            IsServerOnline = false,
            LastSyncTime = null,
            LastSyncMessage = "Initialized in local offline-first mode.",
            ServerDatabaseProvider = "Azure SQL Server"
        };
        private static readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);

        public ServerDatabaseSyncService(
            IServiceProvider serviceProvider,
            ILogger<ServerDatabaseSyncService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public SyncStatusDto GetCurrentSyncStatus()
        {
            return _currentStatus;
        }

        public async Task<bool> CheckServerConnectivityAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var serverDb = scope.ServiceProvider.GetService<ServerDbContext>();
                if (serverDb == null) return false;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var canConnect = await serverDb.Database.CanConnectAsync(cts.Token);
                _currentStatus.IsServerOnline = canConnect;
                return canConnect;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Server database connectivity check: Offline ({Message})", ex.Message);
                _currentStatus.IsServerOnline = false;
                return false;
            }
        }

        public async Task<SyncSummaryResult> SyncWithServerDatabaseAsync()
        {
            if (!await _syncLock.WaitAsync(100))
            {
                return new SyncSummaryResult
                {
                    Success = true,
                    IsConnected = _currentStatus.IsServerOnline,
                    Message = "A sync operation is already in progress."
                };
            }

            try
            {
                bool isConnected = await CheckServerConnectivityAsync();
                if (!isConnected)
                {
                    _currentStatus.IsServerOnline = false;
                    _currentStatus.LastSyncMessage = "Server database is offline. All operations running against local database.";
                    return new SyncSummaryResult
                    {
                        Success = true,
                        IsConnected = false,
                        Message = _currentStatus.LastSyncMessage
                    };
                }

                using var scope = _serviceProvider.CreateScope();
                var localDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var serverDb = scope.ServiceProvider.GetService<ServerDbContext>();

                if (serverDb == null)
                {
                    return new SyncSummaryResult
                    {
                        Success = false,
                        IsConnected = false,
                        Message = "ServerDbContext is not configured."
                    };
                }

                int pulledBooks = 0;
                int pulledCategories = 0;
                int pushedOrders = 0;
                int pushedReviews = 0;
                int syncedCartItems = 0;
                int syncedWishlistItems = 0;

                // 1. PULL: Sync Categories from Server to Local
                try
                {
                    var serverCategories = await serverDb.Categories.ToListAsync();
                    var localCategories = await localDb.Categories.ToDictionaryAsync(c => c.Name);

                    foreach (var sCat in serverCategories)
                    {
                        if (!localCategories.TryGetValue(sCat.Name, out var lCat))
                        {
                            await localDb.Categories.AddAsync(new Category
                            {
                                Name = sCat.Name,
                                Description = sCat.Description,
                                DisplayOrder = sCat.DisplayOrder
                            });
                            pulledCategories++;
                        }
                    }
                    if (pulledCategories > 0)
                    {
                        await localDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to pull categories during server sync.");
                }

                // 2. PULL: Sync Books from Server to Local
                try
                {
                    var serverBooks = await serverDb.Books.Include(b => b.Category).ToListAsync();
                    var localCategories = await localDb.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
                    var localBooks = await localDb.Books.ToDictionaryAsync(b => b.ISBN);

                    foreach (var sBook in serverBooks)
                    {
                        if (string.IsNullOrWhiteSpace(sBook.ISBN)) continue;

                        int categoryId = 1;
                        if (sBook.Category != null && localCategories.TryGetValue(sBook.Category.Name, out var catId))
                        {
                            categoryId = catId;
                        }
                        else if (localCategories.Any())
                        {
                            categoryId = localCategories.Values.First();
                        }

                        if (!localBooks.TryGetValue(sBook.ISBN, out var lBook))
                        {
                            await localDb.Books.AddAsync(new Book
                            {
                                Title = sBook.Title,
                                Author = sBook.Author,
                                ISBN = sBook.ISBN,
                                Price = sBook.Price,
                                StockQuantity = sBook.StockQuantity,
                                Description = sBook.Description,
                                CoverImageUrl = sBook.CoverImageUrl,
                                CategoryId = categoryId,
                                CreatedAt = sBook.CreatedAt
                            });
                            pulledBooks++;
                        }
                    }
                    if (pulledBooks > 0)
                    {
                        await localDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to pull books during server sync.");
                }

                // 3. PULL: Sync Coupons from Server to Local
                try
                {
                    var serverCoupons = await serverDb.Coupons.ToListAsync();
                    var localCoupons = await localDb.Coupons.ToDictionaryAsync(c => c.Code.ToUpper());

                    foreach (var sCoupon in serverCoupons)
                    {
                        if (!localCoupons.ContainsKey(sCoupon.Code.ToUpper()))
                        {
                            await localDb.Coupons.AddAsync(new Coupon
                            {
                                Code = sCoupon.Code,
                                Description = sCoupon.Description,
                                DiscountType = sCoupon.DiscountType,
                                DiscountValue = sCoupon.DiscountValue,
                                MinimumOrderAmount = sCoupon.MinimumOrderAmount,
                                MaximumDiscountAmount = sCoupon.MaximumDiscountAmount,
                                IsActive = sCoupon.IsActive,
                                StartDate = sCoupon.StartDate,
                                ExpiryDate = sCoupon.ExpiryDate,
                                UsageLimit = sCoupon.UsageLimit,
                                TimesUsed = sCoupon.TimesUsed
                            });
                        }
                    }
                    await localDb.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync coupons.");
                }

                // 4. USER MAPPING: Map Local User IDs to Server User IDs (and sync new users)
                var userMapping = new Dictionary<string, string>(); // localUserId -> serverUserId
                try
                {
                    var localUsers = await localDb.Users.ToListAsync();
                    var serverUsers = await serverDb.Users.ToListAsync();
                    var serverUsersByEmail = serverUsers
                        .Where(u => !string.IsNullOrEmpty(u.Email))
                        .GroupBy(u => u.Email!.ToUpper())
                        .ToDictionary(g => g.Key, g => g.First());

                    foreach (var lUser in localUsers)
                    {
                        string emailKey = (lUser.Email ?? lUser.UserName ?? "").ToUpper();
                        if (string.IsNullOrEmpty(emailKey)) continue;

                        if (serverUsersByEmail.TryGetValue(emailKey, out var sUser))
                        {
                            userMapping[lUser.Id] = sUser.Id;
                        }
                        else
                        {
                            // Sync user to server database
                            var newServerUser = new ApplicationUser
                            {
                                Id = lUser.Id,
                                UserName = lUser.UserName,
                                NormalizedUserName = lUser.NormalizedUserName,
                                Email = lUser.Email,
                                NormalizedEmail = lUser.NormalizedEmail,
                                EmailConfirmed = lUser.EmailConfirmed,
                                PasswordHash = lUser.PasswordHash,
                                SecurityStamp = lUser.SecurityStamp ?? Guid.NewGuid().ToString(),
                                ConcurrencyStamp = lUser.ConcurrencyStamp ?? Guid.NewGuid().ToString(),
                                PhoneNumber = lUser.PhoneNumber,
                                FullName = lUser.FullName,
                                Address = lUser.Address,
                                City = lUser.City,
                                PostalCode = lUser.PostalCode
                            };
                            await serverDb.Users.AddAsync(newServerUser);
                            await serverDb.SaveChangesAsync();
                            userMapping[lUser.Id] = newServerUser.Id;
                            serverUsersByEmail[emailKey] = newServerUser;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync users.");
                }

                // Helper dictionary to map ISBN to Server Book safely
                var serverBooksList = await serverDb.Books.ToListAsync();
                var serverBooksDict = serverBooksList
                    .Where(b => !string.IsNullOrWhiteSpace(b.ISBN))
                    .GroupBy(b => b.ISBN)
                    .ToDictionary(g => g.Key, g => g.First());
                var localBooksDict = await localDb.Books.ToDictionaryAsync(b => b.Id);

                Book? ResolveServerBook(int localBookId)
                {
                    if (localBooksDict.TryGetValue(localBookId, out var lBook))
                    {
                        if (!string.IsNullOrWhiteSpace(lBook.ISBN) && serverBooksDict.TryGetValue(lBook.ISBN, out var sBookByIsbn))
                        {
                            return sBookByIsbn;
                        }
                        if (!string.IsNullOrWhiteSpace(lBook.Title))
                        {
                            return serverBooksList.FirstOrDefault(b => b.Title.Equals(lBook.Title, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    return null;
                }

                // 5. PUSH / SYNC: Shopping Cart Items (Local -> Server)
                try
                {
                    var localCartItems = await localDb.ShoppingCartItems.Include(c => c.Book).ToListAsync();
                    var distinctLocalUserIds = localCartItems.Select(c => c.UserId).Distinct().ToList();

                    // Group local cart by local user ID
                    var localCartByUser = localCartItems.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var (localUserId, serverUserId) in userMapping)
                    {
                        var sCartItems = await serverDb.ShoppingCartItems.Where(c => c.UserId == serverUserId).ToListAsync();

                        if (localCartByUser.TryGetValue(localUserId, out var lItems) && lItems.Any())
                        {
                            // Keep track of active server book IDs for this user
                            var activeServerBookIds = new HashSet<int>();

                            foreach (var lItem in lItems)
                            {
                                var sBook = ResolveServerBook(lItem.BookId);
                                if (sBook == null) continue;

                                activeServerBookIds.Add(sBook.Id);

                                var existingSCart = sCartItems.FirstOrDefault(c => c.BookId == sBook.Id);
                                if (existingSCart == null)
                                {
                                    await serverDb.ShoppingCartItems.AddAsync(new ShoppingCartItem
                                    {
                                        UserId = serverUserId,
                                        BookId = sBook.Id,
                                        Count = lItem.Count
                                    });
                                    syncedCartItems++;
                                }
                                else if (existingSCart.Count != lItem.Count)
                                {
                                    existingSCart.Count = lItem.Count;
                                    syncedCartItems++;
                                }
                            }

                            // Remove items from server cart that are no longer in local cart
                            var itemsToRemove = sCartItems.Where(c => !activeServerBookIds.Contains(c.BookId)).ToList();
                            if (itemsToRemove.Any())
                            {
                                serverDb.ShoppingCartItems.RemoveRange(itemsToRemove);
                            }
                        }
                        else
                        {
                            // User has no items in local cart (cleared or checked out) -> clear server cart for this user
                            if (sCartItems.Any())
                            {
                                serverDb.ShoppingCartItems.RemoveRange(sCartItems);
                            }
                        }
                    }

                    await serverDb.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync shopping cart items to server database.");
                }

                // 6. PUSH / SYNC: Wishlist Items (Local -> Server)
                try
                {
                    var localWishlistItems = await localDb.WishlistItems.Include(w => w.Book).ToListAsync();
                    var localWishlistByUser = localWishlistItems.GroupBy(w => w.UserId).ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var (localUserId, serverUserId) in userMapping)
                    {
                        var sWishlistItems = await serverDb.WishlistItems.Where(w => w.UserId == serverUserId).ToListAsync();

                        if (localWishlistByUser.TryGetValue(localUserId, out var lItems) && lItems.Any())
                        {
                            var activeServerBookIds = new HashSet<int>();

                            foreach (var lItem in lItems)
                            {
                                var sBook = ResolveServerBook(lItem.BookId);
                                if (sBook == null) continue;

                                activeServerBookIds.Add(sBook.Id);

                                var existingSWishlist = sWishlistItems.FirstOrDefault(w => w.BookId == sBook.Id);
                                if (existingSWishlist == null)
                                {
                                    await serverDb.WishlistItems.AddAsync(new WishlistItem
                                    {
                                        UserId = serverUserId,
                                        BookId = sBook.Id,
                                        CreatedAt = lItem.CreatedAt
                                    });
                                    syncedWishlistItems++;
                                }
                            }

                            var itemsToRemove = sWishlistItems.Where(w => !activeServerBookIds.Contains(w.BookId)).ToList();
                            if (itemsToRemove.Any())
                            {
                                serverDb.WishlistItems.RemoveRange(itemsToRemove);
                            }
                        }
                        else
                        {
                            if (sWishlistItems.Any())
                            {
                                serverDb.WishlistItems.RemoveRange(sWishlistItems);
                            }
                        }
                    }

                    await serverDb.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync wishlist items to server database.");
                }

                // 7. PUSH / SYNC: Offline Orders to Server Database
                try
                {
                    var localOrders = await localDb.OrderHeaders
                        .Include(o => o.OrderDetails)
                        .Where(o => !string.IsNullOrEmpty(o.ClientSyncId))
                        .ToListAsync();

                    if (localOrders.Any())
                    {
                        var localSyncIds = localOrders.Select(o => o.ClientSyncId).ToList();
                        var existingServerSyncIds = await serverDb.OrderHeaders
                            .Where(o => localSyncIds.Contains(o.ClientSyncId))
                            .Select(o => o.ClientSyncId)
                            .ToListAsync();

                        var unsyncedOrders = localOrders
                            .Where(o => !existingServerSyncIds.Contains(o.ClientSyncId))
                            .ToList();

                        foreach (var localOrder in unsyncedOrders)
                        {
                            string targetUserId = userMapping.TryGetValue(localOrder.UserId, out var mappedId) ? mappedId : localOrder.UserId;

                            var serverOrder = new OrderHeader
                            {
                                UserId = targetUserId,
                                Name = localOrder.Name,
                                PhoneNumber = localOrder.PhoneNumber,
                                StreetAddress = localOrder.StreetAddress,
                                City = localOrder.City,
                                PostalCode = localOrder.PostalCode,
                                OrderDate = localOrder.OrderDate,
                                ShippingDate = localOrder.ShippingDate,
                                OrderStatus = localOrder.OrderStatus,
                                PaymentStatus = localOrder.PaymentStatus,
                                CouponCode = localOrder.CouponCode,
                                DiscountAmount = localOrder.DiscountAmount,
                                OrderTotal = localOrder.OrderTotal,
                                TrackingNumber = localOrder.TrackingNumber,
                                Carrier = localOrder.Carrier,
                                ClientSyncId = localOrder.ClientSyncId
                            };

                            await serverDb.OrderHeaders.AddAsync(serverOrder);
                            await serverDb.SaveChangesAsync();

                            foreach (var detail in localOrder.OrderDetails)
                            {
                                int serverBookId = detail.BookId;
                                var sBook = ResolveServerBook(detail.BookId);
                                if (sBook != null)
                                {
                                    serverBookId = sBook.Id;
                                    sBook.StockQuantity -= detail.Count;
                                    if (sBook.StockQuantity < 0) sBook.StockQuantity = 0;
                                }

                                var serverDetail = new OrderDetail
                                {
                                    OrderHeaderId = serverOrder.Id,
                                    BookId = serverBookId,
                                    Count = detail.Count,
                                    Price = detail.Price
                                };
                                await serverDb.OrderDetails.AddAsync(serverDetail);
                            }

                            await serverDb.SaveChangesAsync();
                            pushedOrders++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to push offline orders to server database.");
                }

                // 8. PUSH / SYNC: Reviews to Server Database
                try
                {
                    var localReviews = await localDb.BookReviews.Include(r => r.Book).ToListAsync();

                    foreach (var lRev in localReviews)
                    {
                        var sBook = ResolveServerBook(lRev.BookId);
                        if (sBook == null) continue;

                        string targetUserId = userMapping.TryGetValue(lRev.UserId, out var mappedId) ? mappedId : lRev.UserId;

                        var existingServerRev = await serverDb.BookReviews
                            .FirstOrDefaultAsync(r => r.BookId == sBook.Id && r.UserId == targetUserId);

                        if (existingServerRev == null)
                        {
                            await serverDb.BookReviews.AddAsync(new BookReview
                            {
                                BookId = sBook.Id,
                                UserId = targetUserId,
                                Rating = lRev.Rating,
                                Comment = lRev.Comment,
                                ReviewDate = lRev.ReviewDate
                            });
                            pushedReviews++;
                        }
                    }
                    if (pushedReviews > 0)
                    {
                        await serverDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to push reviews to server database.");
                }

                _currentStatus.IsServerOnline = true;
                _currentStatus.LastSyncTime = DateTime.UtcNow;
                _currentStatus.LastSyncMessage = $"Synchronized with Server Database: {syncedCartItems} cart item(s), {pushedOrders} order(s), {pushedReviews} review(s).";

                return new SyncSummaryResult
                {
                    Success = true,
                    IsConnected = true,
                    PulledBooksCount = pulledBooks,
                    PulledCategoriesCount = pulledCategories,
                    PushedOrdersCount = pushedOrders,
                    PushedReviewsCount = pushedReviews,
                    Message = _currentStatus.LastSyncMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Server Database Sync.");
                _currentStatus.IsServerOnline = false;
                _currentStatus.LastSyncMessage = $"Sync error: {ex.Message}";
                return new SyncSummaryResult
                {
                    Success = false,
                    IsConnected = false,
                    Message = ex.Message
                };
            }
            finally
            {
                _syncLock.Release();
            }
        }
    }
}
