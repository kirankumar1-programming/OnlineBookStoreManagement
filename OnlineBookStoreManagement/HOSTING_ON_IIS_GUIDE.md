# Step-by-Step Guide: Hosting OnlineBookStoreManagement on IIS

---

## Overview
This document provides complete, step-by-step instructions to host the **OnlineBookStoreManagement** ASP.NET Core 10 application on **IIS (Internet Information Services)** on a Windows Server or Windows PC.

---

## Step 1: Install IIS & Web Server Features

1. Press **Win + R**, type `optionalfeatures`, and press **Enter**.
2. Expand **Internet Information Services**:
   - Check **Web Management Tools** $\rightarrow$ **IIS Management Console**.
   - Expand **World Wide Web Services** $\rightarrow$ **Application Development Features**:
     - Check **.NET Extensibility 4.8**
     - Check **ASP.NET 4.8**
     - Check **WebSocket Protocol**
3. Click **OK** and let Windows install the required features.

---

## Step 2: Install ASP.NET Core Hosting Bundle (.NET 10)

> [!IMPORTANT]
> IIS requires the **ASP.NET Core Module v2 (`AspNetCoreModuleV2`)** to run .NET applications.

1. Download the **ASP.NET Core 10.0 Hosting Bundle** from official Microsoft .NET Download page.
2. Run the installer (`dotnet-hosting-10.x-win.exe`).
3. Open PowerShell as Administrator and restart IIS to register the new module:
   ```powershell
   iisreset
   ```

---

## Step 3: Publish the ASP.NET Core Application

1. Open PowerShell / Command Prompt in your project directory (`OnlineBookStoreManagement`).
2. Run the release publish command:
   ```powershell
   dotnet publish -c Release -o C:\inetpub\wwwroot\OnlineBookStore
   ```
3. Verify that `C:\inetpub\wwwroot\OnlineBookStore` contains `OnlineBookStoreManagement.dll` and `web.config`.

---

## Step 4: Configure IIS Application Pool & Web Site

1. Press **Win + R**, type `inetmgr`, and press **Enter** to open **IIS Manager**.
2. **Create Application Pool**:
   - In the left tree, right-click **Application Pools** $\rightarrow$ Click **Add Application Pool...**.
   - **Name**: `BookStoreAppPool`
   - **.NET CLR version**: Select **No Managed Code** *(ASP.NET Core handles runtime out-of-process/in-process via `AspNetCoreModuleV2`)*.
   - **Managed pipeline mode**: `Integrated`
   - Click **OK**.

3. **Create IIS Web Site**:
   - In the left tree, right-click **Sites** $\rightarrow$ Click **Add Website...**.
   - **Site name**: `OnlineBookStore`
   - **Application pool**: Select `BookStoreAppPool`
   - **Physical path**: `C:\inetpub\wwwroot\OnlineBookStore`
   - **Binding**: Type `http`, Port `80` (or `8080`), Host name: *(leave blank for localhost or enter your domain)*.
   - Click **OK**.

---

## Step 5: Configure Folder & Database Permissions

Because SQLite (`bookstore.db`) requires write access to the published directory:

1. Open File Explorer and navigate to `C:\inetpub\wwwroot\OnlineBookStore`.
2. Right-click the `OnlineBookStore` folder $\rightarrow$ Select **Properties** $\rightarrow$ **Security** tab.
3. Click **Edit...** $\rightarrow$ Click **Add...**.
4. Type `IIS AppPool\BookStoreAppPool` and click **Check Names** $\rightarrow$ Click **OK**.
5. Grant **Modify** and **Write** permissions to `IIS AppPool\BookStoreAppPool`.
6. Also ensure group `IIS_IUSRS` has **Read & execute**, **List folder contents**, and **Read** permissions.
7. Click **Apply** and **OK**.

---

## Step 6: Verify `web.config`

The `web.config` file inside `C:\inetpub\wwwroot\OnlineBookStore` should look like this:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\OnlineBookStoreManagement.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

---

## Step 7: Test Your Application

1. Open your browser and navigate to:  
   👉 **`http://localhost:8080`** (or `http://localhost`)

2. Test features:
   - Login as Admin (`admin@bookstore.com` / `Admin@123`).
   - Browse catalog, view book details, add to cart, write reviews.

---

## Troubleshooting Common IIS Errors

| Error Code / Symptom | Possible Cause | Fix Action |
| :--- | :--- | :--- |
| **HTTP Error 500.19** | ASP.NET Core Hosting Bundle missing or invalid `web.config` | Re-install ASP.NET Core Hosting Bundle and run `iisreset`. |
| **HTTP Error 500.30 - ANCM In-Process Start Failure** | Missing dependencies or app crash on startup | Set `stdoutLogEnabled="true"` in `web.config` and check `logs/stdout`. |
| **SQLite Error: Attempt to write a read-only database** | Permission denied on `bookstore.db` | Grant **Modify / Write** permissions on publish folder to `IIS AppPool\BookStoreAppPool`. |
