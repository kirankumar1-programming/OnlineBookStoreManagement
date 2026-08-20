using Microsoft.Extensions.Options;
using OnlineBookStoreManagement.Models;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace OnlineBookStoreManagement.Services
{
    public class SmtpEmailSenderService : IEmailSenderService
    {
        private readonly SmtpSettings _smtpSettings;
        private readonly ILogger<SmtpEmailSenderService> _logger;

        public SmtpEmailSenderService(IOptions<SmtpSettings> smtpSettings, ILogger<SmtpEmailSenderService> logger)
        {
            _smtpSettings = smtpSettings.Value;
            _logger = logger;
        }

        // Implementation of IEmailSender interface
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            await SendEmailInternalAsync(email, subject, htmlMessage);
        }

        private async Task<bool> SendEmailInternalAsync(string toEmail, string subject, string bodyHtml)
        {
            if (!_smtpSettings.EnableEmailNotifications)
            {
                _logger.LogInformation("Email notifications are disabled in settings. Skipping email to {Email}.", toEmail);
                return false;
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("Target email address is empty. Cannot send email.");
                return false;
            }

            try
            {
                var senderEmail = !string.IsNullOrWhiteSpace(_smtpSettings.SenderEmail)
                    ? _smtpSettings.SenderEmail
                    : "noreply@MyBookStore.com";

                var senderName = !string.IsNullOrWhiteSpace(_smtpSettings.SenderName)
                    ? _smtpSettings.SenderName
                    : " My Book Store";

                using var message = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };

                message.To.Add(new MailAddress(toEmail));

                var host = !string.IsNullOrWhiteSpace(_smtpSettings.Host) ? _smtpSettings.Host : "localhost";
                var port = _smtpSettings.Port > 0 ? _smtpSettings.Port : 587;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = _smtpSettings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = string.IsNullOrWhiteSpace(_smtpSettings.UserName)
                };

                if (!string.IsNullOrWhiteSpace(_smtpSettings.UserName))
                {
                    client.Credentials = new NetworkCredential(_smtpSettings.UserName, _smtpSettings.Password ?? "");
                }

                _logger.LogInformation("Attempting to send SMTP transactional email to {ToEmail} via {Host}:{Port}", toEmail, host, port);
                await client.SendMailAsync(message);
                _logger.LogInformation("Transactional email successfully sent to {ToEmail}. Subject: '{Subject}'", toEmail, subject);
                return true;
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP error occurred while sending email to {ToEmail}. Host: {Host}, Port: {Port}", toEmail, _smtpSettings.Host, _smtpSettings.Port);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error sending email notification to {ToEmail}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendWelcomeEmailAsync(string toEmail, string userName)
        {
            var subject = "Welcome to MyBookStore! Your Account is Ready";
            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); padding: 30px; text-align: center; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 26px; font-weight: 700; color: #6366f1; }}
        .content {{ padding: 30px; line-height: 1.6; }}
        .btn {{ display: inline-block; background-color: #4f46e5; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: 600; margin-top: 20px; }}
        .footer {{ background: #f8fafc; padding: 20px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📚 MyBookStore</h1>
            <p style='margin: 5px 0 0 0; color: #94a3b8; font-size: 14px;'>Your Gateway to Timeless Stories</p>
        </div>
        <div class='content'>
            <h2>Welcome aboard, {WebUtility.HtmlEncode(userName)}! 🎉</h2>
            <p>Thank you for creating an account with <strong>MyBookStore Management</strong>. We are excited to have you join our community of avid readers!</p>
            <p>With your new account, you can:</p>
            <ul>
                <li>Browse hundreds of books across multiple genres</li>
                <li>Add favorite books to your shopping cart and place orders instantly</li>
                <li>Track your order history and live delivery statuses</li>
                <li>Rate and write reviews on books you've read</li>
            </ul>
            <p style='text-align: center;'>
                <a href='#' class='btn'>Explore Catalog Now</a>
            </p>
            <p>If you have any questions or feedback, feel free to reply directly to this email.</p>
            <p>Happy Reading,<br/><strong>The MyBookStore Team</strong></p>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} MyBookStore  Online Management. All rights reserved.<br/>
            This is an automated transactional email sent to {WebUtility.HtmlEncode(toEmail)}.
        </div>
    </div>
</body>
</html>";

            return await SendEmailInternalAsync(toEmail, subject, body);
        }

        public async Task<bool> SendOrderConfirmationEmailAsync(string toEmail, OrderHeader orderHeader, IEnumerable<OrderDetail> orderDetails)
        {
            var subject = $"Order Confirmation - #{orderHeader.Id} [MyBookStore]";
            var itemsHtml = new StringBuilder();

            foreach (var detail in orderDetails)
            {
                var bookTitle = detail.Book?.Title ?? "Book Item";
                var bookAuthor = detail.Book?.Author ?? "";
                var price = detail.Price;
                var itemTotal = price * detail.Count;

                itemsHtml.Append($@"
                <tr>
                    <td style='padding: 12px; border-bottom: 1px solid #e2e8f0;'>
                        <strong>{WebUtility.HtmlEncode(bookTitle)}</strong>
                        {(string.IsNullOrEmpty(bookAuthor) ? "" : $"<br/><small style='color:#64748b;'>by {WebUtility.HtmlEncode(bookAuthor)}</small>")}
                    </td>
                    <td style='padding: 12px; border-bottom: 1px solid #e2e8f0; text-align: center;'>{detail.Count}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e2e8f0; text-align: right;'>₹{price:F2}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e2e8f0; text-align: right; font-weight: 600;'>₹{itemTotal:F2}</td>
                </tr>");
            }

            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 650px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: #0f172a; padding: 25px 30px; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 24px; color: #818cf8; }}
        .status-badge {{ background: #dcfce7; color: #166534; padding: 4px 12px; border-radius: 12px; font-weight: 600; font-size: 13px; display: inline-block; }}
        .content {{ padding: 30px; line-height: 1.6; }}
        .order-info {{ background: #f8fafc; border-radius: 6px; padding: 15px 20px; margin-bottom: 25px; border-left: 4px solid #4f46e5; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 15px; }}
        th {{ background: #f1f5f9; padding: 10px 12px; text-align: left; font-size: 13px; color: #475569; text-transform: uppercase; letter-spacing: 0.5px; }}
        .total-row {{ font-size: 16px; font-weight: 700; color: #1e293b; background: #f8fafc; }}
        .footer {{ background: #f8fafc; padding: 20px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <table style='width: 100%;'>
                <tr>
                    <td><h1 style='margin:0;'>📚 MyBookStore</h1></td>
                    <td style='text-align: right;'><span class='status-badge'>Order Placed</span></td>
                </tr>
            </table>
        </div>
        <div class='content'>
            <h2>Thank you for your order, {WebUtility.HtmlEncode(orderHeader.Name)}!</h2>
            <p>We have received your order and are getting it ready for shipment. Below are your order summary and delivery details.</p>

            <div class='order-info'>
                <p style='margin: 0 0 5px 0;'><strong>Order Reference:</strong> #{orderHeader.Id}</p>
                <p style='margin: 0 0 5px 0;'><strong>Order Date:</strong> {orderHeader.OrderDate:dd MMM yyyy, hh:mm tt} UTC</p>
                <p style='margin: 0 0 5px 0;'><strong>Payment Status:</strong> {WebUtility.HtmlEncode(orderHeader.PaymentStatus ?? "Pending")}</p>
                <p style='margin: 0;'><strong>Shipping Address:</strong> {WebUtility.HtmlEncode(orderHeader.StreetAddress)}, {WebUtility.HtmlEncode(orderHeader.City)} - {WebUtility.HtmlEncode(orderHeader.PostalCode)}</p>
            </div>

            <h3>Ordered Items</h3>
            <table>
                <thead>
                    <tr>
                        <th>Item</th>
                        <th style='text-align: center;'>Qty</th>
                        <th style='text-align: right;'>Price</th>
                        <th style='text-align: right;'>Total</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsHtml}
                </tbody>
                <tfoot>
                    <tr class='total-row'>
                        <td colspan='3' style='padding: 14px 12px; text-align: right;'>Grand Total:</td>
                        <td style='padding: 14px 12px; text-align: right; color: #4f46e5;'>₹{orderHeader.OrderTotal:F2}</td>
                    </tr>
                </tfoot>
            </table>

            <p style='margin-top: 25px;'>You can view your order progress at any time by signing into your account on <strong>MyBookStore</strong>.</p>
            <p>Warm regards,<br/><strong>MyBookStore Customer Care</strong></p>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} MyBookStore Online Management. All rights reserved.<br/>
            Order #{orderHeader.Id} confirmation sent to {WebUtility.HtmlEncode(toEmail)}.
        </div>
    </div>
</body>
</html>";

            return await SendEmailInternalAsync(toEmail, subject, body);
        }

        public async Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, OrderHeader orderHeader, string previousStatus)
        {
            var subject = $"Order #{orderHeader.Id} Status Update: {orderHeader.OrderStatus} [MyBookStore]";
            var badgeColor = orderHeader.OrderStatus?.ToLower() switch
            {
                "shipped" => "#3b82f6",
                "delivered" => "#22c55e",
                "cancelled" => "#ef4444",
                "processing" => "#f59e0b",
                _ => "#64748b"
            };

            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: #0f172a; padding: 25px 30px; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 24px; color: #818cf8; }}
        .content {{ padding: 30px; line-height: 1.6; }}
        .status-box {{ background: #f8fafc; border-radius: 8px; padding: 20px; text-align: center; margin: 20px 0; border: 1px solid #e2e8f0; }}
        .status-pill {{ display: inline-block; background-color: {badgeColor}; color: #ffffff; padding: 6px 16px; border-radius: 20px; font-weight: 700; font-size: 15px; }}
        .footer {{ background: #f8fafc; padding: 20px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1 style='margin:0;'>📚 MyBookStore</h1>
        </div>
        <div class='content'>
            <h2>Order Status Updated! 📦</h2>
            <p>Hello <strong>{WebUtility.HtmlEncode(orderHeader.Name)}</strong>,</p>
            <p>The status of your order <strong>#{orderHeader.Id}</strong> placed on {orderHeader.OrderDate:dd MMM yyyy} has been updated.</p>

            <div class='status-box'>
                <p style='margin: 0 0 10px 0; color: #64748b; font-size: 14px;'>New Order Status</p>
                <span class='status-pill'>{WebUtility.HtmlEncode(orderHeader.OrderStatus ?? "Updated")}</span>
                {(string.IsNullOrEmpty(previousStatus) ? "" : $"<p style='margin: 10px 0 0 0; font-size: 12px; color: #94a3b8;'>Previous status: {WebUtility.HtmlEncode(previousStatus)}</p>")}
            </div>

            <p><strong>Order Summary:</strong></p>
            <ul>
                <li><strong>Order Total:</strong> ₹{orderHeader.OrderTotal:F2}</li>
                <li><strong>Payment:</strong> {WebUtility.HtmlEncode(orderHeader.PaymentStatus ?? "N/A")}</li>
                <li><strong>Delivery Destination:</strong> {WebUtility.HtmlEncode(orderHeader.StreetAddress)}, {WebUtility.HtmlEncode(orderHeader.City)}</li>
            </ul>

            <p>Thank you for choosing <strong>MyBookStore</strong>!</p>
            <p>Best regards,<br/><strong>MyBookStore Support Team</strong></p>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} MyBookStore Online Management. All rights reserved.<br/>
            Status notification for Order #{orderHeader.Id} sent to {WebUtility.HtmlEncode(toEmail)}.
        </div>
    </div>
</body>
</html>";

            return await SendEmailInternalAsync(toEmail, subject, body);
        }

        public async Task<bool> SendTestEmailAsync(string toEmail)
        {
            var subject = "Test Email - MyBookStore SMTP Diagnostic";
            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: #1e1b4b; padding: 25px; color: #ffffff; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 22px; color: #a5b4fc; }}
        .content {{ padding: 25px; line-height: 1.6; }}
        .info-table {{ width: 100%; border-collapse: collapse; margin-top: 15px; }}
        .info-table td {{ padding: 8px 12px; border-bottom: 1px solid #e2e8f0; }}
        .info-table td:first-child {{ font-weight: 600; color: #475569; width: 40%; }}
        .badge-success {{ background: #dcfce7; color: #15803d; padding: 4px 10px; border-radius: 4px; font-weight: 600; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>⚙️ SMTP Diagnostic Test Email</h1>
        </div>
        <div class='content'>
            <h2>SMTP Configuration Verified <span class='badge-success'>Passed</span></h2>
            <p>If you are reading this email, your <strong>MyBookStore SMTP Transactional Email Notification System</strong> is correctly configured and successfully delivering messages!</p>

            <h3>Server Details</h3>
            <table class='info-table'>
                <tr><td>SMTP Host</td><td>{WebUtility.HtmlEncode(_smtpSettings.Host)}</td></tr>
                <tr><td>SMTP Port</td><td>{_smtpSettings.Port}</td></tr>
                <tr><td>SSL Enabled</td><td>{_smtpSettings.EnableSsl}</td></tr>
                <tr><td>Sender Name</td><td>{WebUtility.HtmlEncode(_smtpSettings.SenderName)}</td></tr>
                <tr><td>Sender Email</td><td>{WebUtility.HtmlEncode(_smtpSettings.SenderEmail)}</td></tr>
                <tr><td>Test Timestamp</td><td>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</td></tr>
            </table>

            <p style='margin-top: 20px; font-size: 13px; color: #64748b;'>Sent automatically from MyBookStore Admin Diagnostics Panel.</p>
        </div>
    </div>
</body>
</html>";

            return await SendEmailInternalAsync(toEmail, subject, body);
        }
    }
}
