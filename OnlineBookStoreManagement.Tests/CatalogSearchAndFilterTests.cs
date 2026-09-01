using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookStoreManagement.Controllers;
using OnlineBookStoreManagement.Data;
using OnlineBookStoreManagement.Models;
using OnlineBookStoreManagement.Models.ViewModels;
using Xunit;

namespace OnlineBookStoreManagement.Tests
{
    public class CatalogSearchAndFilterTests
    {
        private async Task<ApplicationDbContext> GetDatabaseContextAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared")
                .Options;

            var context = new ApplicationDbContext(options);
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();

            // Seed Categories
            var techCategory = new Category { Name = "Technology", DisplayOrder = 1 };
            var fictionCategory = new Category { Name = "Fiction", DisplayOrder = 2 };
            context.Categories.AddRange(techCategory, fictionCategory);
            await context.SaveChangesAsync();

            // Seed User
            var user1 = new ApplicationUser { Id = "user-1", UserName = "user1@test.com", Email = "user1@test.com", FullName = "Test User 1" };
            var user2 = new ApplicationUser { Id = "user-2", UserName = "user2@test.com", Email = "user2@test.com", FullName = "Test User 2" };
            context.Users.AddRange(user1, user2);
            await context.SaveChangesAsync();

            // Seed Books
            var book1 = new Book
            {
                Id = 1,
                Title = "Clean Code: Craftsman's Handbook",
                Author = "Robert C. Martin",
                ISBN = "978-0132350884",
                Price = 600.00m,
                StockQuantity = 10,
                CategoryId = techCategory.Id,
                Description = "Agile software craftsmanship and refactoring patterns."
            };

            var book2 = new Book
            {
                Id = 2,
                Title = "Designing Data-Intensive Applications",
                Author = "Martin Kleppmann",
                ISBN = "978-1449373320",
                Price = 1200.00m,
                StockQuantity = 15,
                CategoryId = techCategory.Id,
                Description = "Distributed systems, storage engines, and reliability."
            };

            var book3 = new Book
            {
                Id = 3,
                Title = "Clean Architecture",
                Author = "Robert C. Martin",
                ISBN = "978-0134494166",
                Price = 850.00m,
                StockQuantity = 20,
                CategoryId = techCategory.Id,
                Description = "System architecture structure and boundaries."
            };

            var book4 = new Book
            {
                Id = 4,
                Title = "Dune",
                Author = "Frank Herbert",
                ISBN = "978-0441172719",
                Price = 400.00m,
                StockQuantity = 5,
                CategoryId = fictionCategory.Id,
                Description = "Epic sci-fi novel set on desert planet Arrakis."
            };

            context.Books.AddRange(book1, book2, book3, book4);
            await context.SaveChangesAsync();

            // Seed Reviews
            context.BookReviews.AddRange(
                new BookReview { BookId = 1, UserId = "user-1", Rating = 5, Comment = "Must read!" },
                new BookReview { BookId = 1, UserId = "user-2", Rating = 5, Comment = "Excellent!" }, // Avg 5.0
                new BookReview { BookId = 2, UserId = "user-1", Rating = 4, Comment = "Very informative" }, // Avg 4.0
                new BookReview { BookId = 3, UserId = "user-1", Rating = 2, Comment = "Average" } // Avg 2.0
                // Book 4 has no reviews (Avg 0)
            );
            await context.SaveChangesAsync();

            return context;
        }

        [Fact]
        public async Task Index_MultiKeywordSearch_MatchesAcrossTitleAuthorDescriptionAndISBN()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            // Search for multi-word keywords "craftsmanship refactoring"
            var result = await controller.Index(null, null, null, null, "craftsmanship refactoring", null, null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Single(model.Books);
            Assert.Equal("Clean Code: Craftsman's Handbook", model.Books.First().Title);
        }

