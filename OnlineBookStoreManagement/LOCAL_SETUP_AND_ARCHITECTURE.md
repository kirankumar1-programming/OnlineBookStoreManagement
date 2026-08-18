# OnlineBookStoreManagement - Developer Documentation

---

## 1. System Architecture

**OnlineBookStoreManagement** is built on a modern, decoupled **ASP.NET Core 10 MVC** architecture following N-tier design principles:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        PRESENTATION TIER (MVC)                         │
│ Razor Views (.cshtml) | Bootstrap 5 & Glassmorphism CSS | JavaScript   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    v
┌────────────────────────────────────────────────────────────────────────┐
│                       BUSINESS & SECURITY TIER                         │
│ Controllers (Admin, Books, Home, Cart, Account)                       │
│ Authentication: ASP.NET Core Identity & Claim-Based Authorization     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    v
┌────────────────────────────────────────────────────────────────────────┐
│                         DATA ACCESS TIER (ORM)                         │
│ Entity Framework Core 10.0 | ApplicationDbContext                     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    v
┌────────────────────────────────────────────────────────────────────────┐
│                             DATABASE TIER                              │
│ SQLite Database Engine (bookstore.db)                                  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Database Schema

The database consists of 5 core tables managed by Entity Framework Core:

| Table Name | Primary Key | Key Foreign Keys | Purpose |
| :--- | :--- | :--- | :--- |
| **`AspNetUsers`** | `Id` (NVARCHAR) | None | Stores user profiles, credentials, address & role identity claims |
| **`Categories`** | `Id` (INT AUTO) | None | Book genres & category classifications (e.g. Fiction, Technology) |
| **`Books`** | `Id` (INT AUTO) | `CategoryId` -> `Categories.Id` | Main book inventory: Title, Author, Price, StockQuantity, CoverImageUrl |
| **`BookReviews`** | `Id` (INT AUTO) | `BookId` -> `Books.Id`, `UserId` -> `AspNetUsers.Id` | User ratings (1-5 stars) and review comments |
| **`CartItems`** | `Id` (INT AUTO) | `BookId` -> `Books.Id`, `UserId` -> `AspNetUsers.Id` | Active shopping cart items and quantities |

---

## 3. Instructions for Running the Project Locally

### Prerequisites
- **.NET 10 SDK** (or .NET 8/9 SDK with C# 12+)
- Git CLI
- Any Web Browser (Chrome, Edge, Firefox)

### Quick Start Steps

1. **Clone the Repository**:
   ```bash
   git clone https://github.com/kirankumar1-programming/OnlineBookStoreManagement.git
   cd OnlineBookStoreManagement/OnlineBookStoreManagement
   ```

2. **Restore Dependencies**:
   ```bash
   dotnet restore
   ```

3. **Build the Solution**:
   ```bash
   dotnet build
   ```

4. **Run the Application**:
   ```bash
   dotnet run
   ```

5. **Access in Browser**:
   Open your browser and navigate to:  
   👉 **`http://localhost:5059`**

---

### Default Admin Credentials (Seeded Automatically)
- **Admin Email**: `admin@bookstore.com`
- **Admin Password**: `Admin@123`
