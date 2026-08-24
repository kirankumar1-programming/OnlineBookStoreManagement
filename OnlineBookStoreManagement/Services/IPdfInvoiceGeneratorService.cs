using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Services
{
    public interface IPdfInvoiceGeneratorService
    {
        /// <summary>
        /// Generates a formatted PDF document byte array for the specified order.
        /// </summary>
        byte[] GenerateInvoicePdf(OrderHeader order);
    }
}
