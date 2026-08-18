# Technical Architecture & System Documentation
## OnlineBookStoreManagement System (ASP.NET Core 10 & Azure Web Apps)

---

> [!NOTE]
> **Document Version**: 1.0.0  
> **Target Framework**: .NET 10.0 Enterprise Edition  
> **Deployment Target**: Azure App Service (`app-onlinebookstoremanagement`)  
> **CI/CD Platform**: GitHub Actions  

---

## Executive Summary

**OnlineBookStoreManagement** is a modern, high-performance, enterprise-grade e-commerce web application engineered with **ASP.NET Core 10 MVC** and **Entity Framework Core 10**. The platform provides full-lifecycle book catalog management, category management, dynamic user reviews, shopping cart processing, claim/role-based security management, and automated continuous integration and continuous deployment (CI/CD) to Microsoft Azure.

---

## System Architecture

```mermaid
graph TD
    Client[Browser / Client App] -->|HTTPS / Port 443| Cloud[Azure Web App: app-onlinebookstoremanagement]
    
    subgraph Azure App Service Environment
        Cloud --> MVC[ASP.NET Core 10 MVC Controller Layer]
        MVC --> Identity[ASP.NET Core Identity & Claims Middleware]
        MVC --> EF[Entity Framework Core 10 ORM]
        EF --> DB[(SQLite Database: bookstore.db)]
    end

    subgraph CI/CD Automated Pipeline
        GitHub[GitHub Repository: kirankumar1-programming] -->|Push to main| GHA[GitHub Actions Runner]
        GHA -->|Build & Publish net10.0| ReleaseArtifact[Release Package]
        ReleaseArtifact -->|Azure WebApp Deploy API| Cloud
    end
```

---

## 1. Technology Stack & Key Dependencies

| Architectural Layer | Technology / Package | Description / Role |
| :--- | :--- | :--- |
| **Runtime & Framework** | .NET 10.0 (`net10.0`) | Modern, high-performance C# web runtime |
| **Presentation Layer** | ASP.NET Core MVC (Razor Views) | Server-rendered HTML with modern Glassmorphism UI |
| **ORM / Data Access** | Entity Framework Core 10.0 | Object-Relational Mapper for C# entity queries |
| **Database Engine** | SQLite (`Microsoft.EntityFrameworkCore.Sqlite`) | Lightweight, zero-config relational SQL engine |
| **Identity & Access** | ASP.NET Core Identity | Membership system for passwords, claims, & roles |
| **Hosting Platform** | Azure App Service (Linux / Windows) | Managed PaaS hosting for ASP.NET Web Applications |
| **CI/CD Automation** | GitHub Actions | Automated build, test, and release orchestration |

---

## 2. Domain Data Model & Entity Relations

```mermaid
erDiagram
    APPLICATION_USER ||--o{ BOOK_REVIEW : writes
    APPLICATION_USER ||--o{ CART_ITEM : owns
    CATEGORY ||--o{ BOOK : contains
    BOOK ||--o{ BOOK_REVIEW : receives
    BOOK ||--o{ CART_ITEM : added_to

    APPLICATION_USER {
        string Id PK
        string FullName
        string Address
        string City
        string PostalCode
        DateTime CreatedAt
    }

    CATEGORY {
        int Id PK
        string Name
        string Description
    }

    BOOK {
        int Id PK
        string Title
        string Author
        decimal Price
        int StockQuantity
        string CoverImageUrl
        int CategoryId FK
    }

    BOOK_REVIEW {
        int Id PK
        int Rating
        string Comment
        DateTime CreatedAt
        int BookId FK
        string UserId FK
    }
```

---

## 3. Security & Access Control Architecture

The platform implements multi-layered authentication and robust authorization:

```mermaid
flowchart LR
    Request[Incoming Request] --> AuthCheck{Is User Authenticated?}
    AuthCheck -- No --> RedirectLogin[Redirect to /Account/Login]
    AuthCheck -- Yes --> RoleCheck{Check Admin Claims / Roles}
    RoleCheck -- Admin Claims / Role --> AllowAdmin[Grant Access to /Admin and /Books CRUD]
    RoleCheck -- Standard Customer --> DenyAdmin[Return 403 Access Denied]
```

### Key Security Implementations:
1. **Claim-Based Authorization**: `CheckAdminAccessAsync()` checks Identity roles, claim types, and trusted administrative email domains (`admin@bookstore.com`).
2. **Password Hashing**: Identity uses PBKDF2 with HMAC-SHA256 and dynamic salt.
3. **CSRF Protection**: Form submissions use `@Html.AntiForgeryToken()` and `[ValidateAntiForgeryToken]` attributes.
4. **Publish Profile Encryption**: Deployment to Azure relies on encrypted Publish Profile secrets (`AZURE_WEBAPP_PUBLISH_PROFILE`) with SCM Basic Auth strictly guarded.

---

## 4. Continuous Integration & Deployment (CI/CD)

The project leverages automated **GitHub Actions** workflows triggered on code pushes to the `main` branch.

### CI/CD Pipeline Workflow Diagram:

```mermaid
sequenceDiagram
    autonumber
    actor Developer
    participant GitHub as GitHub Repo (main)
    participant Actions as GitHub Actions Runner
    participant Azure as Azure App Service

    Developer->>GitHub: git push origin main
    GitHub->>Actions: Trigger workflow (.github/workflows/deploy.yml)
    Actions->>Actions: 1. Checkout repository code
    Actions->>Actions: 2. Setup .NET 10.x SDK
    Actions->>Actions: 3. Run `dotnet restore`
    Actions->>Actions: 4. Run `dotnet build -c Release`
    Actions->>Actions: 5. Run `dotnet publish -c Release`
    Actions->>Azure: 6. Deploy via azure/webapps-deploy@v3 (Publish Profile)
    Azure-->>Actions: Deployment Success HTTP 200
    Actions-->>Developer: Green Checkmark ✅ Live at app-onlinebookstoremanagement.azurewebsites.net
```

### GitHub Actions Workflow Specification:
```yaml
name: Build and Deploy ASP.NET Core App to Azure Web App

on:
  push:
    branches:
      - main
  workflow_dispatch:

env:
  AZURE_WEBAPP_NAME: 'app-onlinebookstoremanagement'
  DOTNET_VERSION: '10.x'

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout Repository
        uses: actions/checkout@v4

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore Dependencies
        run: dotnet restore

      - name: Build Project
        run: dotnet build --configuration Release --no-restore

      - name: Publish Web App
        run: dotnet publish --configuration Release --output ./publish

      - name: Deploy to Azure Web App
        uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ env.AZURE_WEBAPP_NAME }}
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
          package: ./publish
```

---

## 5. Deployment & Configuration Management

### Azure Infrastructure Setup
- **Web App Name**: `app-onlinebookstoremanagement`
- **Resource Group**: `app-onlinebookstoremanagement_group`
- **Connection String**: `Data Source=/home/site/wwwroot/bookstore.db` (Type: `Custom`)
- **Live Production URL**: [https://app-onlinebookstoremanagement.azurewebsites.net](https://app-onlinebookstoremanagement.azurewebsites.net)

---

## 6. Verification & Quality Assurance

> [!TIP]
> All build artifacts, EF Core database migrations, and Razor views are validated locally using `.NET 10 CLI` before pushing to production:

- **Local Build Verification**: `dotnet build` returns `0 Errors`.
- **Local Runtime Test**: `dotnet run` executes cleanly on `http://localhost:5059`.
- **Image Fallbacks**: SVG fallback handlers (`onerror="this.src='/images/default-book.svg'"`) ensure visual integrity even when external cover URLs fail.