        [Fact]
        public async Task Index_AuthorFilter_ReturnsOnlyBooksBySelectedAuthor()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, "Robert C. Martin", null, null, null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Equal(2, model.Books.Count());
            Assert.All(model.Books, b => Assert.Equal("Robert C. Martin", b.Author));
        }

        [Fact]
        public async Task Index_MinRatingFilter_ReturnsOnlyBooksMeetingThreshold()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            // Filter books with 4.0+ rating
            var result = await controller.Index(null, null, null, null, null, null, null, null, null, 4.0);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Equal(2, model.Books.Count()); // Book 1 (5.0) and Book 2 (4.0)
            Assert.Contains(model.Books, b => b.Id == 1);
            Assert.Contains(model.Books, b => b.Id == 2);
            Assert.DoesNotContain(model.Books, b => b.Id == 3);
            Assert.DoesNotContain(model.Books, b => b.Id == 4);
        }

        [Fact]
        public async Task Index_MultiFilterCombination_AppliesAllFiltersTogether()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            // Category=Technology, Author="Robert C. Martin", MinPrice=500, MinRating=4.0
            var result = await controller.Index(null, 1, null, "Robert C. Martin", null, null, 500.00m, 1000.00m, null, 4.0);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Single(model.Books);
            Assert.Equal(1, model.Books.First().Id); // Clean Code
        }

        [Fact]
        public async Task Index_Sorting_RatingDesc_SortsHighestRatedFirst()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, null, null, "rating_desc", null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            var booksList = model.Books.ToList();
            Assert.Equal(4, booksList.Count);
            Assert.Equal(1, booksList[0].Id); // Avg 5.0
            Assert.Equal(2, booksList[1].Id); // Avg 4.0
            Assert.Equal(3, booksList[2].Id); // Avg 2.0
            Assert.Equal(4, booksList[3].Id); // Avg 0.0
        }

        [Fact]
        public async Task Index_Sorting_PriceAsc_SortsLowestPriceFirst()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, null, null, "price_asc", null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            var booksList = model.Books.ToList();
            Assert.Equal(4, booksList.Count);
            Assert.Equal(400.00m, booksList[0].Price); // Dune (400)
            Assert.Equal(600.00m, booksList[1].Price); // Clean Code (600)
            Assert.Equal(850.00m, booksList[2].Price); // Clean Architecture (850)
            Assert.Equal(1200.00m, booksList[3].Price); // Designing Data-Intensive (1200)
        }

        [Fact]
        public async Task Index_Sorting_PriceDesc_SortsHighestPriceFirst()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, null, null, "price_desc", null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            var booksList = model.Books.ToList();
            Assert.Equal(4, booksList.Count);
            Assert.Equal(1200.00m, booksList[0].Price);
            Assert.Equal(850.00m, booksList[1].Price);
            Assert.Equal(600.00m, booksList[2].Price);
            Assert.Equal(400.00m, booksList[3].Price);
        }

        [Fact]
        public async Task Index_Sorting_Newest_SortsNewestArrivalsFirst()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, null, null, "newest", null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            var booksList = model.Books.ToList();
            Assert.Equal(4, booksList.Count);
            Assert.Equal(4, booksList[0].Id); // Highest Id / Newest arrival
        }

        [Fact]
        public async Task Index_PopulatesDistinctAuthorsList()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            var result = await controller.Index(null, null, null, null, null, null, null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Equal(3, model.Authors.Count());
            Assert.Contains("Frank Herbert", model.Authors);
            Assert.Contains("Martin Kleppmann", model.Authors);
            Assert.Contains("Robert C. Martin", model.Authors);
        }

        [Fact]
        public async Task Index_MultiSelectAuthors_ReturnsBooksByAnySelectedAuthor()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            // Select Robert C. Martin AND Frank Herbert
            var selectedAuthors = new List<string> { "Robert C. Martin", "Frank Herbert" };
            var result = await controller.Index(null, null, selectedAuthors, null, null, null, null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Equal(3, model.Books.Count()); // 2 by Robert C. Martin + 1 by Frank Herbert
            Assert.All(model.Books, b => Assert.True(b.Author == "Robert C. Martin" || b.Author == "Frank Herbert"));
        }

        [Fact]
        public async Task Index_MultiSelectCategories_ReturnsBooksInAnySelectedCategory()
        {
            using var context = await GetDatabaseContextAsync();
            var controller = new HomeController(context, null!);

            // Category 1 (Tech) and Category 2 (Fiction)
            var selectedCategories = new List<int> { 1, 2 };
            var result = await controller.Index(selectedCategories, null, null, null, null, null, null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StoreIndexViewModel>(viewResult.Model);

            Assert.Equal(4, model.Books.Count());
        }

        [Fact]
        public void StoreIndexViewModel_GetQueryString_GeneratesCorrectMultiValueUrls()
        {
            var model = new StoreIndexViewModel
            {
                SelectedCategoryIds = new List<int> { 1, 2 },
                SelectedAuthors = new List<string> { "Robert C. Martin", "Martin Kleppmann" },
                SelectedRatings = new List<double> { 4.0 },
                SearchTerm = "clean code",
                SortBy = "newest",
                CurrentPage = 2
            };

            string queryString = model.GetQueryString();

            Assert.Contains("page=2", queryString);
            Assert.Contains("searchTerm=clean%20code", queryString);
            Assert.Contains("categoryIds=1", queryString);
            Assert.Contains("categoryIds=2", queryString);
            Assert.Contains("authors=Robert%20C.%20Martin", queryString);
            Assert.Contains("authors=Martin%20Kleppmann", queryString);
            Assert.Contains("minRatings=4", queryString);
        }
    }
}
