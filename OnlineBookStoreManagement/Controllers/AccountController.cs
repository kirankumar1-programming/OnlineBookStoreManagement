
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
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _db;
        private readonly IEmailSenderService _emailSender;
        private readonly IPdfInvoiceGeneratorService _pdfGenerator;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext db,
            IEmailSenderService emailSender,
            IPdfInvoiceGeneratorService pdfGenerator)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _db = db;
            _emailSender = emailSender;
            _pdfGenerator = pdfGenerator;
        }

        // GET: /Account/Register
        public IActionResult Register(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            returnUrl ??= Url.Content("~/");

            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    Address = model.Address,
                    City = model.City,
                    PostalCode = model.PostalCode,
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, DbInitializer.Role_Customer);
                    await _signInManager.SignInAsync(user, isPersistent: false);

                    // Send Welcome Email Notification via SMTP
                    if (!string.IsNullOrEmpty(user.Email))
                    {
                        _ = Task.Run(() => _emailSender.SendWelcomeEmailAsync(user.Email, user.FullName ?? user.Email));
                    }

                    TempData["SuccessMessage"] = $"Welcome to My Book Store, {user.FullName}!";
                    return LocalRedirect(returnUrl);
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // GET: /Account/Login
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            returnUrl ??= Url.Content("~/");

            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    var user = await _userManager.FindByEmailAsync(model.Email);
                    TempData["SuccessMessage"] = $"Welcome back, {user?.FullName ?? "User"}!";

                    bool isAdmin = false;
                    if (user != null)
                    {
                        isAdmin = await _userManager.IsInRoleAsync(user, DbInitializer.Role_Admin);
                    }

                    if (isAdmin)
                    {
                        return RedirectToAction("Users", "Admin");
                    }

                    return LocalRedirect(returnUrl);
                }

                ModelState.AddModelError(string.Empty, "Invalid login credentials.");
            }

            return View(model);
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            TempData["SuccessMessage"] = "You have been logged out safely.";
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/AccessDenied
        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: /Account/Orders
        [Authorize]
        public async Task<IActionResult> Orders()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var orders = await _db.OrderHeaders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        // GET: /Account/OrderDetails/5
        [Authorize]
        public async Task<IActionResult> OrderDetails(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var order = await _db.OrderHeaders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null) return NotFound();

            return View(order);
        }

        // GET: /Account/DownloadInvoice/5
        [Authorize]
        public async Task<IActionResult> DownloadInvoice(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Administrator");

            var order = await _db.OrderHeaders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == id && (isAdmin || o.UserId == userId));

            if (order == null) return NotFound();

            byte[] pdfBytes = _pdfGenerator.GenerateInvoicePdf(order);
            string fileName = $"Invoice_Order_{order.Id}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
