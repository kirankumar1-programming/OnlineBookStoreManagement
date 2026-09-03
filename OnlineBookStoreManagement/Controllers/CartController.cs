using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using OnlineBookStoreManagement.Services;
using System.Security.Claims;

namespace OnlineBookStoreManagement.Controllers
{
    public class CartController : Controller
    {
        private const string SessionCouponKey = "AppliedCouponCode";

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSenderService _emailSender;
        private readonly IServerDatabaseSyncService? _serverDbSync;
        private readonly ILogger<CartController> _logger;

        public CartController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSenderService emailSender,
            ILogger<CartController> logger,
            IServerDatabaseSyncService? serverDbSync = null)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
            _serverDbSync = serverDbSync;
        }

        private void TriggerBackgroundSync()
        {
            if (_serverDbSync != null)
            {
                _ = Task.Run(() => _serverDbSync.SyncWithServerDatabaseAsync());
            }
        }

        private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private decimal CalculateCouponDiscount(Coupon coupon, decimal subtotal)
        {
            if (!coupon.IsActive || subtotal < coupon.MinimumOrderAmount)
                return 0m;

            if (coupon.StartDate.HasValue && coupon.StartDate.Value > DateTime.UtcNow)
                return 0m;

            if (coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < DateTime.UtcNow)
                return 0m;

            if (coupon.UsageLimit.HasValue && coupon.TimesUsed >= coupon.UsageLimit.Value)
                return 0m;

            decimal discount = 0m;
            if (coupon.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
            {
                discount = Math.Round((subtotal * coupon.DiscountValue) / 100m, 2);
                if (coupon.MaximumDiscountAmount.HasValue && discount > coupon.MaximumDiscountAmount.Value)
                {
                    discount = coupon.MaximumDiscountAmount.Value;
                }
            }
            else if (coupon.DiscountType.Equals("Flat", StringComparison.OrdinalIgnoreCase) || coupon.DiscountType.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
            {
                discount = Math.Min(subtotal, coupon.DiscountValue);
            }

            return discount;
        }

        private async Task<(string? code, decimal discount, string? infoMessage)> ProcessSessionCouponAsync(decimal subtotal)
        {
            var code = HttpContext.Session.GetString(SessionCouponKey);
            if (string.IsNullOrWhiteSpace(code)) return (null, 0m, null);

            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == code.ToUpper() && c.IsActive);
            if (coupon == null)
            {
                HttpContext.Session.Remove(SessionCouponKey);
                return (null, 0m, "Previously applied coupon is no longer active.");
            }

            if (coupon.StartDate.HasValue && coupon.StartDate.Value > DateTime.UtcNow)
            {
                HttpContext.Session.Remove(SessionCouponKey);
                return (null, 0m, $"Coupon '{coupon.Code}' is not active until {coupon.StartDate.Value:yyyy-MM-dd}.");
            }

            if (coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < DateTime.UtcNow)
            {
                HttpContext.Session.Remove(SessionCouponKey);
                return (null, 0m, $"Coupon '{coupon.Code}' has expired.");
            }

            if (coupon.UsageLimit.HasValue && coupon.TimesUsed >= coupon.UsageLimit.Value)
            {
                HttpContext.Session.Remove(SessionCouponKey);
                return (null, 0m, $"Coupon '{coupon.Code}' has reached its maximum usage limit.");
            }

            if (subtotal < coupon.MinimumOrderAmount)
            {
                return (coupon.Code, 0m, $"Coupon '{coupon.Code}' requires a minimum subtotal of ₹{coupon.MinimumOrderAmount:N2}.");
            }

            decimal discount = CalculateCouponDiscount(coupon, subtotal);
            return (coupon.Code, discount, $"Coupon '{coupon.Code}' applied successfully!");
        }

        private List<string> ValidateCartStock(IEnumerable<ShoppingCartItem> cartItems)
        {
            var errors = new List<string>();
            foreach (var item in cartItems)
            {
                if (item.Book == null || item.Book.StockQuantity <= 0)
                {
                    errors.Add($"\"{item.Book?.Title ?? "Book"}\" is currently out of stock!");
                }
                else if (item.Count > item.Book.StockQuantity)
                {
                    errors.Add($"Only {item.Book.StockQuantity} copy(ies) of \"{item.Book.Title}\" in stock, but you requested {item.Count}.");
                }
            }
            return errors;
        }

        private async Task<List<Coupon>> GetAvailableCouponsAsync()
        {
            var now = DateTime.UtcNow;
            return await _db.Coupons
                .Where(c => c.IsActive
                    && (!c.StartDate.HasValue || c.StartDate.Value <= now)
                    && (!c.ExpiryDate.HasValue || c.ExpiryDate.Value >= now)
                    && (!c.UsageLimit.HasValue || c.TimesUsed < c.UsageLimit.Value))
                .OrderBy(c => c.Code)
                .ToListAsync();
        }

        // GET: /Cart
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Cart" });
            }

            var cartItems = await _db.ShoppingCartItems
                .Include(i => i.Book)
                .ThenInclude(b => b!.Category)
                .Where(i => i.UserId == userId)
                .ToListAsync();

            var viewModel = new ShoppingCartViewModel
            {
                CartItems = cartItems,
                StockValidationErrors = ValidateCartStock(cartItems),
                AvailableCoupons = await GetAvailableCouponsAsync()
            };

            var (appliedCode, discountAmount, couponInfo) = await ProcessSessionCouponAsync(viewModel.SubTotal);
            viewModel.CouponCode = appliedCode;
            viewModel.DiscountAmount = discountAmount;
            if (!string.IsNullOrEmpty(couponInfo))
            {
                if (discountAmount > 0) viewModel.CouponSuccessMessage = couponInfo;
                else viewModel.CouponErrorMessage = couponInfo;
            }

            return View(viewModel);
        }

        // POST: /Cart/AddToCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(int bookId, int quantity = 1)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Please sign in to add items to your cart.";
                return RedirectToAction("Login", "Account", new { returnUrl = $"/Home/Details/{bookId}" });
            }

            if (quantity <= 0) quantity = 1;

            var book = await _db.Books.FindAsync(bookId);
            if (book == null) return NotFound();

            if (book.StockQuantity <= 0)
            {
                TempData["ErrorMessage"] = $"Sorry, \"{book.Title}\" is currently out of stock!";
                return RedirectToAction("Details", "Home", new { id = bookId });
            }

            if (book.StockQuantity < quantity)
            {
                TempData["ErrorMessage"] = $"Sorry, only {book.StockQuantity} copy(ies) left in stock!";
                return RedirectToAction("Details", "Home", new { id = bookId });
            }

            var cartItem = await _db.ShoppingCartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.BookId == bookId);

            if (cartItem == null)
            {
                cartItem = new ShoppingCartItem
                {
                    UserId = userId,
                    BookId = bookId,
                    Count = quantity
                };
                await _db.ShoppingCartItems.AddAsync(cartItem);
            }
            else
            {
                if (cartItem.Count + quantity > book.StockQuantity)
                {
                    TempData["ErrorMessage"] = $"Cannot add more. Stock limit of {book.StockQuantity} reached.";
                    return RedirectToAction("Details", "Home", new { id = bookId });
                }
                cartItem.Count += quantity;
            }

            await _db.SaveChangesAsync();
            TriggerBackgroundSync();
            TempData["SuccessMessage"] = $"\"{book.Title}\" added to your cart!";

            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/UpdateQuantity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartId, int quantity)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Cart" });
            }

            var cartItem = await _db.ShoppingCartItems
                .Include(c => c.Book)
                .FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);

            if (cartItem != null && cartItem.Book != null)
            {
                if (quantity <= 0)
                {
                    _db.ShoppingCartItems.Remove(cartItem);
                    TempData["SuccessMessage"] = $"\"{cartItem.Book.Title}\" removed from cart.";
                }
                else if (quantity > cartItem.Book.StockQuantity)
                {
                    cartItem.Count = cartItem.Book.StockQuantity;
                    TempData["ErrorMessage"] = $"Quantity for \"{cartItem.Book.Title}\" set to max available stock ({cartItem.Book.StockQuantity} unit(s)).";
                }
                else
                {
                    cartItem.Count = quantity;
                    TempData["SuccessMessage"] = $"Updated quantity for \"{cartItem.Book.Title}\" to {quantity}.";
                }
                await _db.SaveChangesAsync();
                TriggerBackgroundSync();
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/Plus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Plus(int cartId)
        {
            var userId = GetUserId();
            var cartItem = await _db.ShoppingCartItems.Include(c => c.Book).FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null && cartItem.Book != null)
            {
                if (cartItem.Count < cartItem.Book.StockQuantity)
                {
                    cartItem.Count += 1;
                    await _db.SaveChangesAsync();
                    TriggerBackgroundSync();
                    TempData["SuccessMessage"] = $"Updated quantity for \"{cartItem.Book.Title}\" to {cartItem.Count}.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Max available stock for \"{cartItem.Book.Title}\" is {cartItem.Book.StockQuantity}.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/Minus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Minus(int cartId)
        {
            var userId = GetUserId();
            var cartItem = await _db.ShoppingCartItems.Include(c => c.Book).FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null && cartItem.Book != null)
            {
                if (cartItem.Count <= 1)
                {
                    _db.ShoppingCartItems.Remove(cartItem);
                    TempData["SuccessMessage"] = $"\"{cartItem.Book.Title}\" removed from cart.";
                }
                else
                {
                    cartItem.Count -= 1;
                    TempData["SuccessMessage"] = $"Updated quantity for \"{cartItem.Book.Title}\" to {cartItem.Count}.";
                }
                await _db.SaveChangesAsync();
                TriggerBackgroundSync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/Remove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int cartId)
        {
            var userId = GetUserId();
            var cartItem = await _db.ShoppingCartItems.Include(c => c.Book).FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null)
            {
                var title = cartItem.Book?.Title ?? "Item";
                _db.ShoppingCartItems.Remove(cartItem);
                await _db.SaveChangesAsync();
                TriggerBackgroundSync();
                TempData["SuccessMessage"] = $"\"{title}\" removed from shopping cart.";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/MoveToWishlist
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToWishlist(int cartId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Cart" });
            }

            var cartItem = await _db.ShoppingCartItems.Include(c => c.Book).FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null)
            {
                var bookTitle = cartItem.Book?.Title ?? "Book";
                var alreadyInWishlist = await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.BookId == cartItem.BookId);
                if (!alreadyInWishlist)
                {
                    var wishlistItem = new WishlistItem
                    {
                        UserId = userId,
                        BookId = cartItem.BookId,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _db.WishlistItems.AddAsync(wishlistItem);
                }

                _db.ShoppingCartItems.Remove(cartItem);
                await _db.SaveChangesAsync();
                TriggerBackgroundSync();
                TempData["SuccessMessage"] = $"\"{bookTitle}\" moved to your wishlist!";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/ApplyCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyCoupon(string couponCode)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/Cart" });
            }

            if (string.IsNullOrWhiteSpace(couponCode))
            {
                TempData["ErrorMessage"] = "Please enter a valid coupon code.";
                return RedirectToAction(nameof(Index));
            }

            couponCode = couponCode.Trim().ToUpper();
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == couponCode);

            if (coupon == null || !coupon.IsActive)
            {
                TempData["ErrorMessage"] = $"Invalid or inactive coupon code '{couponCode}'.";
                return RedirectToAction(nameof(Index));
            }

            if (coupon.StartDate.HasValue && coupon.StartDate.Value > DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = $"Coupon code '{couponCode}' is not valid until {coupon.StartDate.Value:yyyy-MM-dd}.";
                return RedirectToAction(nameof(Index));
            }

            if (coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = $"Coupon code '{couponCode}' has expired.";
                return RedirectToAction(nameof(Index));
            }

            if (coupon.UsageLimit.HasValue && coupon.TimesUsed >= coupon.UsageLimit.Value)
            {
                TempData["ErrorMessage"] = $"Coupon code '{couponCode}' has reached its maximum usage limit.";
                return RedirectToAction(nameof(Index));
            }

            var cartItems = await _db.ShoppingCartItems
                .Include(i => i.Book)
                .Where(i => i.UserId == userId)
                .ToListAsync();

            decimal subtotal = cartItems.Sum(i => (i.Book != null ? i.Book.Price : 0m) * i.Count);

            if (subtotal < coupon.MinimumOrderAmount)
            {
                TempData["ErrorMessage"] = $"Coupon '{coupon.Code}' requires a minimum subtotal of ₹{coupon.MinimumOrderAmount:N2}.";
                return RedirectToAction(nameof(Index));
            }

            HttpContext.Session.SetString(SessionCouponKey, coupon.Code);
            decimal discount = CalculateCouponDiscount(coupon, subtotal);
            TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' applied! You saved ₹{discount:N2}.";

            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/RemoveCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCoupon()
        {
            HttpContext.Session.Remove(SessionCouponKey);
            TempData["SuccessMessage"] = "Coupon removed.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Cart/Checkout
        [Authorize]
        public async Task<IActionResult> Checkout()
        {
            var userId = GetUserId();
            var user = await _userManager.FindByIdAsync(userId!);

            var cartItems = await _db.ShoppingCartItems
                .Include(i => i.Book)
                .Where(i => i.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["ErrorMessage"] = "Your shopping cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            var vm = new ShoppingCartViewModel
            {
                CartItems = cartItems,
                OrderHeader = new OrderHeader
                {
                    Name = user?.FullName ?? "",
                    StreetAddress = user?.Address ?? "",
                    City = user?.City ?? "",
                    PostalCode = user?.PostalCode ?? "",
                    PhoneNumber = user?.PhoneNumber ?? ""
                },
                StockValidationErrors = ValidateCartStock(cartItems)
            };

            var (appliedCode, discountAmount, couponInfo) = await ProcessSessionCouponAsync(vm.SubTotal);
            vm.CouponCode = appliedCode;
            vm.DiscountAmount = discountAmount;

            if (vm.StockValidationErrors.Any())
            {
                TempData["ErrorMessage"] = "Some items in your cart have stock restrictions. Please review before proceeding.";
            }

            return View(vm);
        }

        // POST: /Cart/Checkout (Process Payment & Place Order)
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(ShoppingCartViewModel vm, string? paymentType = null)
        {
            var userId = GetUserId();
            var cartItems = await _db.ShoppingCartItems
                .Include(i => i.Book)
                .Where(i => i.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["ErrorMessage"] = "Your cart is empty.";
                return RedirectToAction("Index", "Home");
            }

            vm.CartItems = cartItems;

            // Assign UserId and clear non-form ModelState validations
            vm.OrderHeader.UserId = userId!;
            ModelState.Remove("OrderHeader.UserId");
            ModelState.Remove("OrderHeader.User");

            // Server-side robust checkout validation (Address & Contact formatting)
            if (string.IsNullOrWhiteSpace(vm.OrderHeader.Name))
            {
                ModelState.AddModelError("OrderHeader.Name", "Recipient Full Name is required.");
            }
            if (string.IsNullOrWhiteSpace(vm.OrderHeader.PhoneNumber) || vm.OrderHeader.PhoneNumber.Trim().Length < 8)
            {
                ModelState.AddModelError("OrderHeader.PhoneNumber", "Valid phone number (minimum 8 digits) is required.");
            }
            if (string.IsNullOrWhiteSpace(vm.OrderHeader.StreetAddress))
            {
                ModelState.AddModelError("OrderHeader.StreetAddress", "Street Address is required.");
            }
            if (string.IsNullOrWhiteSpace(vm.OrderHeader.City))
            {
                ModelState.AddModelError("OrderHeader.City", "City is required.");
            }
            if (string.IsNullOrWhiteSpace(vm.OrderHeader.PostalCode) || vm.OrderHeader.PostalCode.Trim().Length < 4)
            {
                ModelState.AddModelError("OrderHeader.PostalCode", "Valid Postal / PIN Code is required.");
            }

            // Real-time Stock Validation Guard
            var stockErrors = ValidateCartStock(cartItems);
            if (stockErrors.Any())
            {
                foreach (var err in stockErrors)
                {
                    ModelState.AddModelError(string.Empty, err);
                }
                vm.StockValidationErrors = stockErrors;
                var (code, discount, _) = await ProcessSessionCouponAsync(vm.SubTotal);
                vm.CouponCode = code;
                vm.DiscountAmount = discount;
                TempData["ErrorMessage"] = "Please adjust cart quantities to match available stock before placing your order.";
                return View(vm);
            }

            if (!ModelState.IsValid)
            {
                var (code, discount, _) = await ProcessSessionCouponAsync(vm.SubTotal);
                vm.CouponCode = code;
                vm.DiscountAmount = discount;
                return View(vm);
            }

            // Calculate Discount & Order Totals
            var (appliedCoupon, couponDiscount, _) = await ProcessSessionCouponAsync(vm.SubTotal);
            vm.CouponCode = appliedCoupon;
            vm.DiscountAmount = couponDiscount;

            // Create Order Header
            var orderHeader = vm.OrderHeader;
            orderHeader.OrderDate = DateTime.UtcNow;
            orderHeader.OrderStatus = "Pending";
            if (string.IsNullOrEmpty(orderHeader.ClientSyncId))
            {
                orderHeader.ClientSyncId = "ORD-" + Guid.NewGuid().ToString("N");
            }
            
            string methodKey = (paymentType ?? "upi").ToLower();
            string paymentMethodTitle = methodKey switch
            {
                "card" => "Approved (Credit/Debit Card)",
                "cod" => "Pending (Cash on Delivery)",
                _ => "Approved (UPI / Net Banking)"
            };
            orderHeader.PaymentStatus = paymentMethodTitle;
            orderHeader.CouponCode = appliedCoupon;
            orderHeader.DiscountAmount = couponDiscount;
            orderHeader.OrderTotal = vm.GrandTotal;

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.OrderHeaders.AddAsync(orderHeader);
                await _db.SaveChangesAsync();

                // Create Order Details & Deduct Stock Atomically
                foreach (var item in cartItems)
                {
                    var detail = new OrderDetail
                    {
                        OrderHeaderId = orderHeader.Id,
                        BookId = item.BookId,
                        Count = item.Count,
                        Price = item.Book!.Price
                    };
                    await _db.OrderDetails.AddAsync(detail);

                    // Deduct stock
                    if (item.Book != null)
                    {
                        item.Book.StockQuantity -= item.Count;
                        if (item.Book.StockQuantity < 0) item.Book.StockQuantity = 0;

                        if (item.Book.StockQuantity < 5)
                        {
                            _logger.LogWarning("LOW-STOCK ALERT: Book '{Title}' (ID: {BookId}) stock dropped to {StockQuantity} units (< 5 units) after Order #{OrderId}.",
                                item.Book.Title, item.Book.Id, item.Book.StockQuantity, orderHeader.Id);
                        }
                    }
                }

                // Increment Coupon TimesUsed count if applicable
                if (!string.IsNullOrWhiteSpace(appliedCoupon))
                {
                    var couponEntity = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == appliedCoupon.ToUpper());
                    if (couponEntity != null)
                    {
                        couponEntity.TimesUsed += 1;
                    }
                }

                // Clear Cart items from Database
                _db.ShoppingCartItems.RemoveRange(cartItems);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                // Clear applied coupon from Session
                HttpContext.Session.Remove(SessionCouponKey);

                // Trigger background server sync
                TriggerBackgroundSync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to complete checkout order for user {UserId}", userId);
                TempData["ErrorMessage"] = "An error occurred while processing your order. Please try again.";
                return View(vm);
            }

            // Fetch order user email and send Order Confirmation Email via SMTP
            var orderUser = await _userManager.FindByIdAsync(userId!);
            var recipientEmail = orderUser?.Email;
            if (!string.IsNullOrEmpty(recipientEmail))
            {
                var createdOrderDetails = await _db.OrderDetails
                    .Include(d => d.Book)
                    .Where(d => d.OrderHeaderId == orderHeader.Id)
                    .ToListAsync();

                _ = Task.Run(() => _emailSender.SendOrderConfirmationEmailAsync(recipientEmail, orderHeader, createdOrderDetails));
            }

            TempData["SuccessMessage"] = "Order placed successfully!";

            return RedirectToAction(nameof(OrderConfirmation), new { id = orderHeader.Id });
        }

        // GET: /Cart/OrderConfirmation/5
        [Authorize]
        public async Task<IActionResult> OrderConfirmation(int id)
        {
            var userId = GetUserId();
            var orderHeader = await _db.OrderHeaders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (orderHeader == null)
            {
                TempData["ErrorMessage"] = "Order not found or you do not have permission to view it.";
                return RedirectToAction("Index", "Home");
            }

            return View(orderHeader);
        }
    }
}
