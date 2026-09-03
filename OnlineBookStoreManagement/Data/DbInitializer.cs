
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace OnlineBookStoreManagement.Data
{
    public static class DbInitializer
    {
        public const string Role_Admin = "Admin";
        public const string Role_Customer = "Customer";

        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Ensure Database and Schema Tables are Created
            var databaseCreator = context.Database.GetService<IDatabaseCreator>() as RelationalDatabaseCreator;
            if (databaseCreator != null)
            {
                if (!await databaseCreator.ExistsAsync())
                {
                    await databaseCreator.CreateAsync();
                }

                // Verify if application & identity tables exist
                bool tablesExist = false;
                try
                {
                    tablesExist = await databaseCreator.HasTablesAsync();
                    if (tablesExist)
                    {
                        // Explicitly check if AspNetUsers exists to avoid Azure SQL system-table false positives
                        _ = await userManager.Users.Take(1).ToListAsync();
                    }
                }
                catch
                {
                    tablesExist = false;
                }

                if (!tablesExist)
                {
                    try
                    {
                        await databaseCreator.CreateTablesAsync();
                    }
                    catch
                    {
                        await context.Database.EnsureCreatedAsync();
                    }
                }
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

            // Schema Migration Guard for Existing SQLite Database Files
            if (context.Database.IsSqlite())
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""Coupons"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Coupons"" PRIMARY KEY AUTOINCREMENT,
                            ""Code"" TEXT NOT NULL,
                            ""Description"" TEXT NOT NULL,
                            ""DiscountType"" TEXT NOT NULL,
                            ""DiscountValue"" TEXT NOT NULL,
                            ""MinimumOrderAmount"" TEXT NOT NULL,
                            ""MaximumDiscountAmount"" TEXT NULL,
                            ""IsActive"" INTEGER NOT NULL,
                            ""StartDate"" TEXT NULL,
                            ""ExpiryDate"" TEXT NULL,
                            ""UsageLimit"" INTEGER NULL,
                            ""TimesUsed"" INTEGER NOT NULL DEFAULT 0
                        );");
                }
                catch { /* Table already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE Coupons ADD COLUMN StartDate TEXT NULL;");
                }
                catch { /* Column already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE Coupons ADD COLUMN UsageLimit INTEGER NULL;");
                }
                catch { /* Column already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE Coupons ADD COLUMN TimesUsed INTEGER NOT NULL DEFAULT 0;");
                }
                catch { /* Column already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE OrderHeaders ADD COLUMN CouponCode TEXT NULL;");
                }
                catch { /* Column already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE OrderHeaders ADD COLUMN DiscountAmount TEXT NOT NULL DEFAULT '0.00';");
                }
                catch { /* Column already exists */ }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""WishlistItems"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WishlistItems"" PRIMARY KEY AUTOINCREMENT,
                            ""UserId"" TEXT NOT NULL,
                            ""BookId"" INTEGER NOT NULL,
                            ""CreatedAt"" TEXT NOT NULL,
                            CONSTRAINT ""FK_WishlistItems_AspNetUsers_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE CASCADE,
                            CONSTRAINT ""FK_WishlistItems_Books_BookId"" FOREIGN KEY (""BookId"") REFERENCES ""Books"" (""Id"") ON DELETE CASCADE
                        );");
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WishlistItems_UserId_BookId"" ON ""WishlistItems"" (""UserId"", ""BookId"");");
                }
                catch { /* Table/index already exists */ }
            }

            // 1. Seed Roles
            if (!await roleManager.RoleExistsAsync(Role_Admin))
            {
                await roleManager.CreateAsync(new IdentityRole(Role_Admin));
            }
            if (!await roleManager.RoleExistsAsync(Role_Customer))
            {
                await roleManager.CreateAsync(new IdentityRole(Role_Customer));
            }

            // 2. Seed Default Admin User
            var adminEmail = "admin@bookstore.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    Address = "123 Tech Park, MG Road",
                    City = "Bengaluru",
                    PostalCode = "560001"
                };

                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, Role_Admin);
                }
            }

            // 3. Seed Default Customer User
            var customerEmail = "customer@bookstore.com";
            var customerUser = await userManager.FindByEmailAsync(customerEmail);
            if (customerUser == null)
            {
                customerUser = new ApplicationUser
                {
                    UserName = customerEmail,
                    Email = customerEmail,
                    FullName = "Rohan Sharma",
                    EmailConfirmed = true,
                    Address = "45 Park Street",
                    City = "Mumbai",
                    PostalCode = "400001"
                };

                var result = await userManager.CreateAsync(customerUser, "Customer@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(customerUser, Role_Customer);
                }
            }

            // 4. Seed Categories
            if (!await context.Categories.AnyAsync())
            {
                var categories = new List<Category>
                {
                    new Category { Name = "Technology & Code", Description = "Programming, AI, Architecture, and Software Engineering", DisplayOrder = 1 },
                    new Category { Name = "Fiction & Classics", Description = "Timeless literary masterworks and modern novels", DisplayOrder = 2 },
                    new Category { Name = "Sci-Fi & Fantasy", Description = "Epic worlds, futuristic tech, and space exploration", DisplayOrder = 3 },
                    new Category { Name = "Business & Leadership", Description = "Entrepreneurship, strategy, finance, and career growth", DisplayOrder = 4 },
                    new Category { Name = "Science & Philosophy", Description = "Understanding the universe, mind, and nature", DisplayOrder = 5 }
                };

                await context.Categories.AddRangeAsync(categories);
                await context.SaveChangesAsync();
            }

            // 5. Seed Books with Reliable OpenLibrary ISBN Cover URLs & High Quality Fallbacks
            if (!await context.Books.AnyAsync())
            {
                var techCategory = await context.Categories.FirstAsync(c => c.Name == "Technology & Code");
                var fictionCategory = await context.Categories.FirstAsync(c => c.Name == "Fiction & Classics");
                var scifiCategory = await context.Categories.FirstAsync(c => c.Name == "Sci-Fi & Fantasy");
                var bizCategory = await context.Categories.FirstAsync(c => c.Name == "Business & Leadership");
                var sciCategory = await context.Categories.FirstAsync(c => c.Name == "Science & Philosophy");

                var books = new List<Book>
                {
                    new Book
                    {
                        Title = "Clean Architecture: Craftsman's Guide",
                        Author = "Robert C. Martin",
                        ISBN = "978-0134494166",
                        Price = 899.00m,
                        StockQuantity = 25,
                        CategoryId = techCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780134494166-L.jpg",
                        Description = "Practical software architecture solutions for building scalable, maintainable, and robust applications. Master universal rules of software structure."
                    },
                    new Book
                    {
                        Title = "Designing Data-Intensive Applications",
                        Author = "Martin Kleppmann",
                        ISBN = "978-1449373320",
                        Price = 1299.00m,
                        StockQuantity = 18,
                        CategoryId = techCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9781449373320-L.jpg",
                        Description = "The definitive guide to the architecture, storage, processing, and reliability of large-scale distributed systems and databases."
                    },
                    new Book
                    {
                        Title = "Dune: 50th Anniversary Edition",
                        Author = "Frank Herbert",
                        ISBN = "978-0441172719",
                        Price = 599.00m,
                        StockQuantity = 40,
                        CategoryId = scifiCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780441172719-L.jpg",
                        Description = "Set on the desert planet Arrakis, Dune is the story of the boy Paul Atreides, heir to a noble family in a galactic empire."
                    },
                    new Book
                    {
                        Title = "The Great Gatsby",
                        Author = "F. Scott Fitzgerald",
                        ISBN = "978-0743273565",
                        Price = 349.00m,
                        StockQuantity = 30,
                        CategoryId = fictionCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780743273565-L.jpg",
                        Description = "A quintessential American novel depicting the glamour, tragedy, and decadence of the Roaring Twenties on Long Island."
                    },
                    new Book
                    {
                        Title = "Atomic Habits",
                        Author = "James Clear",
                        ISBN = "978-0735211292",
                        Price = 499.00m,
                        StockQuantity = 50,
                        CategoryId = bizCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780735211292-L.jpg",
                        Description = "An easy and proven way to build good habits and break bad ones. Transform your personal and professional life with tiny changes."
                    },
                    new Book
                    {
                        Title = "Astrophysics for People in a Hurry",
                        Author = "Neil deGrasse Tyson",
                        ISBN = "978-0393609394",
                        Price = 450.00m,
                        StockQuantity = 22,
                        CategoryId = sciCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780393609394-L.jpg",
                        Description = "What is the nature of space and time? How do we fit within the universe? Neil deGrasse Tyson brings the cosmos down to Earth."
                    },
                    new Book
                    {
                        Title = "The Pragmatic Programmer",
                        Author = "David Thomas & Andrew Hunt",
                        ISBN = "978-0135957059",
                        Price = 1150.00m,
                        StockQuantity = 15,
                        CategoryId = techCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780135957059-L.jpg",
                        Description = "Illustrates best practices and major pitfalls of modern software development, covering developer mindset, tooling, and refactoring."
                    },
                    new Book
                    {
                        Title = "Zero to One: Notes on Startups",
                        Author = "Peter Thiel",
                        ISBN = "978-0804139298",
                        Price = 475.00m,
                        StockQuantity = 35,
                        CategoryId = bizCategory.Id,
                        CoverImageUrl = "https://covers.openlibrary.org/b/isbn/9780804139298-L.jpg",
                        Description = "Great business minds share insights on how to build the future, create true innovation, and escape competition."
                    }
                };

                await context.Books.AddRangeAsync(books);
                await context.SaveChangesAsync();
            }

            // 6. Seed Sample Book Reviews
            if (!await context.BookReviews.AnyAsync())
            {
                var firstBook = await context.Books.FirstOrDefaultAsync(b => b.Title.Contains("Clean Architecture"));
                var secondBook = await context.Books.FirstOrDefaultAsync(b => b.Title.Contains("Atomic Habits"));
                var sampleCustomer = await userManager.FindByEmailAsync("customer@bookstore.com");

                if (sampleCustomer != null)
                {
                    var reviews = new List<BookReview>();

                    if (firstBook != null)
                    {
                        reviews.Add(new BookReview
                        {
                            BookId = firstBook.Id,
                            UserId = sampleCustomer.Id,
                            Rating = 5,
                            Comment = "Must-read for every backend engineer! The concepts of SOLID and decoupled architecture are explained with crystal clear clarity.",
                            ReviewDate = DateTime.UtcNow.AddDays(-5)
                        });
                    }

                    if (secondBook != null)
                    {
                        reviews.Add(new BookReview
                        {
                            BookId = secondBook.Id,
                            UserId = sampleCustomer.Id,
                            Rating = 5,
                            Comment = "Life changing book! Small 1% improvements everyday really add up over time.",
                            ReviewDate = DateTime.UtcNow.AddDays(-2)
                        });
                    }

                    if (reviews.Any())
                    {
                        await context.BookReviews.AddRangeAsync(reviews);
                        await context.SaveChangesAsync();
                    }
                }
            }

            // 7. Seed Sample Orders for Historical Visual Analytics
            if (!await context.OrderHeaders.AnyAsync())
            {
                var sampleCustomer = await userManager.FindByEmailAsync("customer@bookstore.com");
                var books = await context.Books.Include(b => b.Category).ToListAsync();

                if (sampleCustomer != null && books.Any())
                {
                    var cleanArch = books.FirstOrDefault(b => b.Title.Contains("Clean Architecture")) ?? books[0];
                    var dataIntensive = books.FirstOrDefault(b => b.Title.Contains("Designing Data-Intensive")) ?? books[0];
                    var dune = books.FirstOrDefault(b => b.Title.Contains("Dune")) ?? books[0];
                    var gatsby = books.FirstOrDefault(b => b.Title.Contains("Great Gatsby")) ?? books[0];
                    var atomic = books.FirstOrDefault(b => b.Title.Contains("Atomic Habits")) ?? books[0];
                    var astro = books.FirstOrDefault(b => b.Title.Contains("Astrophysics")) ?? books[0];
                    var pragmatic = books.FirstOrDefault(b => b.Title.Contains("Pragmatic Programmer")) ?? books[0];
                    var zeroToOne = books.FirstOrDefault(b => b.Title.Contains("Zero to One")) ?? books[0];

                    var now = DateTime.UtcNow;

                    var sampleOrders = new List<(DateTime date, string status, List<(Book book, int count)> items)>
                    {
                        // 5 Months Ago
                        (now.AddMonths(-5), "Approved", new() { (atomic, 3), (gatsby, 2), (astro, 1) }),
                        (now.AddMonths(-5).AddDays(5), "Approved", new() { (dune, 4), (cleanArch, 2) }),

                        // 4 Months Ago
                        (now.AddMonths(-4), "Approved", new() { (dataIntensive, 2), (pragmatic, 3), (atomic, 4) }),
                        (now.AddMonths(-4).AddDays(8), "Approved", new() { (zeroToOne, 5), (gatsby, 3) }),

                        // 3 Months Ago
                        (now.AddMonths(-3), "Approved", new() { (cleanArch, 4), (dune, 5), (atomic, 6) }),
                        (now.AddMonths(-3).AddDays(12), "Approved", new() { (dataIntensive, 3), (astro, 4) }),

                        // 2 Months Ago
                        (now.AddMonths(-2), "Approved", new() { (pragmatic, 5), (atomic, 8), (gatsby, 4) }),
                        (now.AddMonths(-2).AddDays(10), "Approved", new() { (cleanArch, 6), (zeroToOne, 4) }),

                        // 1 Month Ago
                        (now.AddMonths(-1), "Approved", new() { (dune, 7), (dataIntensive, 4), (atomic, 10) }),
                        (now.AddMonths(-1).AddDays(15), "Approved", new() { (pragmatic, 6), (cleanArch, 5), (astro, 3) }),

                        // Current Month
                        (now.AddDays(-10), "Approved", new() { (atomic, 12), (cleanArch, 7), (zeroToOne, 6) }),
                        (now.AddDays(-2), "Approved", new() { (dataIntensive, 5), (dune, 8), (pragmatic, 4) })
                    };

                    foreach (var (orderDate, status, items) in sampleOrders)
                    {
                        decimal total = items.Sum(i => i.book.Price * i.count);

                        var header = new OrderHeader
                        {
                            UserId = sampleCustomer.Id,
                            OrderDate = orderDate,
                            ShippingDate = orderDate.AddDays(2),
                            OrderTotal = total,
                            OrderStatus = status,
                            PaymentStatus = "Approved",
                            TrackingNumber = $"TRK-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                            Carrier = "FedEx Express",
                            Name = sampleCustomer.FullName ?? "Rohan Sharma",
                            PhoneNumber = "9876543210",
                            StreetAddress = "45 Park Street",
                            City = "Mumbai",
                            PostalCode = "400001"
                        };

                        context.OrderHeaders.Add(header);
                        await context.SaveChangesAsync();

                        foreach (var (book, count) in items)
                        {
                            var detail = new OrderDetail
                            {
                                OrderHeaderId = header.Id,
                                BookId = book.Id,
                                Count = count,
                                Price = book.Price
                            };
                            context.OrderDetails.Add(detail);
                        }
                        await context.SaveChangesAsync();
                    }
                }
            }

            // 8. Seed Discount Coupons
            if (!await context.Coupons.AnyAsync())
            {
                var coupons = new List<Coupon>
                {
                    new Coupon
                    {
                        Code = "WELCOME10",
                        Description = "10% discount on all books (No minimum order)",
                        DiscountType = "Percentage",
                        DiscountValue = 10m,
                        MinimumOrderAmount = 0m,
                        IsActive = true,
                        ExpiryDate = DateTime.UtcNow.AddYears(1)
                    },
                    new Coupon
                    {
                        Code = "BOOKWORM20",
                        Description = "20% discount (up to ₹200) on minimum order of ₹500",
                        DiscountType = "Percentage",
                        DiscountValue = 20m,
                        MinimumOrderAmount = 500m,
                        MaximumDiscountAmount = 200m,
                        IsActive = true,
                        ExpiryDate = DateTime.UtcNow.AddYears(1)
                    },
                    new Coupon
                    {
                        Code = "FLAT100",
                        Description = "Flat ₹100 discount on minimum order of ₹600",
                        DiscountType = "Flat",
                        DiscountValue = 100m,
                        MinimumOrderAmount = 600m,
                        IsActive = true,
                        ExpiryDate = DateTime.UtcNow.AddYears(1)
                    },
                    new Coupon
                    {
                        Code = "SAVE50",
                        Description = "Flat ₹50 discount on minimum order of ₹300",
                        DiscountType = "Flat",
                        DiscountValue = 50m,
                        MinimumOrderAmount = 300m,
                        IsActive = true,
                        ExpiryDate = DateTime.UtcNow.AddYears(1)
                    }
                };

                await context.Coupons.AddRangeAsync(coupons);
                await context.SaveChangesAsync();
            }
        }
    }
}
