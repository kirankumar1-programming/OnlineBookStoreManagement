using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using OnlineBookStoreManagement.Services;

namespace OnlineBookStoreManagement.Controllers
{
    [Authorize(Roles = DbInitializer.Role_Admin)]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _db;
        private readonly IEmailSenderService _emailSender;
        private readonly SmtpSettings _smtpSettings;
        private readonly ILowStockDigestService _digestService;
        private readonly IPdfInvoiceGeneratorService _pdfGenerator;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext db,
            IEmailSenderService emailSender,
            IOptions<SmtpSettings> smtpSettings,
            ILowStockDigestService digestService,
            IPdfInvoiceGeneratorService pdfGenerator)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
            _emailSender = emailSender;
            _smtpSettings = smtpSettings.Value;
            _digestService = digestService;
            _pdfGenerator = pdfGenerator;
        }

        private async Task<bool> CheckAdminAccessAsync()
        {
            if (User.IsInRole(DbInitializer.Role_Admin) || User.IsInRole("Administrator")) return true;

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return false;

            return await _userManager.IsInRoleAsync(user, DbInitializer.Role_Admin) || await _userManager.IsInRoleAsync(user, "Administrator");
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            return RedirectToAction(nameof(Analytics));
        }

        // GET: /Admin/Analytics
        public async Task<IActionResult> Analytics()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var viewModel = await BuildAnalyticsDashboardViewModelAsync();
            return View(viewModel);
        }

        // GET: /Admin/GetAnalyticsApiData
        [HttpGet]
        public async Task<IActionResult> GetAnalyticsApiData()
        {
            if (!await CheckAdminAccessAsync()) return Unauthorized();

            var viewModel = await BuildAnalyticsDashboardViewModelAsync();
            return Json(viewModel);
        }

        private async Task<AnalyticsDashboardViewModel> BuildAnalyticsDashboardViewModelAsync()
        {
            var vm = new AnalyticsDashboardViewModel();

            // 1. Overall Summary Stats
            vm.TotalRevenue = await _db.OrderHeaders.SumAsync(o => (decimal?)o.OrderTotal) ?? 0m;
            vm.TotalOrders = await _db.OrderHeaders.CountAsync();
            vm.TotalBooksSold = await _db.OrderDetails.SumAsync(d => (int?)d.Count) ?? 0;

            // 2. Chart 1: Monthly Revenue (Last 6 Months up to current date)
            var orders = await _db.OrderHeaders
                .OrderBy(o => o.OrderDate)
                .ToListAsync();

            var monthlyGrouped = orders
                .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                .Select(g => new
                {
                    Date = new DateTime(g.Key.Year, g.Key.Month, 1),
                    MonthLabel = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    Revenue = g.Sum(o => o.OrderTotal),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            if (!monthlyGrouped.Any())
            {
                var currentDate = DateTime.UtcNow;
                for (int i = 5; i >= 0; i--)
                {
                    var dt = currentDate.AddMonths(-i);
                    vm.MonthlyRevenue.Labels.Add(dt.ToString("MMM yyyy"));
                    vm.MonthlyRevenue.Revenue.Add(0m);
                    vm.MonthlyRevenue.OrderCounts.Add(0);
                }
            }
            else
            {
                foreach (var item in monthlyGrouped)
                {
                    vm.MonthlyRevenue.Labels.Add(item.MonthLabel);
                    vm.MonthlyRevenue.Revenue.Add(item.Revenue);
                    vm.MonthlyRevenue.OrderCounts.Add(item.Count);
                }
            }

            // 3. Chart 2: Top 5 Best Selling Books
            var orderDetails = await _db.OrderDetails
                .Include(d => d.Book)
                .ThenInclude(b => b!.Category)
                .ToListAsync();

            var topBooks = orderDetails
                .Where(d => d.Book != null)
                .GroupBy(d => d.Book!.Title)
                .Select(g => new
                {
                    Title = g.Key,
                    QuantitySold = g.Sum(d => d.Count),
                    TotalRevenue = g.Sum(d => d.Count * d.Price)
                })
                .OrderByDescending(b => b.QuantitySold)
                .ThenByDescending(b => b.TotalRevenue)
                .Take(5)
                .ToList();

            foreach (var book in topBooks)
            {
                vm.TopSellingBooks.Labels.Add(book.Title);
                vm.TopSellingBooks.QuantitiesSold.Add(book.QuantitySold);
                vm.TopSellingBooks.TotalRevenues.Add(book.TotalRevenue);
            }

            // 4. Chart 3: Category Revenue Breakdown
            var categoryGrouped = orderDetails
                .Where(d => d.Book != null && d.Book.Category != null)
                .GroupBy(d => d.Book!.Category!.Name)
                .Select(g => new
                {
                    CategoryName = g.Key,
                    Revenue = g.Sum(d => d.Count * d.Price)
                })
                .OrderByDescending(c => c.Revenue)
                .ToList();

            decimal grandCategoryRevenue = categoryGrouped.Sum(c => c.Revenue);

            foreach (var cat in categoryGrouped)
            {
                vm.CategoryRevenue.Labels.Add(cat.CategoryName);
                vm.CategoryRevenue.Revenues.Add(cat.Revenue);

                double pct = grandCategoryRevenue > 0
                    ? Math.Round((double)(cat.Revenue / grandCategoryRevenue * 100), 1)
                    : 0;
                vm.CategoryRevenue.Percentages.Add(pct);
            }

            if (categoryGrouped.Any())
            {
                vm.TopCategoryName = categoryGrouped.First().CategoryName;
            }

            return vm;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Users()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var users = await _userManager.Users.ToListAsync();
            var userRolesList = new List<UserRoleViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userRolesList.Add(new UserRoleViewModel
                {
                    UserId = user.Id,
                    Email = user.Email ?? "",
                    FullName = user.FullName,
                    Roles = roles
                });
            }

            return View(userRolesList);
        }

        // POST: /Admin/ToggleAdminRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAdminRole(string userId)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.RemoveFromRoleAsync(user, "Admin");
            }
            else
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }

            return RedirectToAction(nameof(Users));
        }

        // GET: /Admin/Orders
        public async Task<IActionResult> Orders(string? statusFilter = null)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var query = _db.OrderHeaders.Include(o => o.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
            {
                query = query.Where(o => o.OrderStatus == statusFilter);
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();
            ViewData["StatusFilter"] = statusFilter ?? "All";

            return View(orders);
        }

        // GET: /Admin/OrderDetails/5
        public async Task<IActionResult> OrderDetails(int id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var order = await _db.OrderHeaders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // POST: /Admin/UpdateOrderStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, string orderStatus, string? paymentStatus, string? carrier, string? trackingNumber, DateTime? shippingDate)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var order = await _db.OrderHeaders
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound();

            var previousStatus = order.OrderStatus ?? "Pending";
            order.OrderStatus = orderStatus;

            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                order.PaymentStatus = paymentStatus;
            }

            if (!string.IsNullOrWhiteSpace(carrier))
            {
                order.Carrier = carrier;
            }

            if (!string.IsNullOrWhiteSpace(trackingNumber))
            {
                order.TrackingNumber = trackingNumber;
            }

            if (shippingDate.HasValue)
            {
                order.ShippingDate = shippingDate.Value;
            }
            else if (orderStatus == "Shipped" && order.ShippingDate == default)
            {
                order.ShippingDate = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            // Trigger Transactional Order Status Update Email Notification
            var customerEmail = order.User?.Email;
            if (string.IsNullOrEmpty(customerEmail) && !string.IsNullOrEmpty(order.UserId))
            {
                var customer = await _userManager.FindByIdAsync(order.UserId);
                customerEmail = customer?.Email;
            }

            if (!string.IsNullOrEmpty(customerEmail))
            {
                _ = Task.Run(() => _emailSender.SendOrderStatusUpdateEmailAsync(customerEmail, order, previousStatus));
                TempData["SuccessMessage"] = $"Order #{order.Id} status updated to '{orderStatus}' and customer notification email triggered!";
            }
            else
            {
                TempData["SuccessMessage"] = $"Order #{order.Id} status updated to '{orderStatus}'.";
            }

            return RedirectToAction(nameof(OrderDetails), new { id = orderId });
        }

        // GET: /Admin/DownloadInvoice/5
        public async Task<IActionResult> DownloadInvoice(int id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var order = await _db.OrderHeaders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            byte[] pdfBytes = _pdfGenerator.GenerateInvoicePdf(order);
            string fileName = $"Invoice_Order_{order.Id}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        // GET: /Admin/SmtpSettings
        public async Task<IActionResult> SmtpSettings()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            return View(_smtpSettings);
        }

        // POST: /Admin/SendTestEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTestEmail(string testEmail)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            if (string.IsNullOrWhiteSpace(testEmail))
            {
                TempData["ErrorMessage"] = "Please enter a valid target email address to send the test message.";
                return RedirectToAction(nameof(SmtpSettings));
            }

            bool sent = await _emailSender.SendTestEmailAsync(testEmail);
            if (sent)
            {
                TempData["SuccessMessage"] = $"Diagnostic test email sent successfully to {testEmail}!";
            }
            else
            {
                TempData["ErrorMessage"] = $"Failed to send test email to {testEmail}. Please verify SMTP credentials and server connectivity in appsettings.json or review server logs.";
            }

            return RedirectToAction(nameof(SmtpSettings));
        }

        // GET: /Admin/LowStockDigest
        public async Task<IActionResult> LowStockDigest(int? threshold)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var report = await _digestService.GetLowStockReportAsync(threshold);
            return View(report);
        }

        // POST: /Admin/SendLowStockDigest
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendLowStockDigest(int? threshold)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var result = await _digestService.SendLowStockDigestAsync(threshold);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(LowStockDigest), new { threshold });
        }

        // ==================== COUPON MANAGEMENT ACTIONS ====================

        // GET: /Admin/Coupons
        public async Task<IActionResult> Coupons()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var coupons = await _db.Coupons
                .OrderByDescending(c => c.Id)
                .ToListAsync();

            return View(coupons);
        }

        // GET: /Admin/CreateCoupon
        public async Task<IActionResult> CreateCoupon()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            return View(new Coupon { IsActive = true, MinimumOrderAmount = 0m });
        }

        // POST: /Admin/CreateCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCoupon(Coupon coupon)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            coupon.Code = coupon.Code?.Trim().ToUpper() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(coupon.Code))
            {
                ModelState.AddModelError("Code", "Coupon Code is required.");
            }
            else if (await _db.Coupons.AnyAsync(c => c.Code.ToUpper() == coupon.Code))
            {
                ModelState.AddModelError("Code", $"A coupon with code '{coupon.Code}' already exists.");
            }

            if (coupon.DiscountValue <= 0)
            {
                ModelState.AddModelError("DiscountValue", "Discount Value must be greater than zero.");
            }

            if (coupon.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase) && coupon.DiscountValue > 100)
            {
                ModelState.AddModelError("DiscountValue", "Percentage discount cannot exceed 100%.");
            }

            if (coupon.MinimumOrderAmount < 0)
            {
                ModelState.AddModelError("MinimumOrderAmount", "Minimum Order Amount cannot be negative.");
            }

            if (coupon.StartDate.HasValue && coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < coupon.StartDate.Value)
            {
                ModelState.AddModelError("ExpiryDate", "Expiry date must be on or after start date.");
            }

            if (coupon.UsageLimit.HasValue && coupon.UsageLimit.Value < 1)
            {
                ModelState.AddModelError("UsageLimit", "Usage limit must be at least 1.");
            }

            if (ModelState.IsValid)
            {
                _db.Coupons.Add(coupon);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Promotional coupon '{coupon.Code}' created successfully!";
                return RedirectToAction(nameof(Coupons));
            }

            return View(coupon);
        }

        // GET: /Admin/EditCoupon/5
        public async Task<IActionResult> EditCoupon(int? id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var coupon = await _db.Coupons.FindAsync(id);
            if (coupon == null) return NotFound();

            return View(coupon);
        }

        // POST: /Admin/EditCoupon/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCoupon(int id, Coupon coupon)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id != coupon.Id) return NotFound();

            coupon.Code = coupon.Code?.Trim().ToUpper() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(coupon.Code))
            {
                ModelState.AddModelError("Code", "Coupon Code is required.");
            }
            else if (await _db.Coupons.AnyAsync(c => c.Code.ToUpper() == coupon.Code && c.Id != coupon.Id))
            {
                ModelState.AddModelError("Code", $"Another coupon with code '{coupon.Code}' already exists.");
            }

            if (coupon.DiscountValue <= 0)
            {
                ModelState.AddModelError("DiscountValue", "Discount Value must be greater than zero.");
            }

            if (coupon.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase) && coupon.DiscountValue > 100)
            {
                ModelState.AddModelError("DiscountValue", "Percentage discount cannot exceed 100%.");
            }

            if (coupon.MinimumOrderAmount < 0)
            {
                ModelState.AddModelError("MinimumOrderAmount", "Minimum Order Amount cannot be negative.");
            }

            if (coupon.StartDate.HasValue && coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < coupon.StartDate.Value)
            {
                ModelState.AddModelError("ExpiryDate", "Expiry date must be on or after start date.");
            }

            if (coupon.UsageLimit.HasValue && coupon.UsageLimit.Value < 1)
            {
                ModelState.AddModelError("UsageLimit", "Usage limit must be at least 1.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _db.Update(coupon);
                    await _db.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Promotional coupon '{coupon.Code}' updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _db.Coupons.AnyAsync(c => c.Id == coupon.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Coupons));
            }

            return View(coupon);
        }

        // GET: /Admin/DeleteCoupon/5
        public async Task<IActionResult> DeleteCoupon(int? id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var coupon = await _db.Coupons.FindAsync(id);
            if (coupon == null) return NotFound();

            return View(coupon);
        }

        // POST: /Admin/DeleteCoupon/5
        [HttpPost, ActionName("DeleteCoupon")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCouponConfirmed(int id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var coupon = await _db.Coupons.FindAsync(id);
            if (coupon != null)
            {
                _db.Coupons.Remove(coupon);
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' deleted successfully!";
            }

            return RedirectToAction(nameof(Coupons));
        }

        // POST: /Admin/ToggleCouponStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCouponStatus(int id)
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");

            var coupon = await _db.Coupons.FindAsync(id);
            if (coupon != null)
            {
                coupon.IsActive = !coupon.IsActive;
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' status set to {(coupon.IsActive ? "Active" : "Inactive")}.";
            }

            return RedirectToAction(nameof(Coupons));
        }
    }
}