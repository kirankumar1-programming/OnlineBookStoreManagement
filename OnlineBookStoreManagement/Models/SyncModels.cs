using System;
using System.Collections.Generic;

namespace OnlineBookStoreManagement.Models
{
    public class SyncCatalogBookDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ISBN { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CoverImageUrl { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public string Description { get; set; } = string.Empty;
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    public class SyncCatalogCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }

    public class SyncCatalogResponse
    {
        public bool Success { get; set; } = true;
        public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
        public List<SyncCatalogBookDto> Books { get; set; } = new();
        public List<SyncCatalogCategoryDto> Categories { get; set; } = new();
    }

    public class OfflineOrderItemDto
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Price { get; set; }
    }

    public class OfflineOrderDto
    {
        public string ClientSyncId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string StreetAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string? PaymentType { get; set; }
        public string? CouponCode { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal OrderTotal { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public List<OfflineOrderItemDto> Items { get; set; } = new();
    }

    public class OfflineReviewDto
    {
        public string ClientSyncId { get; set; } = string.Empty;
        public int BookId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime ReviewDate { get; set; } = DateTime.UtcNow;
    }

    public class OfflineCartItemDto
    {
        public int BookId { get; set; }
        public int Count { get; set; }
    }

    public class OfflineWishlistItemDto
    {
        public int BookId { get; set; }
    }

    public class SyncBatchRequest
    {
        public string? BatchId { get; set; }
        public List<OfflineOrderDto> Orders { get; set; } = new();
        public List<OfflineReviewDto> Reviews { get; set; } = new();
        public List<OfflineCartItemDto>? CartItems { get; set; }
        public List<OfflineWishlistItemDto>? WishlistItems { get; set; }
    }

    public class SyncResultItem
    {
        public string Type { get; set; } = string.Empty; // "Order", "Review", "Cart", "Wishlist"
        public string ClientSyncId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Success", "Skipped", "Failed", "Conflict"
        public string? ServerId { get; set; }
        public string? Message { get; set; }
    }

    public class SyncBatchResponse
    {
        public bool Success { get; set; } = true;
        public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
        public List<SyncResultItem> Results { get; set; } = new();
        public string SummaryMessage { get; set; } = string.Empty;
        public int SyncedOrdersCount { get; set; }
        public int SyncedReviewsCount { get; set; }
    }

    public class SyncStatusDto
    {
        public bool IsServerOnline { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public int PendingLocalOrdersCount { get; set; }
        public int PendingLocalReviewsCount { get; set; }
        public string LastSyncMessage { get; set; } = string.Empty;
        public string ServerDatabaseProvider { get; set; } = string.Empty;
    }

    public class SyncSummaryResult
    {
        public bool Success { get; set; } = true;
        public bool IsConnected { get; set; }
        public int PulledBooksCount { get; set; }
        public int PulledCategoriesCount { get; set; }
        public int PushedOrdersCount { get; set; }
        public int PushedReviewsCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
