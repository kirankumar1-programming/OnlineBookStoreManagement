
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;

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

            // Ensure Database is Created
            await context.Database.EnsureCreatedAsync();

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
        }
    }
}
