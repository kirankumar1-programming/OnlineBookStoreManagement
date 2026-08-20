using Microsoft.AspNetCore.Identity.UI.Services;
using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Services
{
    public interface IEmailSenderService : IEmailSender
    {
        Task<bool> SendWelcomeEmailAsync(string toEmail, string userName);
        Task<bool> SendOrderConfirmationEmailAsync(string toEmail, OrderHeader orderHeader, IEnumerable<OrderDetail> orderDetails);
        Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, OrderHeader orderHeader, string previousStatus);
        Task<bool> SendTestEmailAsync(string toEmail);
    }
}
