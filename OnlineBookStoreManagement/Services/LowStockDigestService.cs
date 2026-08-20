using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using System.Net;
using System.Text;

namespace OnlineBookStoreManagement.Services
{
    public class LowStockDigestService : ILowStockDigestService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSenderService _emailSender;
        private readonly LowStockSettings _lowStockSettings;
        private readonly ILogger<LowStockDigestService> _logger;

        public LowStockDigestService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSenderService emailSender,
            IOptions<LowStockSettings> lowStockSettings,
            ILogger<LowStockDigestService> logger)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
            _lowStockSettings = lowStockSettings.Value;
            _logger = logger;
        }

        public async Task<LowStockReportViewModel> GetLowStockReportAsync(int? customThreshold = null)
        {
            int threshold = customThreshold ?? (_lowStockSettings.Threshold > 0 ? _lowStockSettings.Threshold : 5);

            var totalBooks = await _db.Books.CountAsync();

            var alertBooks = await _db.Books
                .Include(b => b.Category)
                .Where(b => b.StockQuantity <= threshold)
                .OrderBy(b => b.StockQuantity)
                .ThenBy(b => b.Title)
                .ToListAsync();

            var outOfStock = alertBooks.Where(b => b.StockQuantity <= 0).ToList();
            var lowStock = alertBooks.Where(b => b.StockQuantity > 0).ToList();

            var adminRecipients = await GetAdminEmailRecipientsAsync();

            return new LowStockReportViewModel
            {
                Threshold = threshold,
                TotalBooks = totalBooks,
                OutOfStockBooks = outOfStock,
                LowStockBooks = lowStock,
                AdminRecipients = adminRecipients,
                GeneratedAt = DateTime.UtcNow
            };
        }

        private async Task<List<string>> GetAdminEmailRecipientsAsync()
        {
            var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Check if explicit recipient override is specified
            if (!string.IsNullOrWhiteSpace(_lowStockSettings.RecipientEmailOverride))
            {
                var customEmails = _lowStockSettings.RecipientEmailOverride
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var email in customEmails)
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        recipients.Add(email);
                    }
                }
            }

            // 2. Fetch users in Admin roles
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var user in adminUsers)
            {
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    recipients.Add(user.Email);
                }
            }

            var administratorUsers = await _userManager.GetUsersInRoleAsync("Administrator");
            foreach (var user in administratorUsers)
            {
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    recipients.Add(user.Email);
                }
            }

            // 3. Fallback to all users if no admins found via roles or override (or check email containing admin)
            if (!recipients.Any())
            {
                var allUsers = await _userManager.Users.ToListAsync();
                foreach (var user in allUsers)
                {
                    if (!string.IsNullOrWhiteSpace(user.Email) &&
                        (user.Email.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                         (user.UserName != null && user.UserName.Contains("admin", StringComparison.OrdinalIgnoreCase))))
                    {
                        recipients.Add(user.Email);
                    }
                }
            }

            return recipients.ToList();
        }

        public async Task<LowStockDigestResult> SendLowStockDigestAsync(int? customThreshold = null, CancellationToken cancellationToken = default)
        {
            var report = await GetLowStockReportAsync(customThreshold);
            var result = new LowStockDigestResult
            {
                OutOfStockCount = report.OutOfStockCount,
                LowStockCount = report.LowStockCount,
                ExecutionTime = DateTime.UtcNow
            };

            if (!report.AdminRecipients.Any())
            {
                _logger.LogWarning("No admin email recipients found to receive the low-stock digest.");
                result.Success = false;
                result.Message = "No admin email recipients found.";
                return result;
            }

            var subject = $"[MyBookStore] Daily Inventory Alert Digest - {report.TotalAlertCount} Title(s) Low/Out of Stock ({DateTime.UtcNow:dd MMM yyyy})";
            var bodyHtml = BuildDigestEmailHtml(report);

            var sentList = new List<string>();
            int successCount = 0;

            foreach (var recipient in report.AdminRecipients)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await _emailSender.SendEmailAsync(recipient, subject, bodyHtml);
                    sentList.Add(recipient);
                    successCount++;
                    _logger.LogInformation("Low-stock daily digest email successfully dispatched to admin {Email}", recipient);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send low-stock digest email to admin {Email}", recipient);
                }
            }

            result.SentToEmails = sentList;
            result.Success = successCount > 0;
            result.Message = result.Success
                ? $"Low-stock daily digest successfully sent to {successCount} admin recipient(s)."
                : "Failed to dispatch daily digest email to any admin recipient.";

            return result;
        }

        private string BuildDigestEmailHtml(LowStockReportViewModel report)
        {
            var outOfStockRows = new StringBuilder();
            if (report.OutOfStockBooks.Any())
            {
                foreach (var book in report.OutOfStockBooks)
                {
                    outOfStockRows.Append($@"
                    <tr>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2;'>
                            <span style='background-color: #ef4444; color: #ffffff; padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px;'>OUT OF STOCK</span>
                        </td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2;'>
                            <strong style='color: #991b1b;'>{WebUtility.HtmlEncode(book.Title)}</strong><br/>
                            <small style='color: #64748b;'>by {WebUtility.HtmlEncode(book.Author)}</small>
                        </td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2; color: #475569;'>{WebUtility.HtmlEncode(book.Category?.Name ?? "General")}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2; font-family: monospace;'>{WebUtility.HtmlEncode(book.ISBN)}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2; text-align: right;'>₹{book.Price:F2}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fee2e2; text-align: center; font-weight: 700; color: #dc2626;'>0</td>
                    </tr>");
                }
            }

            var lowStockRows = new StringBuilder();
            if (report.LowStockBooks.Any())
            {
                foreach (var book in report.LowStockBooks)
                {
                    lowStockRows.Append($@"
                    <tr>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7;'>
                            <span style='background-color: #f59e0b; color: #ffffff; padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px;'>LOW STOCK</span>
                        </td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7;'>
                            <strong style='color: #92400e;'>{WebUtility.HtmlEncode(book.Title)}</strong><br/>
                            <small style='color: #64748b;'>by {WebUtility.HtmlEncode(book.Author)}</small>
                        </td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7; color: #475569;'>{WebUtility.HtmlEncode(book.Category?.Name ?? "General")}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7; font-family: monospace;'>{WebUtility.HtmlEncode(book.ISBN)}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7; text-align: right;'>₹{book.Price:F2}</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #fef3c7; text-align: center; font-weight: 700; color: #d97706;'>{book.StockQuantity}</td>
                    </tr>");
                }
            }

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 700px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: #0f172a; padding: 25px 30px; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 24px; color: #818cf8; }}
        .header p {{ margin: 5px 0 0 0; color: #94a3b8; font-size: 14px; }}
        .content {{ padding: 30px; line-height: 1.6; }}
        .stats-grid {{ display: table; width: 100%; margin: 20px 0; border-spacing: 10px; border-collapse: separate; }}
        .stat-card {{ display: table-cell; width: 33%; padding: 15px; border-radius: 8px; text-align: center; vertical-align: middle; }}
        .stat-card.alert {{ background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; }}
        .stat-card.danger {{ background: #fff1f2; border: 1px solid #ffe4e6; color: #be123c; }}
        .stat-card.warning {{ background: #fffbeb; border: 1px solid #fef3c7; color: #b45309; }}
        .stat-card.success {{ background: #f0fdf4; border: 1px solid #bbf7d0; color: #15803d; }}
        .stat-number {{ font-size: 28px; font-weight: 800; display: block; line-height: 1.2; }}
        .stat-label {{ font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 14px; }}
        th {{ background: #f1f5f9; padding: 10px 12px; text-align: left; font-size: 12px; color: #475569; text-transform: uppercase; letter-spacing: 0.5px; }}
        .section-title {{ font-size: 16px; font-weight: 700; margin: 25px 0 10px 0; border-bottom: 2px solid #e2e8f0; padding-bottom: 5px; }}
        .footer {{ background: #f8fafc; padding: 20px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
        .healthy-box {{ background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; padding: 25px; text-align: center; color: #166534; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📚 MyBookStore Admin Inventory Digest</h1>
            <p>Daily Stock Status Report &bull; Threshold: &le; {report.Threshold} copies &bull; Generated {report.GeneratedAt:dd MMM yyyy, hh:mm tt} UTC</p>
        </div>
        <div class='content'>
            <p>Hello Store Administrator,</p>
            <p>Here is your daily automated inventory digest report detailing books that are currently <strong>out of stock</strong> or <strong>nearing replenishment thresholds</strong>.</p>

            <div class='stats-grid'>
                <div class='stat-card alert'>
                    <span class='stat-number'>{report.TotalAlertCount}</span>
                    <span class='stat-label'>Total Alert Titles</span>
                </div>
                <div class='stat-card danger'>
                    <span class='stat-number'>{report.OutOfStockCount}</span>
                    <span class='stat-label'>Out of Stock</span>
                </div>
                <div class='stat-card warning'>
                    <span class='stat-number'>{report.LowStockCount}</span>
                    <span class='stat-label'>Low Stock (&le; {report.Threshold})</span>
                </div>
            </div>

            {(report.TotalAlertCount == 0 ? $@"
            <div class='healthy-box'>
                <h3 style='margin: 0 0 10px 0; color: #15803d;'>✅ All Inventory Levels Healthy!</h3>
                <p style='margin: 0;'>All {report.TotalBooks} books in the catalog currently exceed the low-stock threshold of {report.Threshold} copies. No immediate replenishment is required.</p>
            </div>" : "")}

            {(report.OutOfStockBooks.Any() ? $@"
            <div class='section-title' style='color: #dc2626;'>🚫 Out of Stock Titles ({report.OutOfStockCount})</div>
            <table>
                <thead>
                    <tr>
                        <th style='width: 15%;'>Status</th>
                        <th style='width: 35%;'>Book Title</th>
                        <th style='width: 20%;'>Category</th>
                        <th style='width: 18%;'>ISBN</th>
                        <th style='width: 12%; text-align: right;'>Price</th>
                        <th style='width: 10%; text-align: center;'>Qty</th>
                    </tr>
                </thead>
                <tbody>
                    {outOfStockRows}
                </tbody>
            </table>" : "")}

            {(report.LowStockBooks.Any() ? $@"
            <div class='section-title' style='color: #d97706;'>⚠️ Low Stock Titles ({report.LowStockCount})</div>
            <table>
                <thead>
                    <tr>
                        <th style='width: 15%;'>Status</th>
                        <th style='width: 35%;'>Book Title</th>
                        <th style='width: 20%;'>Category</th>
                        <th style='width: 18%;'>ISBN</th>
                        <th style='width: 12%; text-align: right;'>Price</th>
                        <th style='width: 10%; text-align: center;'>Qty</th>
                    </tr>
                </thead>
                <tbody>
                    {lowStockRows}
                </tbody>
            </table>" : "")}

            <p style='margin-top: 30px; font-size: 13px; color: #64748b;'>
                <strong>Action Required:</strong> Log in to the <strong>MyBookStore Admin Portal</strong> to edit stock levels, order copies, or update catalog details.
            </p>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} MyBookStore Management System. Automated Store Admin Notification Service.<br/>
            This digest was dispatched to {report.AdminRecipients.Count} registered admin recipient(s): {string.Join(", ", report.AdminRecipients)}.
        </div>
    </div>
</body>
</html>";

            return html;
        }
    }
}
