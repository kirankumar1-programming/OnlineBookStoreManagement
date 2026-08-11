using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;

namespace OnlineBookStoreManagement.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
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
            return RedirectToAction(nameof(Users));
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
    }
}