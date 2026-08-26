namespace OnlineBookStoreManagement.Models.ViewModels
{
    public class AnalyticsDashboardViewModel
    {
        // KPI Summary Cards
        public decimal TotalRevenue { get; set; }
        public int TotalOrders { get; set; }
        public int TotalBooksSold { get; set; }
        public string TopCategoryName { get; set; } = "N/A";

        // Chart 1: Monthly Revenue
        public MonthlyRevenueChartData MonthlyRevenue { get; set; } = new();

        // Chart 2: Top 5 Best Selling Books
        public TopSellingBooksChartData TopSellingBooks { get; set; } = new();

        // Chart 3: Category Revenue Breakdown
        public CategoryRevenueChartData CategoryRevenue { get; set; } = new();
    }

    public class MonthlyRevenueChartData
    {
        public List<string> Labels { get; set; } = new(); // e.g. ["Mar 2026", "Apr 2026", "May 2026", ...]
        public List<decimal> Revenue { get; set; } = new(); // e.g. [1200.00, 3500.50, ...]
        public List<int> OrderCounts { get; set; } = new(); // e.g. [5, 12, ...]
    }

    public class TopSellingBooksChartData
    {
        public List<string> Labels { get; set; } = new(); // Book Titles
        public List<int> QuantitiesSold { get; set; } = new(); // Units Sold
        public List<decimal> TotalRevenues { get; set; } = new(); // Total $ Sales
    }

    public class CategoryRevenueChartData
    {
        public List<string> Labels { get; set; } = new(); // Category Names
        public List<decimal> Revenues { get; set; } = new(); // Category Revenue Sum
        public List<double> Percentages { get; set; } = new(); // Revenue Share %
    }
}
