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
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSenderService _emailSender;

        public CartController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailSenderService emailSender)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

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
                CartItems = cartItems
            };

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

            var book = await _db.Books.FindAsync(bookId);
            if (book == null) return NotFound();

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
            TempData["SuccessMessage"] = $"\"{book.Title}\" added to your cart!";

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
            var cartItem = await _db.ShoppingCartItems.FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null)
            {
                if (cartItem.Count <= 1)
                {
                    _db.ShoppingCartItems.Remove(cartItem);
                }
                else
                {
                    cartItem.Count -= 1;
                }
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Cart/Remove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int cartId)
        {
            var userId = GetUserId();
            var cartItem = await _db.ShoppingCartItems.FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
            if (cartItem != null)
            {
                _db.ShoppingCartItems.Remove(cartItem);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item removed from shopping cart.";
            }
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
                }
            };

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

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            // Create Order Header
            var orderHeader = vm.OrderHeader;
            orderHeader.OrderDate = DateTime.UtcNow;
            orderHeader.OrderStatus = "Pending";
            
            string methodKey = (paymentType ?? "upi").ToLower();
            string paymentMethodTitle = methodKey switch
            {
                "card" => "Approved (Credit/Debit Card)",
                "cod" => "Pending (Cash on Delivery)",
                _ => "Approved (UPI / Net Banking)"
            };
            orderHeader.PaymentStatus = paymentMethodTitle;
            orderHeader.OrderTotal = vm.GrandTotal;

            await _db.OrderHeaders.AddAsync(orderHeader);
            await _db.SaveChangesAsync();

            // Create Order Details & Deduct Stock
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
                }
            }

            // Clear Cart items from Database
            _db.ShoppingCartItems.RemoveRange(cartItems);

            await _db.SaveChangesAsync();

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

            if (orderHeader == null) return NotFound();

            return View(orderHeader);
        }
    }
}
