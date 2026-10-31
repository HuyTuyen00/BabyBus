using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CafeManagement.Database
{
    public static class DbHelper
    {
        private static string? _connectionString;

        public static string ConnectionString
        {
            get
            {
                if (_connectionString == null)
                {
                    _connectionString = ReadConnectionString();
                }
                return _connectionString;
            }
        }

        public static string ReadConnectionString()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
            {
                // Create default config file if it does not exist
                var defaultConfig = new
                {
                    ConnectionStrings = new
                    {
                        DefaultConnection = "Server=(localdb)\\mssqllocaldb;Database=CafeManagement;Integrated Security=True;TrustServerCertificate=True;"
                    }
                };
                try
                {
                    string json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, json);
                }
                catch { }
            }

            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings) &&
                        connStrings.TryGetProperty("DefaultConnection", out var defConn))
                    {
                        return defConn.GetString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi đọc appsettings.json: {ex.Message}\n");
            }

            return "Server=(localdb)\\mssqllocaldb;Database=CafeManagement;Integrated Security=True;TrustServerCertificate=True;";
        }

        public static void SaveConnectionString(string connStr)
        {
            _connectionString = connStr;
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            try
            {
                var configObj = new
                {
                    ConnectionStrings = new
                    {
                        DefaultConnection = connStr
                    }
                };
                string json = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi ghi appsettings.json: {ex.Message}\n");
            }
        }

        public static void ParseConnectionString(string connString, out string server, out string database, out bool integratedSecurity, out string userId, out string password)
        {
            server = "(localdb)\\mssqllocaldb";
            database = "CafeManagement";
            integratedSecurity = true;
            userId = "";
            password = "";

            try
            {
                var builder = new SqlConnectionStringBuilder(connString);
                server = builder.DataSource;
                database = string.IsNullOrEmpty(builder.InitialCatalog) ? "CafeManagement" : builder.InitialCatalog;
                integratedSecurity = builder.IntegratedSecurity;
                userId = builder.UserID;
                password = builder.Password;
            }
            catch { }
        }

        public static string BuildConnectionString(string server, string database, bool integratedSecurity, string userId, string password)
        {
            string serverName = server?.Trim() ?? "";
            if (!serverName.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            {
                if (serverName.StartsWith("192.168.") || serverName.StartsWith("10.") || serverName.StartsWith("172.") || System.Net.IPAddress.TryParse(serverName.Split(',')[0], out _))
                {
                    if (!serverName.Contains(","))
                        serverName = $"tcp:{serverName},1433";
                    else
                        serverName = $"tcp:{serverName}";
                }
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = string.IsNullOrWhiteSpace(database) ? "CafeManagement" : database.Trim(),
                IntegratedSecurity = integratedSecurity,
                TrustServerCertificate = true,
                Encrypt = false
            };

            if (!integratedSecurity)
            {
                builder.UserID = userId;
                builder.Password = password;
            }

            return builder.ConnectionString;
        }

        public static bool TestConnection(string connString, out string errorMessage)
        {
            try
            {
                using var conn = new SqlConnection(connString);
                conn.Open();
                errorMessage = "Kết nối SQL thành công!";
                return true;
            }
            catch
            {
                // Try testing master connection if database doesn't exist yet
                try
                {
                    string masterConn = GetMasterConnectionString(connString);
                    using var connMaster = new SqlConnection(masterConn);
                    connMaster.Open();
                    errorMessage = $"Kết nối Server thành công! CSDL '{GetDatabaseName(connString)}' chưa tồn tại nhưng sẽ được tự động tạo khi lưu.";
                    return true;
                }
                catch (Exception exMaster)
                {
                    errorMessage = $"Lỗi kết nối SQL Server: {exMaster.Message}";
                    return false;
                }
            }
        }

        public static string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") || ipStr.StartsWith("172."))
                        {
                            return ipStr;
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        private static string GetDatabaseName(string connString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connString);
                return builder.InitialCatalog;
            }
            catch
            {
                return "CafeManagement";
            }
        }

        private static string GetMasterConnectionString(string connString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connString)
                {
                    InitialCatalog = "master"
                };
                return builder.ConnectionString;
            }
            catch
            {
                return "Server=(localdb)\\mssqllocaldb;Database=master;Integrated Security=True;TrustServerCertificate=True;";
            }
        }

        public static void ResetAndReinitializeDatabase()
        {
            try
            {
                using (var conn = new SqlConnection(ConnectionString))
                {
                    conn.Open();
                    using var trans = conn.BeginTransaction();
                    try
                    {
                        using var cmdClean = new SqlCommand(@"
                            DELETE FROM KitchenQueue;
                            DELETE FROM OrderDetails;
                            DELETE FROM Orders;
                            DELETE FROM Products;
                            DELETE FROM Categories;
                            DELETE FROM Tables;
                            DBCC CHECKIDENT ('KitchenQueue', RESEED, 0);
                            DBCC CHECKIDENT ('OrderDetails', RESEED, 0);
                            DBCC CHECKIDENT ('Orders', RESEED, 0);
                            DBCC CHECKIDENT ('Products', RESEED, 0);
                            DBCC CHECKIDENT ('Categories', RESEED, 0);
                            DBCC CHECKIDENT ('Tables', RESEED, 0);", conn, trans);
                        cmdClean.ExecuteNonQuery();
                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }

                // Re-initialize tables and seed clean data (30 empty tables + clean Excel menu)
                InitializeDatabase();
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi khi khởi tạo lại CSDL: {ex.Message}");
            }
        }

        public static void InitializeDatabase()
        {
            try
            {
                // 1. Ensure the database exists on SQL Server
                string dbName = GetDatabaseName(ConnectionString);
                string masterConnStr = GetMasterConnectionString(ConnectionString);

                using (var masterConn = new SqlConnection(masterConnStr))
                {
                    masterConn.Open();
                    string checkDbSql = $"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{dbName}') CREATE DATABASE [{dbName}]";
                    using (var cmd = new SqlCommand(checkDbSql, masterConn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                // 2. Initialize tables on target database
                using (var conn = new SqlConnection(ConnectionString))
                {
                    conn.Open();

                    // Categories table
                    string createCategoriesSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Categories]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE Categories (
                                Id   BIGINT IDENTITY(1,1) PRIMARY KEY,
                                Name NVARCHAR(255) NOT NULL UNIQUE
                            );
                        END";
                    using (var cmd = new SqlCommand(createCategoriesSql, conn))
                        cmd.ExecuteNonQuery();

                    // Products table
                    string createProductsSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE Products (
                                Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
                                Name                 NVARCHAR(255) NOT NULL UNIQUE,
                                Price                FLOAT NOT NULL CHECK (Price >= 0),
                                Unit                 NVARCHAR(50) NOT NULL,
                                CategoryId           BIGINT NOT NULL REFERENCES Categories(Id),
                                ImagePath            NVARCHAR(500) NOT NULL DEFAULT '',
                                IsExcludeFromKitchen BIT NOT NULL DEFAULT 0
                            );
                        END";
                    using (var cmd = new SqlCommand(createProductsSql, conn))
                        cmd.ExecuteNonQuery();

                    // Auto-migration: Ensure IsExcludeFromKitchen column exists in Products
                    string checkProductsIsExcludeSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND name = 'IsExcludeFromKitchen')
                        BEGIN
                            ALTER TABLE Products ADD IsExcludeFromKitchen BIT NOT NULL DEFAULT 0;
                        END";
                    using (var cmd = new SqlCommand(checkProductsIsExcludeSql, conn))
                        cmd.ExecuteNonQuery();

                    // Dining Tables table
                    string createTablesSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Tables]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE Tables (
                                Id        BIGINT IDENTITY(1,1) PRIMARY KEY,
                                Name      NVARCHAR(255) NOT NULL UNIQUE,
                                Status    NVARCHAR(50) NOT NULL DEFAULT 'Empty',
                                SortOrder INT NOT NULL DEFAULT 0
                            );
                        END";
                    using (var cmd = new SqlCommand(createTablesSql, conn))
                        cmd.ExecuteNonQuery();

                    // Orders table
                    string createOrdersSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE Orders (
                                Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
                                OrderDate    NVARCHAR(50) NOT NULL,
                                TableId      BIGINT NULL REFERENCES Tables(Id),
                                TotalAmount  FLOAT NOT NULL CHECK (TotalAmount >= 0),
                                Status       NVARCHAR(50) NOT NULL,
                                IsPrinted    BIT NOT NULL DEFAULT 0
                            );
                        END";
                    using (var cmd = new SqlCommand(createOrdersSql, conn))
                        cmd.ExecuteNonQuery();

                    // OrderDetails table
                    string createOrderDetailsSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[OrderDetails]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE OrderDetails (
                                Id        BIGINT IDENTITY(1,1) PRIMARY KEY,
                                OrderId   BIGINT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
                                ProductId BIGINT NOT NULL REFERENCES Products(Id),
                                Quantity  INT NOT NULL CHECK (Quantity > 0),
                                Price     FLOAT NOT NULL CHECK (Price >= 0),
                                Note      NVARCHAR(1000) NOT NULL DEFAULT ''
                            );
                        END";
                    using (var cmd = new SqlCommand(createOrderDetailsSql, conn))
                        cmd.ExecuteNonQuery();

                    // KitchenQueue table
                    string createKitchenQueueSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[KitchenQueue]') AND type in (N'U'))
                        BEGIN
                            CREATE TABLE KitchenQueue (
                                Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
                                OrderId       BIGINT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
                                OrderDetailId     BIGINT NULL REFERENCES OrderDetails(Id),
                                ProductId         BIGINT NOT NULL REFERENCES Products(Id),
                                Quantity          INT NOT NULL CHECK (Quantity > 0),
                                CompletedQuantity INT NOT NULL DEFAULT 0,
                                PrintedQuantity   INT NOT NULL DEFAULT 0,
                                Note              NVARCHAR(1000) NOT NULL DEFAULT '',
                                Status            NVARCHAR(50) NOT NULL,
                                CreatedAt         NVARCHAR(50) NOT NULL
                            );
                        END";
                    using (var cmd = new SqlCommand(createKitchenQueueSql, conn))
                        cmd.ExecuteNonQuery();

                    // ===== FULL AUTO-MIGRATIONS FOR ALL EXISTING DATABASES =====
                    string autoMigrationsSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND name = 'IsExcludeFromKitchen')
                            ALTER TABLE Products ADD IsExcludeFromKitchen BIT NOT NULL DEFAULT 0;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND name = 'IsPrinted')
                            ALTER TABLE Orders ADD IsPrinted BIT NOT NULL DEFAULT 0;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tables]') AND name = 'SortOrder')
                            ALTER TABLE Tables ADD SortOrder INT NOT NULL DEFAULT 0;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[KitchenQueue]') AND name = 'OrderDetailId')
                            ALTER TABLE KitchenQueue ADD OrderDetailId BIGINT NULL;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[KitchenQueue]') AND name = 'CompletedQuantity')
                            ALTER TABLE KitchenQueue ADD CompletedQuantity INT NOT NULL DEFAULT 0;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[KitchenQueue]') AND name = 'PrintedQuantity')
                            ALTER TABLE KitchenQueue ADD PrintedQuantity INT NOT NULL DEFAULT 0;

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OrderDetails]') AND name = 'Note')
                            ALTER TABLE OrderDetails ADD Note NVARCHAR(1000) NOT NULL DEFAULT '';

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[KitchenQueue]') AND name = 'Note')
                            ALTER TABLE KitchenQueue ADD Note NVARCHAR(1000) NOT NULL DEFAULT '';

                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OrderDetails]') AND name = 'IsCancelled')
                            ALTER TABLE OrderDetails ADD IsCancelled BIT NOT NULL DEFAULT 0;

                        -- Auto-cleanup any leading numbers and dot prefix in Product & Category names
                        UPDATE Products 
                        SET Name = LTRIM(RTRIM(SUBSTRING(Name, CHARINDEX('.', Name) + 1, LEN(Name))))
                        WHERE Name LIKE '[0-9]%.%' OR Name LIKE '[0-9]-%';

                        UPDATE Categories 
                        SET Name = LTRIM(RTRIM(SUBSTRING(Name, CHARINDEX('.', Name) + 1, LEN(Name))))
                        WHERE Name LIKE '[0-9]%.%' OR Name LIKE '[0-9]-%';

                        -- Auto-sync table status to Empty if no active Pending orders
                        UPDATE Tables 
                        SET Status = 'Empty' 
                        WHERE Id NOT IN (SELECT TableId FROM Orders WHERE Status = 'Pending' AND TableId IS NOT NULL);

                        -- Performance indexes for fast lookups during peak hours
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_TableId_Status' AND object_id = OBJECT_ID('Orders'))
                            CREATE NONCLUSTERED INDEX IX_Orders_TableId_Status ON Orders (TableId, Status) INCLUDE (Id, TotalAmount, IsPrinted, OrderDate);

                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderDetails_OrderId' AND object_id = OBJECT_ID('OrderDetails'))
                            CREATE NONCLUSTERED INDEX IX_OrderDetails_OrderId ON OrderDetails (OrderId) INCLUDE (ProductId, Quantity, Price, Note, IsCancelled);

                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_KitchenQueue_OrderDetailId' AND object_id = OBJECT_ID('KitchenQueue'))
                            CREATE NONCLUSTERED INDEX IX_KitchenQueue_OrderDetailId ON KitchenQueue (OrderDetailId) INCLUDE (Status, Quantity, CompletedQuantity);

                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_KitchenQueue_OrderId_ProductId' AND object_id = OBJECT_ID('KitchenQueue'))
                            CREATE NONCLUSTERED INDEX IX_KitchenQueue_OrderId_ProductId ON KitchenQueue (OrderId, ProductId) INCLUDE (Status);
                    ";
                    using (var cmd = new SqlCommand(autoMigrationsSql, conn))
                        cmd.ExecuteNonQuery();

                    SeedDefaultData(conn);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n\n");
                throw;
            }
        }

        private static void SeedDefaultData(SqlConnection conn)
        {
            int tableCount = 0;
            int catCount = 0;
            int productCount = 0;

            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM Tables", conn))
                tableCount = Convert.ToInt32(cmd.ExecuteScalar());
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM Categories", conn))
                catCount = Convert.ToInt32(cmd.ExecuteScalar());
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM Products", conn))
                productCount = Convert.ToInt32(cmd.ExecuteScalar());

            // 1. Seed tables if empty
            if (tableCount == 0)
            {
                for (int i = 1; i <= 30; i++)
                {
                    using var cmd = new SqlCommand(
                        "INSERT INTO Tables (Name, Status, SortOrder) VALUES (@n, 'Empty', @s)", conn);
                    cmd.Parameters.AddWithValue("@n", $"Bàn {i}");
                    cmd.Parameters.AddWithValue("@s", i);
                    cmd.ExecuteNonQuery();
                }
            }

            // 2. Check for default_menu.xlsx (KiotViet export)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string excelFile = Path.Combine(baseDir, "default_menu.xlsx");
            if (!File.Exists(excelFile))
            {
                excelFile = Path.Combine(baseDir, "wwwroot", "default_menu.xlsx");
            }
            if (!File.Exists(excelFile))
            {
                excelFile = @"C:\Users\admin\Downloads\DanhSachSanPham_KV17082026-131311-691.xlsx";
            }

            // If empty or has <= 10 demo items and Excel file exists -> Auto import real BabyBus menu!
            if (File.Exists(excelFile) && (catCount == 0 || productCount <= 10))
            {
                try
                {
                    var items = CafeManagement.Utilities.ExcelMenuImporter.ReadKiotVietExcel(excelFile);
                    if (items.Count > 0)
                    {
                        CafeManagement.Utilities.ExcelMenuImporter.ImportToDatabase(items, clearExisting: (productCount <= 10));
                        return;
                    }
                }
                catch { }
            }

            // 3. Fallback Seed if still empty
            if (catCount == 0 || productCount == 0)
            {
                // Clear any existing partial data first to avoid foreign key or unique constraint violations
                using (var cmd = new SqlCommand("DELETE FROM Products; DELETE FROM Categories;", conn))
                    cmd.ExecuteNonQuery();

                // Seed categories
                string[] cats = { "Cà phê", "Trà sữa & Trà trái cây", "Đồ ăn nhanh", "Bánh ngọt" };
                foreach (var cat in cats)
                {
                    using (var cmd = new SqlCommand(
                        "INSERT INTO Categories (Name) VALUES (@n)", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", cat);
                        cmd.ExecuteNonQuery();
                    }
                }

                int coffeeId = 0, teaId = 0, foodId = 0, cakeId = 0;
                using (var cmd = new SqlCommand("SELECT Id, Name FROM Categories", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = Convert.ToInt32(r.GetValue(0));
                        string name = r.GetString(1);
                        if (name == "Cà phê") coffeeId = id;
                        else if (name == "Trà sữa & Trà trái cây") teaId = id;
                        else if (name == "Đồ ăn nhanh") foodId = id;
                        else if (name == "Bánh ngọt") cakeId = id;
                    }
                }

                var products = new (string name, decimal price, string unit, int cat)[]
                {
                    ("Cà phê Đen Đá",      25000, "Ly",  coffeeId),
                    ("Cà phê Sữa Đá",      29000, "Ly",  coffeeId),
                    ("Bạc Xỉu",            32000, "Ly",  coffeeId),
                    ("Trà Sữa Trân Châu",  39000, "Ly",  teaId),
                    ("Trà Đào Cam Sả",     45000, "Ly",  teaId),
                    ("Mì Ý Bò Băm",        55000, "Dĩa", foodId),
                    ("Bánh Mì Kẹp Thịt",   25000, "Ổ",   foodId),
                    ("Khoai Tây Chiên",    30000, "Dĩa", foodId),
                    ("Bánh Tiramisu",      35000, "Cái", cakeId),
                    ("Bánh Croissant",     28000, "Cái", cakeId),
                };

                foreach (var p in products)
                {
                    using var cmd = new SqlCommand(
                        "INSERT INTO Products (Name, Price, Unit, CategoryId, ImagePath) VALUES (@n,@p,@u,@c,'')",
                        conn);
                    cmd.Parameters.AddWithValue("@n", p.name);
                    cmd.Parameters.AddWithValue("@p", p.price);
                    cmd.Parameters.AddWithValue("@u", p.unit);
                    cmd.Parameters.AddWithValue("@c", p.cat);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
