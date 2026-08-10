namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class ShoppingCartViewModel
    {
        public IEnumerable<ShoppingCartItem> CartItems { get; set; } = new List<ShoppingCartItem>();
        public OrderHeader OrderHeader { get; set; } = new OrderHeader();
        public decimal SubTotal => CartItems.Sum(i => i.Price);
        public decimal EstimatedTax => SubTotal * 0.08m; // 8% GST/Tax
        public decimal ShippingFee => SubTotal >= 999.00m || SubTotal == 0 ? 0.00m : 99.00m; // Free shipping over ₹999
        public decimal GrandTotal => SubTotal + EstimatedTax + ShippingFee;
    }
}
