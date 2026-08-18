using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;

namespace OnlineBookStoreManagement.Services
{
    public class BookBulkUploadService : IBookBulkUploadService
    {
        private readonly ApplicationDbContext _context;

        public BookBulkUploadService(ApplicationDbContext context)
        {
            _context = context;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        public async Task<BulkUploadResultViewModel> ProcessBulkUploadAsync(IFormFile file)
        {
            var result = new BulkUploadResultViewModel
            {
                FileName = file?.FileName ?? "Uploaded File"
            };

            if (file == null || file.Length == 0)
            {
                result.RowResults.Add(new BulkUploadRowResult
                {
                    RowNumber = 0,
                    Status = "Failed",
                    Messages = new List<string> { "Uploaded file is empty or missing." }
                });
                result.FailureCount = 1;
                return result;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            List<Dictionary<string, string>> rawRows = new List<Dictionary<string, string>>();

            try
            {
                using var stream = file.OpenReadStream();
                if (extension == ".csv" || extension == ".txt" || extension == ".tsv")
                {
                    rawRows = ParseCsvStream(stream);
                }
                else if (extension == ".xlsx" || extension == ".xls")
                {
                    rawRows = ParseExcelStream(stream);
                }
                else
                {
                    result.RowResults.Add(new BulkUploadRowResult
                    {
                        RowNumber = 0,
                        Status = "Failed",
                        Messages = new List<string> { $"Unsupported file format '{extension}'. Please upload a .csv, .xlsx, or .xls file." }
                    });
                    result.FailureCount = 1;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.RowResults.Add(new BulkUploadRowResult
                {
                    RowNumber = 0,
                    Status = "Failed",
                    Messages = new List<string> { $"Error parsing file: {ex.Message}" }
                });
                result.FailureCount = 1;
                return result;
            }

            if (rawRows.Count == 0)
            {
                result.RowResults.Add(new BulkUploadRowResult
                {
                    RowNumber = 0,
                    Status = "Failed",
                    Messages = new List<string> { "No data rows found in the file." }
                });
                result.FailureCount = 1;
                return result;
            }

            result.TotalRowsProcessed = rawRows.Count;
            var categories = await _context.Categories.ToListAsync();
            var categoryDict = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in categories)
            {
                categoryDict[cat.Name.Trim()] = cat;
            }

            int rowIndex = 1; // Header is row 1, data starts at row 2
            var booksToInsert = new List<Book>();

            foreach (var rowData in rawRows)
            {
                rowIndex++;
                var rowResult = new BulkUploadRowResult
                {
                    RowNumber = rowIndex
                };

                string title = GetValue(rowData, "Title", "Book Title", "Name");
                string author = GetValue(rowData, "Author", "Book Author");
                string isbn = GetValue(rowData, "ISBN", "ISBN13", "ISBN10");
                string priceStr = GetValue(rowData, "Price", "MRP", "Cost");
                string stockStr = GetValue(rowData, "StockQuantity", "Stock", "Quantity", "Copies");
                string description = GetValue(rowData, "Description", "Summary", "Details");
                string categoryName = GetValue(rowData, "Category", "Category Name", "Genre");
                string coverImageUrl = GetValue(rowData, "CoverImageUrl", "Cover Image", "Image URL", "ImageUrl");

                rowResult.Title = title;
                rowResult.Author = author;
                rowResult.ISBN = isbn;

                bool isValid = true;

                // Validation: Title
                if (string.IsNullOrWhiteSpace(title))
                {
                    rowResult.Messages.Add("Title is required.");
                    isValid = false;
                }

                // Validation: Author
                if (string.IsNullOrWhiteSpace(author))
                {
                    rowResult.Messages.Add("Author is required.");
                    isValid = false;
                }

                // Validation: Price
                decimal price = 0;
                if (string.IsNullOrWhiteSpace(priceStr) || !decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out price) || price < 0)
                {
                    // Fallback to local culture if invariant fails
                    if (string.IsNullOrWhiteSpace(priceStr) || !decimal.TryParse(priceStr, out price) || price < 0)
                    {
                        rowResult.Messages.Add($"Invalid or missing price value '{priceStr}'. Price must be a valid positive number.");
                        isValid = false;
                    }
                }

                // Validation: Stock Quantity
                int stockQuantity = 0;
                if (!string.IsNullOrWhiteSpace(stockStr))
                {
                    if (!int.TryParse(stockStr, out stockQuantity) || stockQuantity < 0)
                    {
                        rowResult.Messages.Add($"Invalid stock quantity '{stockStr}'. Setting stock to 0 as warning.");
                        rowResult.Status = "Warning";
                        stockQuantity = 0;
                    }
                }

                // Validation: Category
                Category? category = null;
                if (!string.IsNullOrWhiteSpace(categoryName))
                {
                    categoryName = categoryName.Trim();
                    if (categoryDict.TryGetValue(categoryName, out var existingCategory))
                    {
                        category = existingCategory;
                    }
                    else
                    {
                        // Auto-create category if missing
                        category = new Category
                        {
                            Name = categoryName,
                            Description = $"Auto-created during bulk upload on {DateTime.UtcNow:yyyy-MM-dd}",
                            DisplayOrder = 10,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Categories.Add(category);
                        await _context.SaveChangesAsync();
                        categoryDict[categoryName] = category;
                        rowResult.Messages.Add($"Category '{categoryName}' did not exist and was created automatically.");
                    }
                }
                else
                {
                    // Fallback to first available category or create General
                    category = categoryDict.Values.FirstOrDefault();
                    if (category == null)
                    {
                        category = new Category
                        {
                            Name = "General",
                            Description = "General Category",
                            DisplayOrder = 1,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Categories.Add(category);
                        await _context.SaveChangesAsync();
                        categoryDict["General"] = category;
                    }
                    rowResult.Messages.Add($"No category provided. Assigned to '{category.Name}'.");
                }

                // ISBN fallback
                if (string.IsNullOrWhiteSpace(isbn))
                {
                    isbn = $"978-{Random.Shared.Next(100000000, 999999999)}";
                    rowResult.ISBN = isbn;
                    rowResult.Messages.Add($"ISBN missing. Auto-generated ISBN '{isbn}'.");
                }

                // Cover Image fallback
                if (string.IsNullOrWhiteSpace(coverImageUrl))
                {
                    coverImageUrl = "/images/default-book.png";
                }

                if (!isValid)
                {
                    rowResult.Status = "Failed";
                    result.FailureCount++;
                }
                else
                {
                    if (rowResult.Status != "Warning")
                    {
                        rowResult.Status = "Success";
                        rowResult.Messages.Add("Book added successfully.");
                    }
                    result.SuccessCount++;

                    var newBook = new Book
                    {
                        Title = title.Trim(),
                        Author = author.Trim(),
                        ISBN = isbn.Trim(),
                        Price = price,
                        StockQuantity = stockQuantity,
                        Description = description?.Trim() ?? string.Empty,
                        CoverImageUrl = coverImageUrl.Trim(),
                        CategoryId = category.Id,
                        CreatedAt = DateTime.UtcNow
                    };

                    booksToInsert.Add(newBook);
                    result.ImportedBooks.Add(newBook);
                }

                result.RowResults.Add(rowResult);
            }

            if (booksToInsert.Count > 0)
            {
                await _context.Books.AddRangeAsync(booksToInsert);
                await _context.SaveChangesAsync();
            }

            result.WarningCount = result.RowResults.Count(r => r.Status == "Warning");
            return result;
        }

        private string GetValue(Dictionary<string, string> rowData, params string[] possibleKeys)
        {
            foreach (var key in possibleKeys)
            {
                var match = rowData.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key) && !string.IsNullOrWhiteSpace(match.Value))
                {
                    return match.Value;
                }
            }
            return string.Empty;
        }

        private List<Dictionary<string, string>> ParseCsvStream(Stream stream)
        {
            var rows = new List<Dictionary<string, string>>();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine)) return rows;

