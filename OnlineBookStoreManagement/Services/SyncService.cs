using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineBookStoreManagement.Services
{
    public class SyncService : ISyncService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSenderService _emailSender;
        private readonly ILogger<SyncService> _logger;

        public SyncService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSenderService emailSender,
            ILogger<SyncService> logger)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<SyncCatalogResponse> GetCatalogForSyncAsync()
        {
            var books = await _db.Books
                .Include(b => b.Category)
                .Include(b => b.Reviews)
                .OrderBy(b => b.Title)
                .Select(b => new SyncCatalogBookDto
                {
                    Id = b.Id,
                    Title = b.Title,
                    Author = b.Author,
                    ISBN = b.ISBN,
                    Price = b.Price,
                    CategoryId = b.CategoryId,
                    CategoryName = b.Category != null ? b.Category.Name : "General",
                    CoverImageUrl = string.IsNullOrEmpty(b.CoverImageUrl) ? "/images/default-book.svg" : b.CoverImageUrl,
                    StockQuantity = b.StockQuantity,
                    Description = b.Description,
                    AverageRating = b.Reviews.Any() ? Math.Round(b.Reviews.Average(r => (double)r.Rating), 1) : 0,
                    ReviewCount = b.Reviews.Count
                })
                .ToListAsync();

            var categories = await _db.Categories
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .Select(c => new SyncCatalogCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    DisplayOrder = c.DisplayOrder
                })
                .ToListAsync();

            return new SyncCatalogResponse
            {
                Success = true,
                ServerTimestamp = DateTime.UtcNow,
                Books = books,
                Categories = categories
            };
        }

        public async Task<SyncBatchResponse> ProcessBatchSyncAsync(SyncBatchRequest request, string? currentUserId)
        {
            var response = new SyncBatchResponse
            {
                Success = true,
                ServerTimestamp = DateTime.UtcNow,
                Results = new List<SyncResultItem>()
            };

            if (request == null)
            {
                response.Success = false;
                response.SummaryMessage = "Empty sync batch request.";
                return response;
            }

            // Fallback userId if current user is not logged in
            string? effectiveUserId = currentUserId;
            if (string.IsNullOrEmpty(effectiveUserId))
            {
                var defaultUser = await _db.Users.FirstOrDefaultAsync();
                effectiveUserId = defaultUser?.Id;
            }

            // 1. Process Offline Orders
            if (request.Orders != null && request.Orders.Any())
            {
                foreach (var orderDto in request.Orders)
                {
                    var result = await ProcessSingleOrderAsync(orderDto, effectiveUserId);
                    response.Results.Add(result);
                    if (result.Status == "Success")
                    {
                        response.SyncedOrdersCount++;
                    }
                }
            }

            // 2. Process Offline Reviews
            if (request.Reviews != null && request.Reviews.Any() && !string.IsNullOrEmpty(effectiveUserId))
            {
                foreach (var reviewDto in request.Reviews)
                {
                    var result = await ProcessSingleReviewAsync(reviewDto, effectiveUserId);
                    response.Results.Add(result);
                    if (result.Status == "Success")
                    {
                        response.SyncedReviewsCount++;
                    }
                }
            }

            // 3. Process Cart sync if provided
            if (request.CartItems != null && request.CartItems.Any() && !string.IsNullOrEmpty(effectiveUserId))
            {
                await SyncCartItemsAsync(request.CartItems, effectiveUserId);
            }

            // 4. Process Wishlist sync if provided
            if (request.WishlistItems != null && request.WishlistItems.Any() && !string.IsNullOrEmpty(effectiveUserId))
            {
                await SyncWishlistItemsAsync(request.WishlistItems, effectiveUserId);
            }

            response.SummaryMessage = $"Processed sync batch: {response.SyncedOrdersCount} order(s), {response.SyncedReviewsCount} review(s) successfully synchronized.";
            return response;
        }

        private async Task<SyncResultItem> ProcessSingleOrderAsync(OfflineOrderDto orderDto, string? userId)
        {
            if (string.IsNullOrWhiteSpace(orderDto.ClientSyncId))
            {
                orderDto.ClientSyncId = Guid.NewGuid().ToString();
            }

            // Idempotency check: verify if an order with this ClientSyncId already exists
            var existingOrder = await _db.OrderHeaders
                .FirstOrDefaultAsync(o => o.ClientSyncId == orderDto.ClientSyncId);

            if (existingOrder != null)
            {
                return new SyncResultItem
                {
                    Type = "Order",
                    ClientSyncId = orderDto.ClientSyncId,
                    Status = "Skipped",
                    ServerId = existingOrder.Id.ToString(),
                    Message = $"Order #{existingOrder.Id} was already synced previously."
                };
            }

            if (orderDto.Items == null || !orderDto.Items.Any())
            {
                return new SyncResultItem
                {
                    Type = "Order",
                    ClientSyncId = orderDto.ClientSyncId,
                    Status = "Failed",
                    Message = "Order contains no items."
                };
            }

            if (string.IsNullOrEmpty(userId))
            {
                return new SyncResultItem
                {
                    Type = "Order",
                    ClientSyncId = orderDto.ClientSyncId,
                    Status = "Failed",
                    Message = "User authentication required to sync order."
                };
            }

            var bookIds = orderDto.Items.Select(i => i.BookId).Distinct().ToList();
            var books = await _db.Books.Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);

            // Stock Availability Check
            foreach (var item in orderDto.Items)
            {
                if (!books.TryGetValue(item.BookId, out var book))
                {
                    return new SyncResultItem
                    {
                        Type = "Order",
                        ClientSyncId = orderDto.ClientSyncId,
                        Status = "Failed",
                        Message = $"Book ID {item.BookId} does not exist in store catalog."
                    };
                }

                if (book.StockQuantity < item.Count)
                {
                    return new SyncResultItem
                    {
                        Type = "Order",
                        ClientSyncId = orderDto.ClientSyncId,
                        Status = "Conflict",
                        Message = $"Insufficient stock for '{book.Title}'. Available: {book.StockQuantity}, Requested: {item.Count}."
                    };
                }
            }

            // Transactional Order Placement & Stock Deduction
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                string methodKey = (orderDto.PaymentType ?? "upi").ToLower();
                string paymentStatus = methodKey switch
                {
                    "card" => "Approved (Credit/Debit Card)",
                    "cod" => "Pending (Cash on Delivery)",
                    _ => "Approved (UPI / Net Banking)"
                };

                var orderHeader = new OrderHeader
                {
                    UserId = userId,
                    Name = string.IsNullOrWhiteSpace(orderDto.Name) ? "Guest Customer" : orderDto.Name.Trim(),
                    PhoneNumber = string.IsNullOrWhiteSpace(orderDto.PhoneNumber) ? "0000000000" : orderDto.PhoneNumber.Trim(),
                    StreetAddress = string.IsNullOrWhiteSpace(orderDto.StreetAddress) ? "Not Specified" : orderDto.StreetAddress.Trim(),
                    City = string.IsNullOrWhiteSpace(orderDto.City) ? "Not Specified" : orderDto.City.Trim(),
                    PostalCode = string.IsNullOrWhiteSpace(orderDto.PostalCode) ? "000000" : orderDto.PostalCode.Trim(),
                    OrderDate = orderDto.OrderDate <= DateTime.UtcNow ? orderDto.OrderDate : DateTime.UtcNow,
                    OrderStatus = "Pending",
                    PaymentStatus = paymentStatus,
                    CouponCode = orderDto.CouponCode,
                    DiscountAmount = orderDto.DiscountAmount,
                    OrderTotal = orderDto.OrderTotal,
                    ClientSyncId = orderDto.ClientSyncId
                };

                await _db.OrderHeaders.AddAsync(orderHeader);
                await _db.SaveChangesAsync();

                foreach (var item in orderDto.Items)
                {
                    var book = books[item.BookId];
                    var orderDetail = new OrderDetail
                    {
                        OrderHeaderId = orderHeader.Id,
                        BookId = item.BookId,
                        Count = item.Count,
                        Price = book.Price
                    };
                    await _db.OrderDetails.AddAsync(orderDetail);

                    // Deduct stock
                    book.StockQuantity -= item.Count;
                    if (book.StockQuantity < 0) book.StockQuantity = 0;
                }

                // If coupon applied, increment times used
                if (!string.IsNullOrWhiteSpace(orderDto.CouponCode))
                {
                    var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == orderDto.CouponCode.ToUpper());
                    if (coupon != null)
                    {
                        coupon.TimesUsed += 1;
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send email asynchronously if possible
                var orderUser = await _userManager.FindByIdAsync(userId);
                if (orderUser?.Email != null)
                {
                    var details = await _db.OrderDetails
                        .Include(d => d.Book)
                        .Where(d => d.OrderHeaderId == orderHeader.Id)
                        .ToListAsync();

                    _ = Task.Run(() => _emailSender.SendOrderConfirmationEmailAsync(orderUser.Email, orderHeader, details));
                }

                return new SyncResultItem
                {
                    Type = "Order",
                    ClientSyncId = orderDto.ClientSyncId,
                    Status = "Success",
                    ServerId = orderHeader.Id.ToString(),
                    Message = $"Order #{orderHeader.Id} synced and saved to server database."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to sync offline order {ClientSyncId}", orderDto.ClientSyncId);
                return new SyncResultItem
                {
                    Type = "Order",
                    ClientSyncId = orderDto.ClientSyncId,
                    Status = "Failed",
                    Message = $"Sync error: {ex.Message}"
                };
            }
        }

        private async Task<SyncResultItem> ProcessSingleReviewAsync(OfflineReviewDto reviewDto, string userId)
        {
            if (reviewDto.Rating < 1 || reviewDto.Rating > 5)
            {
                return new SyncResultItem
                {
                    Type = "Review",
                    ClientSyncId = reviewDto.ClientSyncId,
                    Status = "Failed",
                    Message = "Rating must be between 1 and 5."
                };
            }

            var book = await _db.Books.FindAsync(reviewDto.BookId);
            if (book == null)
            {
                return new SyncResultItem
                {
                    Type = "Review",
                    ClientSyncId = reviewDto.ClientSyncId,
                    Status = "Failed",
                    Message = $"Book #{reviewDto.BookId} not found."
                };
            }

            var sanitizedComment = reviewDto.Comment?.Trim() ?? string.Empty;
            if (sanitizedComment.Length > 1000)
            {
                sanitizedComment = sanitizedComment.Substring(0, 1000);
            }

            var existingReview = await _db.BookReviews
                .FirstOrDefaultAsync(r => r.BookId == reviewDto.BookId && r.UserId == userId);

            if (existingReview != null)
            {
                existingReview.Rating = reviewDto.Rating;
                existingReview.Comment = sanitizedComment;
                existingReview.ReviewDate = reviewDto.ReviewDate <= DateTime.UtcNow ? reviewDto.ReviewDate : DateTime.UtcNow;
                _db.BookReviews.Update(existingReview);
            }
            else
            {
                var newReview = new BookReview
                {
                    BookId = reviewDto.BookId,
                    UserId = userId,
                    Rating = reviewDto.Rating,
                    Comment = sanitizedComment,
                    ReviewDate = reviewDto.ReviewDate <= DateTime.UtcNow ? reviewDto.ReviewDate : DateTime.UtcNow
                };
                await _db.BookReviews.AddAsync(newReview);
            }

            await _db.SaveChangesAsync();

            return new SyncResultItem
            {
                Type = "Review",
                ClientSyncId = reviewDto.ClientSyncId,
                Status = "Success",
                ServerId = reviewDto.BookId.ToString(),
                Message = $"Review for '{book.Title}' synchronized."
            };
        }

        private async Task SyncCartItemsAsync(List<OfflineCartItemDto> cartItems, string userId)
        {
            foreach (var item in cartItems)
            {
                if (item.Count <= 0) continue;
                var book = await _db.Books.FindAsync(item.BookId);
                if (book == null) continue;

                var existingCart = await _db.ShoppingCartItems
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.BookId == item.BookId);

                if (existingCart == null)
                {
                    await _db.ShoppingCartItems.AddAsync(new ShoppingCartItem
                    {
                        UserId = userId,
                        BookId = item.BookId,
                        Count = Math.Min(item.Count, book.StockQuantity)
                    });
                }
                else
                {
                    existingCart.Count = Math.Min(item.Count, book.StockQuantity);
                }
            }
            await _db.SaveChangesAsync();
        }

        private async Task SyncWishlistItemsAsync(List<OfflineWishlistItemDto> wishlistItems, string userId)
        {
            foreach (var item in wishlistItems)
            {
                var book = await _db.Books.FindAsync(item.BookId);
                if (book == null) continue;

                var existing = await _db.WishlistItems
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.BookId == item.BookId);

                if (existing == null)
                {
                    await _db.WishlistItems.AddAsync(new WishlistItem
                    {
                        UserId = userId,
                        BookId = item.BookId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}
