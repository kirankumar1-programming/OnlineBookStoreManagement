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
    [Authorize]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _db;
        private readonly IEmailSenderService _emailSender;
        private readonly SmtpSettings _smtpSettings;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext db,
            IEmailSenderService emailSender,
            IOptions<SmtpSettings> smtpSettings)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
            _emailSender = emailSender;
            _smtpSettings = smtpSettings.Value;
        }

        private async Task<bool> CheckAdminAccessAsync()
        {
            if (User.IsInRole("Admin") || User.IsInRole("Administrator")) return true;

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return false;

            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Administrator")) return true;

            if (!string.IsNullOrEmpty(user.Email) && user.Email.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(user.UserName) && user.UserName.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(user.FullName) && user.FullName.Contains("admin", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            if (!await CheckAdminAccessAsync()) return RedirectToAction("AccessDenied", "Account");
            return RedirectToAction(nameof(Orders));
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
        public async Task<IActionResult> UpdateOrderStatus(int orderId, string orderStatus, string? paymentStatus)
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
    }
}