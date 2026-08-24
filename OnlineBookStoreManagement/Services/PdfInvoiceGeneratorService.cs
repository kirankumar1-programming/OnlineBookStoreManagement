using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OnlineBookStoreManagement.Models;

namespace OnlineBookStoreManagement.Services
{
    public class PdfInvoiceGeneratorService : IPdfInvoiceGeneratorService
    {
        public byte[] GenerateInvoicePdf(OrderHeader order)
        {
            var writer = new PdfDocumentWriter();
            return writer.BuildInvoicePdf(order);
        }

        private class PdfDocumentWriter
        {
            private readonly MemoryStream _ms = new MemoryStream();
            private readonly List<long> _objectOffsets = new List<long>();
            private readonly StringBuilder _contentBuilder = new StringBuilder();

            public byte[] BuildInvoicePdf(OrderHeader order)
            {
                GenerateContentStream(order);

                byte[] streamContentBytes = Encoding.ASCII.GetBytes(_contentBuilder.ToString());

                // 0. Header (Must be at byte offset 0)
                WriteString("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                // 1 0 obj: Catalog
                WriteObject("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                // 2 0 obj: Pages
                WriteObject("2 0 obj\n<< /Type /Pages /Count 1 /Kids [ 3 0 R ] >>\nendobj\n");

                // 3 0 obj: Page
                WriteObject("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [ 0 0 612 792 ] /Resources << /Font << /F1 4 0 R /F2 5 0 R /F3 6 0 R >> >> /Contents 7 0 R >>\nendobj\n");

                // 4 0 obj: Font Helvetica
                WriteObject("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

                // 5 0 obj: Font Helvetica-Bold
                WriteObject("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

                // 6 0 obj: Font Helvetica-Oblique
                WriteObject("6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>\nendobj\n");

                // 7 0 obj: Contents Stream
                long streamObjPos = _ms.Position;
                _objectOffsets.Add(streamObjPos);

                string streamHeader = $"7 0 obj\n<< /Length {streamContentBytes.Length} >>\nstream\n";
                WriteString(streamHeader);
                _ms.Write(streamContentBytes, 0, streamContentBytes.Length);
                WriteString("\nendstream\nendobj\n");

                // Cross Reference Table
                long startXrefPos = _ms.Position;
                int totalObjects = _objectOffsets.Count + 1; // +1 for 0 0 obj

                StringBuilder xrefSb = new StringBuilder();
                xrefSb.Append("xref\n");
                xrefSb.Append($"0 {totalObjects}\n");
                xrefSb.Append("0000000000 65535 f \r\n");

                foreach (long offset in _objectOffsets)
                {
                    xrefSb.Append($"{offset:D10} 00000 n \r\n");
                }

                xrefSb.Append("trailer\n");
                xrefSb.Append($"<< /Size {totalObjects} /Root 1 0 R >>\n");
                xrefSb.Append("startxref\n");
                xrefSb.Append($"{startXrefPos}\n");
                xrefSb.Append("%%EOF\n");

                WriteString(xrefSb.ToString());

                return _ms.ToArray();
            }

            private void WriteString(string str)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(str);
                _ms.Write(bytes, 0, bytes.Length);
            }

            private void WriteObject(string content)
            {
                _objectOffsets.Add(_ms.Position);
                WriteString(content);
            }

            private void GenerateContentStream(OrderHeader order)
            {
                // Top Header Background (Dark Slate Blue)
                DrawRect(36, 700, 540, 60, "0.10 0.15 0.28", fill: true);

                // Header Titles
                DrawText("ONLINE BOOKSTORE MANAGEMENT", 50, 736, "/F2", 18, "1.0 1.0 1.0");
                DrawText("OFFICIAL TAX INVOICE & ORDER RECEIPT", 50, 716, "/F1", 10, "0.85 0.90 0.98");

                // Invoice Number & Date
                DrawText($"INVOICE #: ORD-{order.Id}", 415, 736, "/F2", 12, "1.0 1.0 1.0");
                DrawText($"DATE: {order.OrderDate:MMM dd, yyyy}", 415, 716, "/F1", 10, "0.85 0.90 0.98");

                // Customer & Delivery Info Card
                DrawRect(36, 570, 260, 115, "0.96 0.97 0.99", strokeColor: "0.80 0.84 0.90", fill: true);
                DrawText("CUSTOMER & SHIPPING DETAILS", 48, 665, "/F2", 10, "0.10 0.15 0.28");
                DrawLine(48, 658, 284, 658, "0.80 0.84 0.90", 0.75f);

                DrawText($"Recipient: {Truncate(order.Name, 30)}", 48, 642, "/F2", 9, "0.15 0.15 0.15");
                DrawText($"Address: {Truncate(order.StreetAddress, 32)}", 48, 626, "/F1", 8.5f, "0.30 0.30 0.30");
                DrawText($"City: {order.City} - {order.PostalCode}", 48, 610, "/F1", 8.5f, "0.30 0.30 0.30");
                DrawText($"Phone: {order.PhoneNumber}", 48, 594, "/F1", 8.5f, "0.30 0.30 0.30");
                if (order.User != null && !string.IsNullOrEmpty(order.User.Email))
                {
                    DrawText($"Email: {Truncate(order.User.Email, 30)}", 48, 578, "/F1", 8.5f, "0.30 0.30 0.30");
                }

                // Order Tracking Info Card
                DrawRect(316, 570, 260, 115, "0.96 0.97 0.99", strokeColor: "0.80 0.84 0.90", fill: true);
                DrawText("ORDER TRACKING & STATUS", 328, 665, "/F2", 10, "0.10 0.15 0.28");
                DrawLine(328, 658, 564, 658, "0.80 0.84 0.90", 0.75f);

                string statusStr = (order.OrderStatus ?? "Pending").ToUpper();
                DrawText($"Order Status: {statusStr}", 328, 642, "/F2", 9.5f, GetStatusRgb(order.OrderStatus));
                DrawText($"Payment: {Truncate(order.PaymentStatus ?? "Approved", 28)}", 328, 626, "/F1", 8.5f, "0.30 0.30 0.30");

                string carrierStr = !string.IsNullOrEmpty(order.Carrier) ? order.Carrier : "Standard Express";
                string trackingStr = !string.IsNullOrEmpty(order.TrackingNumber) ? order.TrackingNumber : $"TRK{order.Id * 884712}";

                DrawText($"Carrier: {Truncate(carrierStr, 28)}", 328, 610, "/F1", 8.5f, "0.30 0.30 0.30");
                DrawText($"Tracking #: {trackingStr}", 328, 594, "/F2", 9, "0.10 0.35 0.75");
                if (order.ShippingDate != default)
                {
                    DrawText($"Dispatched Date: {order.ShippingDate:MMM dd, yyyy}", 328, 578, "/F1", 8.5f, "0.30 0.30 0.30");
                }

                // Table Header Bar
                float tableTop = 540;
                DrawRect(36, tableTop - 22, 540, 22, "0.10 0.15 0.28", fill: true);

                DrawText("ITEM DESCRIPTION", 48, tableTop - 15, "/F2", 9, "1.0 1.0 1.0");
                DrawText("QTY", 370, tableTop - 15, "/F2", 9, "1.0 1.0 1.0");
                DrawText("UNIT PRICE", 425, tableTop - 15, "/F2", 9, "1.0 1.0 1.0");
                DrawText("TOTAL", 515, tableTop - 15, "/F2", 9, "1.0 1.0 1.0");

                float currentY = tableTop - 22;
                int itemIndex = 0;

                if (order.OrderDetails != null)
                {
                    foreach (var item in order.OrderDetails)
                    {
                        currentY -= 24;
                        if (currentY < 120) break; // page boundary guard

                        string rowBg = (itemIndex % 2 == 0) ? "1.0 1.0 1.0" : "0.97 0.98 1.0";
                        DrawRect(36, currentY, 540, 24, rowBg, strokeColor: "0.88 0.90 0.94", fill: true);

                        string title = item.Book?.Title ?? "Book Item";
                        string author = !string.IsNullOrEmpty(item.Book?.Author) ? $" by {item.Book.Author}" : "";
                        string fullItemText = Truncate($"{title}{author}", 50);

                        DrawText(fullItemText, 48, currentY + 7, "/F1", 8.5f, "0.15 0.15 0.15");
                        DrawText(item.Count.ToString(), 378, currentY + 7, "/F2", 8.5f, "0.10 0.10 0.10");
                        DrawText($"INR {item.Price:N2}", 425, currentY + 7, "/F1", 8.5f, "0.25 0.25 0.25");

                        decimal lineTotal = item.Price * item.Count;
                        DrawText($"INR {lineTotal:N2}", 510, currentY + 7, "/F2", 8.5f, "0.10 0.15 0.28");

                        itemIndex++;
                    }
                }

                // Outer Table Outline
                DrawRect(36, currentY, 540, (tableTop - currentY), "0.10 0.15 0.28", fill: false, strokeWidth: 0.75f);

                // Summary Total Box
                float summaryY = currentY - 65;
                DrawRect(336, summaryY, 240, 55, "0.96 0.97 0.99", strokeColor: "0.80 0.84 0.90", fill: true);

                int totalQty = order.OrderDetails?.Sum(d => d.Count) ?? 0;
                DrawText($"Total Items: {totalQty}", 350, summaryY + 36, "/F1", 9, "0.30 0.30 0.30");
                DrawLine(350, summaryY + 28, 560, summaryY + 28, "0.80 0.84 0.90", 0.5f);

                DrawText("GRAND TOTAL:", 350, summaryY + 10, "/F2", 10.5f, "0.10 0.15 0.28");
                DrawText($"INR {order.OrderTotal:N2}", 455, summaryY + 10, "/F2", 12, "0.0 0.55 0.25");

                // Footer Section
                float footerY = 50;
                DrawLine(36, footerY + 20, 576, footerY + 20, "0.80 0.84 0.90", 0.75f);
                DrawText("Thank you for your purchase! For order support, email support@onlinebookstore.com", 36, footerY + 7, "/F3", 8, "0.40 0.40 0.40");
                DrawText($"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC | Online Book Store System", 36, footerY - 5, "/F1", 7.5f, "0.50 0.50 0.50");
            }

            private string GetStatusRgb(string? status)
            {
                return (status ?? "").ToLower() switch
                {
                    "delivered" => "0.0 0.55 0.25",
                    "shipped" => "0.10 0.40 0.85",
                    "processing" => "0.85 0.50 0.0",
                    "cancelled" => "0.80 0.15 0.15",
                    _ => "0.40 0.40 0.40"
                };
            }

            private void DrawText(string text, float x, float y, string font, float fontSize, string colorRgb)
            {
                string safeText = EscapePdfString(text);
                _contentBuilder.Append($"BT {colorRgb} rg {font} {fontSize} Tf 1 0 0 1 {x:F2} {y:F2} Tm ({safeText}) Tj ET\n");
            }

            private void DrawRect(float x, float y, float w, float h, string colorRgb, string? strokeColor = null, bool fill = true, float strokeWidth = 0.5f)
            {
                _contentBuilder.Append("q\n");
                _contentBuilder.Append($"{strokeWidth:F2} w\n");
                if (fill)
                {
                    _contentBuilder.Append($"{colorRgb} rg\n");
                }
                if (strokeColor != null)
                {
                    _contentBuilder.Append($"{strokeColor} RG\n");
                }
                _contentBuilder.Append($"{x:F2} {y:F2} {w:F2} {h:F2} re ");
                if (fill && strokeColor != null)
                {
                    _contentBuilder.Append("B\n");
                }
                else if (fill)
                {
                    _contentBuilder.Append("f\n");
                }
                else
                {
                    _contentBuilder.Append("S\n");
                }
                _contentBuilder.Append("Q\n");
            }

            private void DrawLine(float x1, float y1, float x2, float y2, string colorRgb, float width = 1f)
            {
                _contentBuilder.Append("q\n");
                _contentBuilder.Append($"{width:F2} w {colorRgb} RG\n");
                _contentBuilder.Append($"{x1:F2} {y1:F2} m {x2:F2} {y2:F2} l S\n");
                _contentBuilder.Append("Q\n");
            }

            private string EscapePdfString(string input)
            {
                if (string.IsNullOrEmpty(input)) return string.Empty;
                
                input = input.Replace("₹", "INR ");
                
                StringBuilder sb = new StringBuilder();
                foreach (char c in input)
                {
                    if (c == '\\') sb.Append("\\\\");
                    else if (c == '(') sb.Append("\\(");
                    else if (c == ')') sb.Append("\\)");
                    else if (c >= 32 && c <= 126) sb.Append(c);
                    else sb.Append(' ');
                }
                return sb.ToString();
            }

            private string Truncate(string val, int maxLen)
            {
                if (string.IsNullOrEmpty(val)) return string.Empty;
                return val.Length <= maxLen ? val : val.Substring(0, maxLen - 3) + "...";
            }
        }
    }
}
