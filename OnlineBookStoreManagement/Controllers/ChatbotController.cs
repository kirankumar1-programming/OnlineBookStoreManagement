using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace OnlineBookStoreManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ChatbotController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] ChatRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return Ok(GetDefaultGreetingResponse("Hello! How can I assist you with your book search today?"));
            }

            string input = request.Message.Trim().ToLower();
            string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // 1. GREETING & GENERAL WELCOME
            if (IsMatch(input, "hi", "hello", "hey", "start", "greetings", "help", "menu"))
            {
                return Ok(GetDefaultGreetingResponse("Hi there! 👋 Welcome to MyBookStore! I am your AI reading assistant. How can I help you today?"));
            }

            // 2. ORDER TRACKING & STATUS
            if (IsMatch(input, "order", "track", "status", "delivery", "shipment", "package", "where is my order"))
            {
                return Ok(await HandleOrderTrackingAsync(input, currentUserId));
            }

            // 3. CART SUMMARY
            if (IsMatch(input, "cart", "shopping cart", "basket", "my items", "checkout"))
            {
                return Ok(await HandleCartSummaryAsync(currentUserId));
            }

            // 4. WISHLIST SUMMARY
            if (IsMatch(input, "wishlist", "wish list", "saved books", "saved items", "favorites", "bookmarks"))
            {
                return Ok(await HandleWishlistSummaryAsync(currentUserId));
            }

            // 4. CATEGORIES & GENRES
            var allCategories = await _db.Categories.ToListAsync();
            var specifiedCategory = allCategories.FirstOrDefault(c =>
                input.Contains(c.Name.ToLower()) ||
                c.Name.ToLower().Split(new[] { ' ', '&' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.Length >= 4 && input.Contains(part)));

            if (specifiedCategory != null)
            {
                var categoryBooksResponse = await SearchAndRecommendBooksAsync(input);
                if (categoryBooksResponse != null)
                {
                    return Ok(categoryBooksResponse);
                }
            }

            if (IsMatch(input, "category", "categories", "genre", "genres", "subject", "topics"))
            {
                return Ok(await HandleCategoriesAsync());
            }

            // 5. FAQS - SHIPPING, PAYMENT, RETURNS, CONTACT
            if (IsMatch(input, "shipping", "delivery fee", "dispatch"))
            {
                return Ok(new ChatResponseDto
                {
                    Reply = "🚚 **Shipping Policy Information**:\n\n• Standard delivery takes **2 to 4 business days**.\n• Enjoy **FREE Shipping** on orders over ₹499!\n• Tracking details are provided for every dispatched order.",
                    Options = GetDefaultOptions()
                });
            }

            if (IsMatch(input, "payment", "pay", "upi", "cod", "cash on delivery", "credit card", "debit card"))
            {
                return Ok(new ChatResponseDto
                {
                    Reply = "💳 **Payment Methods Supported**:\n\n• **UPI & Net Banking** (Google Pay, PhonePe, Paytm, BHIM)\n• **Credit & Debit Cards** (Visa, MasterCard, RuPay)\n• **Cash on Delivery (COD)** on eligible orders.",
                    Options = GetDefaultOptions()
                });
            }

            if (IsMatch(input, "return", "refund", "replacement", "exchange", "policy"))
            {
                return Ok(new ChatResponseDto
                {
                    Reply = "📦 **Return & Refund Policy**:\n\n• Easy 7-day return/replacement window for damaged or wrong books.\n• Refunds are processed back to your original payment method within 3 business days of return verification.",
                    Options = GetDefaultOptions()
                });
            }

            if (IsMatch(input, "contact", "support", "helpdesk", "phone", "email"))
            {
                return Ok(new ChatResponseDto
                {
                    Reply = "📞 **Customer Support**:\n\n• **Email**: support@mybookstore.com\n• **Phone**: +91 1800-123-4567 (Mon-Sat, 9 AM - 7 PM)\n• **Address**: Knowledge City, Tech Hub, Suite 400",
                    Options = GetDefaultOptions()
                });
            }

            // 6. BOOK SEARCH / RECOMMENDATIONS / PRICE CONSTRAINTS
            var bookSearchResponse = await SearchAndRecommendBooksAsync(input);
            if (bookSearchResponse != null)
            {
                return Ok(bookSearchResponse);
            }

            // 7. DEFAULT FALLBACK
            return Ok(new ChatResponseDto
            {
                Reply = "I'm not completely sure about that, but I can help you search for books, track your orders, check your cart, or explain shipping and store policies! Try asking something like:\n• *'Recommend best seller books'*\n• *'Fiction books under 500'*\n• *'Track my order'*\n• *'What categories are available?'*",
                Options = GetDefaultOptions()
            });
        }

        private bool IsMatch(string input, params string[] keywords)
        {
            return keywords.Any(kw => input.Contains(kw));
        }

        private ChatResponseDto GetDefaultGreetingResponse(string welcomeMessage)
        {
            return new ChatResponseDto
            {
                Reply = welcomeMessage,
                Options = GetDefaultOptions()
            };
        }

        private List<ChatOptionDto> GetDefaultOptions()
        {
            return new List<ChatOptionDto>
            {
                new ChatOptionDto { Label = "⭐ Best Sellers", Value = "best sellers", Icon = "bi-star-fill" },
                new ChatOptionDto { Label = "📖 Recommend Books", Value = "recommend books", Icon = "bi-book" },
                new ChatOptionDto { Label = "❤️ My Wishlist", Value = "wishlist", Icon = "bi-heart" },
                new ChatOptionDto { Label = "📦 Track My Order", Value = "track order", Icon = "bi-truck" },
                new ChatOptionDto { Label = "📚 Browse Categories", Value = "categories", Icon = "bi-grid" },
                new ChatOptionDto { Label = "🛒 View Cart", Value = "cart", Icon = "bi-cart3" },
                new ChatOptionDto { Label = "💳 Payment & Shipping FAQs", Value = "payment", Icon = "bi-credit-card" }
            };
        }

        private async Task<ChatResponseDto> HandleOrderTrackingAsync(string input, string userId)
        {
            bool isAdmin = User.IsInRole(DbInitializer.Role_Admin) || User.IsInRole("Administrator");

            // Extract numeric order ID if explicitly requested like "order #3" or "track 5"
            var match = Regex.Match(input, @"(?:order\s*#?\s*|track\s*#?\s*)(\d+)");
            int? explicitOrderId = match.Success ? int.Parse(match.Groups[1].Value) : null;

            if (explicitOrderId.HasValue)
            {
                if (string.IsNullOrEmpty(userId) && !isAdmin)
                {
                    return new ChatResponseDto
                    {
                        Reply = "🔐 Please **sign in** to view your order details.",
                        ActionUrl = "/Account/Login?returnUrl=/Cart",
                        ActionText = "Sign In Now",
                        Options = GetDefaultOptions()
                    };
                }

                var order = await _db.OrderHeaders
                    .Include(o => o.OrderDetails)
                    .ThenInclude(d => d.Book)
                    .FirstOrDefaultAsync(o => o.Id == explicitOrderId.Value && (isAdmin || o.UserId == userId));

                if (order == null)
                {
                    return new ChatResponseDto
                    {
                        Reply = $"⚠️ Could not find Order **#{explicitOrderId.Value}** or you do not have permission to view this order.",
                        Options = GetDefaultOptions()
                    };
                }

                string itemsSummary = string.Join(", ", order.OrderDetails.Select(d => $"{d.Book?.Title ?? "Book"} (x{d.Count})"));
                return new ChatResponseDto
                {
                    Reply = $"📦 **Order #{order.Id} Details**:\n\n" +
                            $"• **Date**: {order.OrderDate:dd MMM yyyy, hh:mm tt}\n" +
                            $"• **Status**: {order.OrderStatus}\n" +
                            $"• **Payment**: {order.PaymentStatus}\n" +
                            $"• **Items**: {itemsSummary}\n" +
                            $"• **Total Amount**: ₹{order.OrderTotal:N2}\n" +
                            (string.IsNullOrEmpty(order.TrackingNumber) ? "" : $"• **Tracking No**: `{order.TrackingNumber}` ({order.Carrier})\n"),
                    ActionUrl = $"/Cart/OrderConfirmation/{order.Id}",
                    ActionText = "View Full Order Summary",
                    Options = GetDefaultOptions()
                };
            }

            if (string.IsNullOrEmpty(userId))
            {
                return new ChatResponseDto
                {
                    Reply = "🔐 Please **sign in** to view your active orders and track their delivery status in real-time. Or provide an order number (e.g. *'Track order #3'*).",
                    ActionUrl = "/Account/Login?returnUrl=/Cart",
                    ActionText = "Sign In Now",
                    Options = GetDefaultOptions()
                };
            }

            var recentOrders = await _db.OrderHeaders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .Take(3)
                .ToListAsync();

            if (!recentOrders.Any())
            {
                return new ChatResponseDto
                {
                    Reply = "You haven't placed any orders yet! 📚 Browse our catalog and place your first order to get started.",
                    ActionUrl = "/Home/Index",
                    ActionText = "Browse Catalog",
                    Options = GetDefaultOptions()
                };
            }

            var latest = recentOrders.First();
            string itemsList = string.Join(", ", latest.OrderDetails.Select(d => $"{d.Book?.Title ?? "Book"} (x{d.Count})"));

            string replyText = $"📦 **Your Most Recent Order (#{latest.Id})**:\n\n" +
                               $"• **Status**: `{latest.OrderStatus}`\n" +
                               $"• **Placed On**: {latest.OrderDate:dd MMM yyyy}\n" +
                               $"• **Items**: {itemsList}\n" +
                               $"• **Total**: ₹{latest.OrderTotal:N2}\n";

            if (recentOrders.Count > 1)
            {
                replyText += $"\nYou also have {recentOrders.Count - 1} previous order(s). You can check a specific order by typing e.g. *'Track order #{recentOrders.Last().Id}'*.";
            }

            return new ChatResponseDto
            {
                Reply = replyText,
                ActionUrl = $"/Cart/OrderConfirmation/{latest.Id}",
                ActionText = "View Order Details",
                Options = GetDefaultOptions()
            };
        }

        private async Task<ChatResponseDto> HandleCartSummaryAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return new ChatResponseDto
                {
                    Reply = "🛒 Please **sign in** to view your shopping cart items and place an order.",
                    ActionUrl = "/Account/Login?returnUrl=/Cart",
                    ActionText = "Sign In",
                    Options = GetDefaultOptions()
                };
            }

            var cartItems = await _db.ShoppingCartItems
                .Include(i => i.Book)
                .Where(i => i.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                return new ChatResponseDto
                {
                    Reply = "🛒 Your shopping cart is currently empty! Explore our collection and add your favorite reads.",
                    ActionUrl = "/Home/Index",
                    ActionText = "Explore Books",
                    Options = GetDefaultOptions()
                };
            }

            decimal total = cartItems.Sum(i => (i.Book?.Price ?? 0) * i.Count);
            int totalItems = cartItems.Sum(i => i.Count);

            string itemsDescription = string.Join("\n", cartItems.Select(i => $"• **{i.Book?.Title}** (Qty: {i.Count}) - ₹{(i.Book?.Price ?? 0) * i.Count:N2}"));

            return new ChatResponseDto
            {
                Reply = $"🛒 **Shopping Cart Summary** ({totalItems} item{(totalItems > 1 ? "s" : "")}):\n\n{itemsDescription}\n\n**Grand Total**: ₹{total:N2}",
                ActionUrl = "/Cart",
                ActionText = "Go to Checkout",
                Options = GetDefaultOptions()
            };
        }

        private async Task<ChatResponseDto> HandleWishlistSummaryAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return new ChatResponseDto
                {
                    Reply = "❤️ Please **sign in** to view your wishlist items or save books for later.",
                    ActionUrl = "/Account/Login?returnUrl=/Wishlist",
                    ActionText = "Sign In",
                    Options = GetDefaultOptions()
                };
            }

            var wishlistItems = await _db.WishlistItems
                .Include(w => w.Book)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();

            if (!wishlistItems.Any())
            {
                return new ChatResponseDto
                {
                    Reply = "❤️ Your wishlist is currently empty! Click the heart icon on any book to save it for later.",
                    ActionUrl = "/Home/Index",
                    ActionText = "Explore Books",
                    Options = GetDefaultOptions()
                };
            }

            int inStockCount = wishlistItems.Count(w => w.Book != null && w.Book.StockQuantity > 0);
            string itemsDescription = string.Join("\n", wishlistItems.Select(w => $"• **{w.Book?.Title}** by {w.Book?.Author} - ₹{(w.Book?.Price ?? 0):N2} ({(w.Book?.StockQuantity > 0 ? "In Stock" : "Out of Stock")})"));

            return new ChatResponseDto
            {
                Reply = $"❤️ **My Wishlist** ({wishlistItems.Count} book{(wishlistItems.Count > 1 ? "s" : "")}, {inStockCount} in stock):\n\n{itemsDescription}\n\nYou can move your wishlist items directly to your shopping cart with one click!",
                ActionUrl = "/Wishlist",
                ActionText = "Manage Wishlist",
                Options = GetDefaultOptions()
            };
        }

        private async Task<ChatResponseDto> HandleCategoriesAsync()
        {
            var categories = await _db.Categories
                .Include(c => c.Books)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();

            if (!categories.Any())
            {
                return new ChatResponseDto
                {
                    Reply = "No categories found in store database.",
                    Options = GetDefaultOptions()
                };
            }

            string categoryListText = string.Join("\n", categories.Select(c => $"• **{c.Name}** ({c.Books?.Count ?? 0} books)"));

            var categoryOptions = categories.Select(c => new ChatOptionDto
            {
                Label = $"📚 {c.Name}",
                Value = $"show category {c.Name}",
                Icon = "bi-folder2-open"
            }).ToList();

            return new ChatResponseDto
            {
                Reply = $"📚 **Book Categories Available**:\n\n{categoryListText}\n\nClick any category below to browse books in that genre:",
                Options = categoryOptions
            };
        }

        private async Task<ChatResponseDto?> SearchAndRecommendBooksAsync(string input)
        {
            IQueryable<Book> query = _db.Books.Include(b => b.Category).Include(b => b.Reviews);

            // Extract price limit e.g. "under 500", "below 1000", "< 400"
            decimal? priceLimit = null;
            var priceMatch = Regex.Match(input, @"(?:under|below|less than|<\s*|rs\.?\s*|₹\s*)\s*(\d+)");
            if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, out decimal maxP))
            {
                priceLimit = maxP;
                query = query.Where(b => b.Price <= maxP);
            }

            // Check if specific category mentioned
            var categories = await _db.Categories.ToListAsync();
            var matchedCategory = categories.FirstOrDefault(c =>
                input.Contains(c.Name.ToLower()) ||
                c.Name.ToLower().Split(new[] { ' ', '&' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.Length >= 4 && input.Contains(part)));

            if (matchedCategory != null)
            {
                query = query.Where(b => b.CategoryId == matchedCategory.Id);
            }

            // Check for best seller / top rated intent
            bool isTopRated = input.Contains("best seller") || input.Contains("top rated") || input.Contains("popular") || input.Contains("best");
            if (isTopRated)
            {
                query = query.OrderByDescending(b => b.Reviews.Any() ? b.Reviews.Average(r => r.Rating) : 0).ThenByDescending(b => b.StockQuantity);
            }
            else
            {
                // General text search against title, author, description, category
                string cleanInput = Regex.Replace(input, @"(recommend|suggest|show|me|books?|find|search|for|under|below|top|rated|best|seller|category|categories|in)", "").Trim();

                if (matchedCategory != null)
                {
                    // If category was matched, remove category name tokens so cleanInput doesn't filter out books in that category by title
                    foreach (var part in matchedCategory.Name.ToLower().Split(new[] { ' ', '&' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        cleanInput = Regex.Replace(cleanInput, @"\b" + Regex.Escape(part) + @"\b", "").Trim();
                    }
                }

                if (priceLimit.HasValue)
                {
                    // Strip out digits used for price limit so "50" from "under 50" isn't searched as title text
                    cleanInput = Regex.Replace(cleanInput, @"\b" + (int)priceLimit.Value + @"\b", "").Trim();
                }

                if (!string.IsNullOrEmpty(cleanInput) && cleanInput.Length >= 3 && !cleanInput.All(char.IsDigit))
                {
                    query = query.Where(b =>
                        b.Title.ToLower().Contains(cleanInput) ||
                        b.Author.ToLower().Contains(cleanInput) ||
                        b.Description.ToLower().Contains(cleanInput) ||
                        (b.Category != null && b.Category.Name.ToLower().Contains(cleanInput)));
                }
            }

            var books = await query.Take(4).ToListAsync();

            if (!books.Any())
            {
                // If price filter was applied and yielded 0 books, return helpful message
                if (priceLimit.HasValue)
                {
                    return new ChatResponseDto
                    {
                        Reply = $"Sorry, I couldn't find any books priced under **₹{priceLimit.Value:N0}**. Try increasing your price range or browsing all available categories!",
                        Options = GetDefaultOptions()
                    };
                }

                return null;
            }

            var dtos = books.Select(b => new ChatBookDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                PriceFormatted = $"₹{b.Price:N2}",
                CoverImageUrl = string.IsNullOrEmpty(b.CoverImageUrl) || b.CoverImageUrl.EndsWith("default-book.png") ? "/images/default-book.svg" : b.CoverImageUrl,
                CategoryName = b.Category?.Name ?? "General",
                AverageRating = b.AverageRating,
                StockQuantity = b.StockQuantity
            }).ToList();

            string headerText = matchedCategory != null
                ? $"📚 Here are top recommendations in **{matchedCategory.Name}**"
                : isTopRated
                    ? "⭐ Here are our **Top Rated & Best Seller Books**"
                    : priceLimit.HasValue
                        ? $"🏷️ Here are books under **₹{priceLimit.Value:N0}**"
                        : "📖 Here are books matching your request";

            return new ChatResponseDto
            {
                Reply = $"{headerText}:",
                Books = dtos,
                Options = GetDefaultOptions()
            };
        }
    }
}
