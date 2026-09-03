namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class WishlistViewModel
    {
        public List<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();

        public int TotalItems => WishlistItems.Count;
        public int InStockItemsCount => WishlistItems.Count(w => w.Book != null && w.Book.StockQuantity > 0);
        public int OutOfStockItemsCount => WishlistItems.Count(w => w.Book == null || w.Book.StockQuantity <= 0);
        public decimal TotalEstimatedValue => WishlistItems.Sum(w => w.Book?.Price ?? 0m);
    }
}