            var headers = ParseCsvLine(headerLine);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var values = ParseCsvLine(line);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < headers.Count; i++)
                {
                    var header = headers[i].Trim();
                    var val = i < values.Count ? values[i].Trim() : string.Empty;
                    dict[header] = val;
                }

                rows.Add(dict);
            }

            return rows;
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            bool inQuotes = false;
            var currentField = new StringBuilder();

            char delimiter = line.Contains('\t') ? '\t' : (line.Contains(';') ? ';' : ',');

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result;
        }

        private List<Dictionary<string, string>> ParseExcelStream(Stream stream)
        {
            var rows = new List<Dictionary<string, string>>();

            using var reader = ExcelReaderFactory.CreateReader(stream);
            if (!reader.Read()) return rows;

            // Header row
            var headers = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i)?.ToString()?.Trim() ?? $"Column_{i}";
                headers.Add(val);
            }

            // Data rows
            while (reader.Read())
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool hasData = false;

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var header = i < headers.Count ? headers[i] : $"Column_{i}";
                    var val = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(val)) hasData = true;
                    dict[header] = val;
                }

                if (hasData)
                {
                    rows.Add(dict);
                }
            }

            return rows;
        }

        public byte[] GenerateSampleCsvTemplate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Title,Author,ISBN,Price,StockQuantity,Description,Category,CoverImageUrl");
            sb.AppendLine("\"Clean Code: A Handbook of Agile Software Craftsmanship\",\"Robert C. Martin\",\"978-0132350884\",699.00,50,\"Even bad code can function. But if code isn't clean, it can bring a development organization to its knees.\",\"Technology\",\"/images/default-book.png\"");
            sb.AppendLine("\"Atomic Habits\",\"James Clear\",\"978-1847941831\",499.00,100,\"An Easy & Proven Way to Build Good Habits & Break Bad Ones.\",\"Self-Help\",\"/images/default-book.png\"");
            sb.AppendLine("\"The Pragmatic Programmer\",\"Andrew Hunt, David Thomas\",\"978-0135957059\",850.00,30,\"Your Journey To Mastery, 20th Anniversary Edition.\",\"Technology\",\"/images/default-book.png\"");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] GenerateSampleExcelTemplate()
        {
            // For Excel template, CSV formatted stream bytes with UTF-8 BOM can open cleanly in Microsoft Excel!
            var sb = new StringBuilder();
            sb.AppendLine("Title,Author,ISBN,Price,StockQuantity,Description,Category,CoverImageUrl");
            sb.AppendLine("\"Clean Code: A Handbook of Agile Software Craftsmanship\",\"Robert C. Martin\",\"978-0132350884\",699.00,50,\"Even bad code can function. But if code isn't clean, it can bring a development organization to its knees.\",\"Technology\",\"/images/default-book.png\"");
            sb.AppendLine("\"Atomic Habits\",\"James Clear\",\"978-1847941831\",499.00,100,\"An Easy & Proven Way to Build Good Habits & Break Bad Ones.\",\"Self-Help\",\"/images/default-book.png\"");
            sb.AppendLine("\"The Pragmatic Programmer\",\"Andrew Hunt, David Thomas\",\"978-0135957059\",850.00,30,\"Your Journey To Mastery, 20th Anniversary Edition.\",\"Technology\",\"/images/default-book.png\"");
            
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var preamble = Encoding.UTF8.GetPreamble();
            var result = new byte[preamble.Length + bytes.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(bytes, 0, result, preamble.Length, bytes.Length);
            return result;
        }
    }
}
