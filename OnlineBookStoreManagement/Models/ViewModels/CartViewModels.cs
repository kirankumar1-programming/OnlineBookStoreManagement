namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class ShoppingCartViewModel
    {
        public IEnumerable<ShoppingCartItem> CartItems { get; set; } = new List<ShoppingCartItem>();
        public OrderHeader OrderHeader { get; set; } = new OrderHeader();

        public string? CouponCode { get; set; }
        public decimal DiscountAmount { get; set; } = 0.00m;
        public string? CouponErrorMessage { get; set; }
        public string? CouponSuccessMessage { get; set; }

        public List<string> StockValidationErrors { get; set; } = new List<string>();

        public decimal SubTotal => CartItems.Sum(i => i.Price);
        public decimal SubTotalAfterDiscount => Math.Max(0m, SubTotal - DiscountAmount);
        public decimal EstimatedTax => Math.Round(SubTotalAfterDiscount * 0.08m, 2); // 8% GST/Tax
        public decimal ShippingFee => SubTotalAfterDiscount >= 999.00m || SubTotalAfterDiscount == 0 ? 0.00m : 99.00m; // Free shipping over ₹999
        public decimal GrandTotal => SubTotalAfterDiscount + EstimatedTax + ShippingFee;
    }
}
