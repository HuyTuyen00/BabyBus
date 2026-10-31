using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;
using CafeManagement.Models;

namespace CafeManagement.WebServer
{
    public static class KitchenWebServer
    {
        private static WebApplication? _app;
        private static string _localIp = "127.0.0.1";
        private const int Port = 5000;

        public static string LocalIp => _localIp;
        public static string ServerUrl => $"http://{_localIp}:{Port}";

        public static event Action? KitchenStatusChanged;

        public static void RaiseKitchenStatusChangedLocally()
        {
            App.Current?.Dispatcher.Invoke(() =>
            {
                KitchenStatusChanged?.Invoke();
            });
        }

        public static void NotifyKitchenStatusChanged()
        {
            RaiseKitchenStatusChangedLocally();
            _ = SseManager.BroadcastAsync("update", "status_changed");
        }

        public static void Start()
        {
            _localIp = GetLocalIPAddress();
            EnsureStaticFilesExist();

            var builder = WebApplication.CreateBuilder();

            // Set up Kestrel to listen on all interfaces
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, Port);
            });

            // Add CORS
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            // Configure JSON to accept camelCase from mobile clients
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

            _app = builder.Build();

            _app.UseCors();
            _app.UseDefaultFiles();
            _app.UseStaticFiles();

            // SSE Endpoint
            _app.MapGet("/api/kitchen/events", async (HttpContext httpContext, CancellationToken ct) =>
            {
                httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
                httpContext.Response.Headers.Append("Cache-Control", "no-cache");
                httpContext.Response.Headers.Append("Connection", "keep-alive");

                var client = new SseClient(httpContext.Response);
                SseManager.AddClient(client);

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(5000, ct);
                        await client.SendEventAsync("ping", "keep-alive");
                    }
                }
                catch
                {
                    // Client disconnected
                }
            });

            // Get Pending Kitchen Items
            _app.MapGet("/api/kitchen/pending", () =>
            {
                var items = GetPendingItemsFromDb();
                return Results.Ok(items);
            });

            // Complete Kitchen Item
            _app.MapPost("/api/kitchen/complete/{id:int}", async (int id) =>
            {
                bool success = CompleteItemInDb(id);
                if (success)
                {
                    // Notify WPF
                    App.Current?.Dispatcher.Invoke(() =>
                    {
                        KitchenStatusChanged?.Invoke();
                    });

                    // Notify other mobile/web clients
                    await SseManager.BroadcastAsync("update", "completed");
                    return Results.Ok(new { success = true });
                }
                return Results.BadRequest(new { success = false, message = "Could not update item status." });
            });

            // ── Mobile Order App API Endpoints ──
            _app.MapGet("/api/mobile/tables", () => Results.Ok(GetMobileTablesFromDb()));
            _app.MapGet("/api/mobile/categories", () => Results.Ok(GetMobileCategoriesFromDb()));
            _app.MapGet("/api/mobile/products", () => Results.Ok(GetMobileProductsFromDb()));
            _app.MapGet("/api/mobile/cart/{tableId:int}", (int tableId) => Results.Ok(GetMobileCartFromDb(tableId)));

            _app.MapPost("/api/mobile/order", async (MobileOrderRequest req) =>
            {
                if (req == null || req.TableId <= 0 || req.Items == null || !req.Items.Any())
                {
                    return Results.BadRequest(new { success = false, message = "Đơn hàng không hợp lệ." });
                }

                bool success = SaveMobileOrderToDb(req);
                if (success)
                {
                    App.Current?.Dispatcher.Invoke(() =>
                    {
                        KitchenStatusChanged?.Invoke();
                    });
                    await SseManager.BroadcastAsync("update", "new_order");
                    return Results.Ok(new { success = true });
                }
                return Results.BadRequest(new { success = false, message = "Lỗi lưu đơn hàng." });
            });

            // Update Quantity for an existing OrderDetail
            _app.MapPost("/api/mobile/update-quantity", (MobileUpdateQuantityRequest req) =>
            {
                if (req == null || req.OrderDetailId <= 0)
                {
                    return Results.BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
                }
                bool success = UpdateOrderDetailQuantityInDb(req.OrderDetailId, req.Quantity);
                if (success)
                {
                    NotifyKitchenStatusChanged();
                    return Results.Ok(new { success = true });
                }
                return Results.BadRequest(new { success = false, message = "Lỗi cập nhật số lượng." });
            });

            // Delete an existing OrderDetail
            _app.MapPost("/api/mobile/delete-item", (MobileDeleteItemRequest req) =>
            {
                if (req == null || req.OrderDetailId <= 0)
                {
                    return Results.BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
                }
                bool success = UpdateOrderDetailQuantityInDb(req.OrderDetailId, 0);
                if (success)
                {
                    NotifyKitchenStatusChanged();
                    return Results.Ok(new { success = true });
                }
                return Results.BadRequest(new { success = false, message = "Lỗi xóa món." });
            });

            // Run in background
            Task.Run(() => _app.Run());
        }

        public static void Stop()
        {
            _app?.StopAsync().Wait();
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        string desc = ni.Description.ToLower();
                        string name = ni.Name.ToLower();

                        // Skip virtual adapters commonly used by Hyper-V, WSL, Docker, VMware, VirtualBox
                        if (desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("wsl") || 
                            desc.Contains("docker") || desc.Contains("vmware") || desc.Contains("virtualbox") || 
                            desc.Contains("vbox") || name.Contains("vEthernet"))
                        {
                            continue;
                        }

                        var ipProps = ni.GetIPProperties();
                        // Prefer active adapters that have a default gateway configured
                        if (ipProps.GatewayAddresses.Count == 0)
                        {
                            continue;
                        }

                        foreach (var ip in ipProps.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to DNS if NetworkInterface fails
            }

            // Fallback DNS logic
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
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

        private static List<KitchenItem> GetPendingItemsFromDb()
        {
            var list = new List<KitchenItem>();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT k.Id, k.OrderId, k.ProductId, k.Quantity, 
                           ISNULL(k.CompletedQuantity, 0) AS CompletedQuantity,
                           ISNULL(k.PrintedQuantity, 0) AS PrintedQuantity,
                           k.Note, k.Status, k.CreatedAt, 
                           p.Name AS ProductName, ISNULL(t.Name, 'Mang đi') AS TableNumber 
                    FROM KitchenQueue k 
                    JOIN Products p ON k.ProductId = p.Id 
                    JOIN Orders o ON k.OrderId = o.Id 
                    LEFT JOIN Tables t ON o.TableId = t.Id
                    WHERE o.Status = 'Pending'
                    ORDER BY k.CreatedAt ASC";

                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new KitchenItem
                        {
                            Id                = Convert.ToInt32(reader.GetValue(0)),
                            OrderId           = Convert.ToInt32(reader.GetValue(1)),
                            ProductId         = Convert.ToInt32(reader.GetValue(2)),
                            Quantity          = Convert.ToInt32(reader.GetValue(3)),
                            CompletedQuantity = Convert.ToInt32(reader.GetValue(4)),
                            PrintedQuantity   = Convert.ToInt32(reader.GetValue(5)),
                            Note              = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Status            = reader.GetString(7),
                            CreatedAt         = DateTime.Parse(reader.GetString(8)),
                            ProductName       = reader.GetString(9),
                            TableNumber       = reader.GetString(10)
                        });
                    }
                }
            }
            return list;
        }

        private static bool CompleteItemInDb(int id)
        {
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                // 1. Get current status
                string currentStatus = "Pending";
                using (var cmdSelect = new SqlCommand("SELECT Status FROM KitchenQueue WHERE Id = @Id", conn))
                {
                    cmdSelect.Parameters.AddWithValue("@Id", id);
                    var obj = cmdSelect.ExecuteScalar();
                    if (obj != null) currentStatus = obj.ToString()!;
                }

                // 2. Determine next status
                string newStatus = "Completed";
                if (currentStatus == "Pending" || currentStatus == "Processing")
                {
                    newStatus = "Completed";
                }
                else
                {
                    return false; // Already completed or served. Can only be served by printing!
                }

                string sql = "UPDATE KitchenQueue SET Status = @Status WHERE Id = @Id";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    cmd.Parameters.AddWithValue("@Id", id);
                    int rows = cmd.ExecuteNonQuery();
                    return rows > 0;
                }
            }
        }

        private static List<MobileTableDto> GetMobileTablesFromDb()
        {
            var list = new List<MobileTableDto>();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT t.Id, t.Name, t.Status,
                           ISNULL((SELECT SUM(d.Quantity * d.Price) 
                                   FROM Orders o JOIN OrderDetails d ON o.Id = d.OrderId 
                                   WHERE o.TableId = t.Id AND o.Status = 'Pending'), 0) AS UnpaidAmount,
                           ISNULL((SELECT SUM(d.Quantity) 
                                   FROM Orders o JOIN OrderDetails d ON o.Id = d.OrderId 
                                   WHERE o.TableId = t.Id AND o.Status = 'Pending'), 0) AS ItemCount,
                           ISNULL((SELECT TOP 1 CAST(o.IsPrinted AS INT)
                                   FROM Orders o WHERE o.TableId = t.Id AND o.Status = 'Pending'), 0) AS IsPrinted,
                           (SELECT TOP 1 o.OrderDate FROM Orders o WHERE o.TableId = t.Id AND o.Status = 'Pending' ORDER BY o.Id DESC) AS OrderDate
                    FROM Tables t
                    ORDER BY t.SortOrder, t.Name";

                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = Convert.ToInt32(reader.GetValue(0));
                        string name = reader.GetString(1);
                        string statusStr = reader.GetString(2);
                        decimal unpaid = Convert.ToDecimal(reader.GetValue(3));
                        int itemCount = Convert.ToInt32(reader.GetValue(4));
                        bool isPrinted = Convert.ToBoolean(reader.GetValue(5));
                        string? orderDateStr = reader.IsDBNull(6) ? null : reader.GetString(6);

                        bool isOccupied = statusStr.Equals("Occupied", StringComparison.OrdinalIgnoreCase) || unpaid > 0;
                        
                        string timeText = "";
                        if (!string.IsNullOrEmpty(orderDateStr) && DateTime.TryParse(orderDateStr, out DateTime od))
                        {
                            var span = DateTime.Now - od;
                            if (span.TotalHours >= 1)
                                timeText = $"{(int)span.TotalHours}g {span.Minutes}p";
                            else
                                timeText = $"{Math.Max(1, (int)span.TotalMinutes)}p";
                        }

                        list.Add(new MobileTableDto
                        {
                            Id = id,
                            Name = name,
                            Status = isPrinted ? "Printed" : (isOccupied ? "Occupied" : "Empty"),
                            IsOccupied = isOccupied,
                            HasPrintedBill = isPrinted,
                            TotalAmount = unpaid,
                            ItemCount = itemCount,
                            OccupiedTimeText = timeText
                        });
                    }
                }
            }
            return list;
        }

        private static List<MobileCategoryDto> GetMobileCategoriesFromDb()
        {
            var list = new List<MobileCategoryDto>();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand("SELECT Id, Name FROM Categories ORDER BY Name", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new MobileCategoryDto
                    {
                        Id = Convert.ToInt32(reader.GetValue(0)),
                        Name = reader.GetString(1)
                    });
                }
            }
            return list;
        }

        private static List<MobileProductDto> GetMobileProductsFromDb()
        {
            var list = new List<MobileProductDto>();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT p.Id, p.Name, p.Price, p.Unit, p.CategoryId, c.Name AS CategoryName, 
                           ISNULL(p.IsExcludeFromKitchen, 0) AS IsExclude
                    FROM Products p
                    LEFT JOIN Categories c ON p.CategoryId = c.Id
                    ORDER BY p.Name";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new MobileProductDto
                    {
                        Id = Convert.ToInt32(reader.GetValue(0)),
                        Name = reader.GetString(1),
                        Price = Convert.ToDecimal(reader.GetValue(2)),
                        Unit = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        CategoryId = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                        CategoryName = reader.IsDBNull(5) ? "Khác" : reader.GetString(5),
                        IsExcludeFromKitchen = Convert.ToBoolean(reader.GetValue(6))
                    });
                }
            }
            return list;
        }

        private static List<MobileCartItemDto> GetMobileCartFromDb(int tableId)
        {
            var list = new List<MobileCartItemDto>();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT d.Id, d.ProductId, p.Name AS ProductName, d.Quantity, d.Price, ISNULL(d.Note, '') AS Note,
                           CASE 
                               WHEN p.IsExcludeFromKitchen = 1 THEN 'Served' 
                               ELSE ISNULL((SELECT TOP 1 k.Status FROM KitchenQueue k WHERE k.OrderDetailId = d.Id ORDER BY k.Id DESC), 
                                    ISNULL((SELECT TOP 1 kq.Status FROM KitchenQueue kq WHERE kq.OrderId = d.OrderId AND kq.ProductId = d.ProductId ORDER BY kq.Id DESC), 'Pending')) 
                           END AS KitchenStatus
                    FROM Orders o
                    JOIN OrderDetails d ON o.Id = d.OrderId
                    JOIN Products p ON d.ProductId = p.Id
                    WHERE o.TableId = @tableId AND o.Status = 'Pending'
                    ORDER BY d.Id ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@tableId", tableId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new MobileCartItemDto
                    {
                        Id = Convert.ToInt32(reader.GetValue(0)),
                        ProductId = Convert.ToInt32(reader.GetValue(1)),
                        ProductName = reader.GetString(2),
                        Quantity = Convert.ToInt32(reader.GetValue(3)),
                        Price = Convert.ToDecimal(reader.GetValue(4)),
                        Note = reader.GetString(5),
                        KitchenStatus = reader.GetString(6)
                    });
                }
            }
            return list;
        }

        private static bool SaveMobileOrderToDb(MobileOrderRequest req)
        {
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                long orderId = 0;
                bool isNewOrder = true;

                using (var cmdCheck = new SqlCommand(
                    "SELECT TOP 1 Id FROM Orders WHERE TableId = @t AND Status = 'Pending'", conn, trans))
                {
                    cmdCheck.Parameters.AddWithValue("@t", req.TableId);
                    var obj = cmdCheck.ExecuteScalar();
                    if (obj != null)
                    {
                        orderId = Convert.ToInt64(obj);
                        isNewOrder = false;
                    }
                }

                var productPrices = new Dictionary<int, decimal>();
                var excludeList = new HashSet<int>();
                using (var cmdP = new SqlCommand("SELECT Id, Price, ISNULL(IsExcludeFromKitchen,0) FROM Products", conn, trans))
                using (var r = cmdP.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int pid = Convert.ToInt32(r.GetValue(0));
                        decimal pr = Convert.ToDecimal(r.GetValue(1));
                        bool ex = Convert.ToBoolean(r.GetValue(2));
                        productPrices[pid] = pr;
                        if (ex) excludeList.Add(pid);
                    }
                }

                if (isNewOrder)
                {
                    if (req.Items == null || !req.Items.Any())
                    {
                        trans.Rollback();
                        return true;
                    }

                    using var cmdOrder = new SqlCommand(@"
                        INSERT INTO Orders (OrderDate, TableId, TotalAmount, Status)
                        VALUES (@d, @t, 0, 'Pending');
                        SELECT SCOPE_IDENTITY();", conn, trans);
                    cmdOrder.Parameters.AddWithValue("@d", DateTime.Now.ToString("o"));
                    cmdOrder.Parameters.AddWithValue("@t", req.TableId);
                    orderId = Convert.ToInt64(cmdOrder.ExecuteScalar());
                }

                // Get existing OrderDetails in DB for this order
                var existingDetails = new Dictionary<int, int>(); // DetailId -> ProductId
                var existingDetailsByProduct = new Dictionary<int, int>(); // ProductId -> DetailId
                if (!isNewOrder)
                {
                    using var cmdGetD = new SqlCommand("SELECT Id, ProductId FROM OrderDetails WHERE OrderId = @oid", conn, trans);
                    cmdGetD.Parameters.AddWithValue("@oid", orderId);
                    using var rd = cmdGetD.ExecuteReader();
                    while (rd.Read())
                    {
                        int dId = Convert.ToInt32(rd.GetValue(0));
                        int pId = Convert.ToInt32(rd.GetValue(1));
                        existingDetails[dId] = pId;
                        existingDetailsByProduct[pId] = dId;
                    }
                }

                var keptDetailIds = new HashSet<int>();

                if (req.Items != null)
                {
                    foreach (var item in req.Items)
                    {
                        if (item.Quantity <= 0) continue;

                        decimal price = productPrices.ContainsKey(item.ProductId) ? productPrices[item.ProductId] : 0;
                        int detailId = item.Id;

                        if (detailId > 0 && existingDetails.ContainsKey(detailId))
                        {
                            // Update existing detail
                            using var cmdUpd = new SqlCommand(@"
                                UPDATE OrderDetails SET Quantity = @qty, Note = @note, Price = @price WHERE Id = @did", conn, trans);
                            cmdUpd.Parameters.AddWithValue("@qty", item.Quantity);
                            cmdUpd.Parameters.AddWithValue("@note", item.Note ?? "");
                            cmdUpd.Parameters.AddWithValue("@price", (double)price);
                            cmdUpd.Parameters.AddWithValue("@did", detailId);
                            cmdUpd.ExecuteNonQuery();

                            // Update kitchen queue
                            using var cmdUpdK = new SqlCommand(@"
                                UPDATE KitchenQueue 
                                SET Quantity = @qty, 
                                    Note = @note,
                                    CompletedQuantity = CASE WHEN CompletedQuantity > @qty THEN @qty ELSE CompletedQuantity END,
                                    PrintedQuantity = CASE WHEN PrintedQuantity > @qty THEN @qty ELSE PrintedQuantity END,
                                    Status = CASE 
                                        WHEN ISNULL(PrintedQuantity, 0) >= @qty THEN 'Served' 
                                        WHEN ISNULL(CompletedQuantity, 0) > 0 THEN 'Completed' 
                                        ELSE 'Pending' 
                                    END 
                                WHERE OrderDetailId = @did", conn, trans);
                            cmdUpdK.Parameters.AddWithValue("@qty", item.Quantity);
                            cmdUpdK.Parameters.AddWithValue("@note", item.Note ?? "");
                            cmdUpdK.Parameters.AddWithValue("@did", detailId);
                            cmdUpdK.ExecuteNonQuery();

                            keptDetailIds.Add(detailId);
                        }
                        else
                        {
                            // Insert new detail
                            using var cmdIns = new SqlCommand(@"
                                INSERT INTO OrderDetails (OrderId, ProductId, Quantity, Price, Note)
                                VALUES (@oid, @pid, @qty, @price, @note);
                                SELECT SCOPE_IDENTITY();", conn, trans);
                            cmdIns.Parameters.AddWithValue("@oid", orderId);
                            cmdIns.Parameters.AddWithValue("@pid", item.ProductId);
                            cmdIns.Parameters.AddWithValue("@qty", item.Quantity);
                            cmdIns.Parameters.AddWithValue("@price", (double)price);
                            cmdIns.Parameters.AddWithValue("@note", item.Note ?? "");
                            long newDetailId = Convert.ToInt64(cmdIns.ExecuteScalar());

                            keptDetailIds.Add((int)newDetailId);

                            if (!excludeList.Contains(item.ProductId))
                            {
                                using var cmdKitchen = new SqlCommand(@"
                                    INSERT INTO KitchenQueue (OrderId, OrderDetailId, ProductId, Quantity, Note, Status, CreatedAt)
                                    VALUES (@oid, @odid, @pid, @qty, @note, 'Pending', @at)", conn, trans);
                                cmdKitchen.Parameters.AddWithValue("@oid", orderId);
                                cmdKitchen.Parameters.AddWithValue("@odid", newDetailId);
                                cmdKitchen.Parameters.AddWithValue("@pid", item.ProductId);
                                cmdKitchen.Parameters.AddWithValue("@qty", item.Quantity);
                                cmdKitchen.Parameters.AddWithValue("@note", item.Note ?? "");
                                cmdKitchen.Parameters.AddWithValue("@at", DateTime.Now.ToString("o"));
                                cmdKitchen.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // Delete any details that were removed from the cart
                if (!isNewOrder)
                {
                    foreach (var dId in existingDetails.Keys)
                    {
                        if (!keptDetailIds.Contains(dId))
                        {
                            using var cmdDelK = new SqlCommand("DELETE FROM KitchenQueue WHERE OrderDetailId = @did", conn, trans);
                            cmdDelK.Parameters.AddWithValue("@did", dId);
                            cmdDelK.ExecuteNonQuery();

                            using var cmdDelD = new SqlCommand("DELETE FROM OrderDetails WHERE Id = @did", conn, trans);
                            cmdDelD.Parameters.AddWithValue("@did", dId);
                            cmdDelD.ExecuteNonQuery();
                        }
                    }
                }

                // Recalculate total amount from OrderDetails
                using (var cmdRecalc = new SqlCommand(@"
                    SELECT ISNULL(SUM(Quantity * Price), 0), COUNT(1) 
                    FROM OrderDetails WHERE OrderId = @oid", conn, trans))
                {
                    cmdRecalc.Parameters.AddWithValue("@oid", orderId);
                    using var rdc = cmdRecalc.ExecuteReader();
                    if (rdc.Read())
                    {
                        double finalTotal = Convert.ToDouble(rdc.GetValue(0));
                        int detailCount = Convert.ToInt32(rdc.GetValue(1));
                        rdc.Close();

                        if (detailCount == 0)
                        {
                            using var cmdDelOrd = new SqlCommand("DELETE FROM Orders WHERE Id = @oid", conn, trans);
                            cmdDelOrd.Parameters.AddWithValue("@oid", orderId);
                            cmdDelOrd.ExecuteNonQuery();

                            using var cmdEmpty = new SqlCommand("UPDATE Tables SET Status = 'Empty' WHERE Id = @tid", conn, trans);
                            cmdEmpty.Parameters.AddWithValue("@tid", req.TableId);
                            cmdEmpty.ExecuteNonQuery();
                        }
                        else
                        {
                            using var cmdUpdOrd = new SqlCommand("UPDATE Orders SET TotalAmount = @tot WHERE Id = @oid", conn, trans);
                            cmdUpdOrd.Parameters.AddWithValue("@tot", finalTotal);
                            cmdUpdOrd.Parameters.AddWithValue("@oid", orderId);
                            cmdUpdOrd.ExecuteNonQuery();

                            using var cmdOcc = new SqlCommand("UPDATE Tables SET Status = 'Occupied' WHERE Id = @tid", conn, trans);
                            cmdOcc.Parameters.AddWithValue("@tid", req.TableId);
                            cmdOcc.ExecuteNonQuery();
                        }
                    }
                }

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        private static bool UpdateOrderDetailNoteInDb(int orderDetailId, string note)
        {
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                using (var cmd1 = new SqlCommand("UPDATE OrderDetails SET Note = @n WHERE Id = @id", conn, trans))
                {
                    cmd1.Parameters.AddWithValue("@n", note ?? "");
                    cmd1.Parameters.AddWithValue("@id", orderDetailId);
                    cmd1.ExecuteNonQuery();
                }
                using (var cmd2 = new SqlCommand("UPDATE KitchenQueue SET Note = @n WHERE OrderDetailId = @id", conn, trans))
                {
                    cmd2.Parameters.AddWithValue("@n", note ?? "");
                    cmd2.Parameters.AddWithValue("@id", orderDetailId);
                    cmd2.ExecuteNonQuery();
                }
                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        private static bool UpdateOrderDetailQuantityInDb(int orderDetailId, int newQuantity)
        {
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                long orderId = 0;
                int oldQuantity = 0;
                decimal price = 0;
                using (var cmdGet = new SqlCommand("SELECT OrderId, Quantity, Price FROM OrderDetails WHERE Id = @id", conn, trans))
                {
                    cmdGet.Parameters.AddWithValue("@id", orderDetailId);
                    using var reader = cmdGet.ExecuteReader();
                    if (reader.Read())
                    {
                        orderId = Convert.ToInt64(reader.GetValue(0));
                        oldQuantity = Convert.ToInt32(reader.GetValue(1));
                        price = Convert.ToDecimal(reader.GetValue(2));
                    }
                    else
                    {
                        trans.Rollback();
                        return false;
                    }
                }

                if (newQuantity > 0)
                {
                    using (var cmdUpdateDetail = new SqlCommand("UPDATE OrderDetails SET Quantity = @qty WHERE Id = @id", conn, trans))
                    {
                        cmdUpdateDetail.Parameters.AddWithValue("@qty", newQuantity);
                        cmdUpdateDetail.Parameters.AddWithValue("@id", orderDetailId);
                        cmdUpdateDetail.ExecuteNonQuery();
                    }

                    using (var cmdUpdateKitchen = new SqlCommand(@"
                        UPDATE KitchenQueue 
                        SET Quantity = @qty,
                            CompletedQuantity = CASE WHEN CompletedQuantity > @qty THEN @qty ELSE CompletedQuantity END,
                            PrintedQuantity = CASE WHEN PrintedQuantity > @qty THEN @qty ELSE PrintedQuantity END,
                            Status = CASE 
                                WHEN ISNULL(PrintedQuantity, 0) >= @qty THEN 'Served' 
                                WHEN ISNULL(CompletedQuantity, 0) > 0 THEN 'Completed' 
                                ELSE 'Pending' 
                            END 
                        WHERE OrderDetailId = @id", conn, trans))
                    {
                        cmdUpdateKitchen.Parameters.AddWithValue("@qty", newQuantity);
                        cmdUpdateKitchen.Parameters.AddWithValue("@id", orderDetailId);
                        cmdUpdateKitchen.ExecuteNonQuery();
                    }

                    decimal diff = (newQuantity - oldQuantity) * price;
                    using (var cmdUpdateOrder = new SqlCommand("UPDATE Orders SET TotalAmount = TotalAmount + @diff WHERE Id = @oid", conn, trans))
                    {
                        cmdUpdateOrder.Parameters.AddWithValue("@diff", (double)diff);
                        cmdUpdateOrder.Parameters.AddWithValue("@oid", orderId);
                        cmdUpdateOrder.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var cmdDelKitchen = new SqlCommand("DELETE FROM KitchenQueue WHERE OrderDetailId = @id", conn, trans))
                    {
                        cmdDelKitchen.Parameters.AddWithValue("@id", orderDetailId);
                        cmdDelKitchen.ExecuteNonQuery();
                    }

                    using (var cmdDelDetail = new SqlCommand("DELETE FROM OrderDetails WHERE Id = @id", conn, trans))
                    {
                        cmdDelDetail.Parameters.AddWithValue("@id", orderDetailId);
                        cmdDelDetail.ExecuteNonQuery();
                    }

                    decimal diff = -(oldQuantity * price);
                    using (var cmdUpdateOrder = new SqlCommand("UPDATE Orders SET TotalAmount = TotalAmount + @diff WHERE Id = @oid", conn, trans))
                    {
                        cmdUpdateOrder.Parameters.AddWithValue("@diff", (double)diff);
                        cmdUpdateOrder.Parameters.AddWithValue("@oid", orderId);
                        cmdUpdateOrder.ExecuteNonQuery();
                    }

                    using (var cmdCheck = new SqlCommand("SELECT COUNT(1) FROM OrderDetails WHERE OrderId = @oid", conn, trans))
                    {
                        cmdCheck.Parameters.AddWithValue("@oid", orderId);
                        int count = Convert.ToInt32(cmdCheck.ExecuteScalar());
                        if (count == 0)
                        {
                            using (var cmdGetTable = new SqlCommand("SELECT TableId FROM Orders WHERE Id = @oid", conn, trans))
                            {
                                cmdGetTable.Parameters.AddWithValue("@oid", orderId);
                                var tidObj = cmdGetTable.ExecuteScalar();
                                if (tidObj != null && tidObj != DBNull.Value)
                                {
                                    int tid = Convert.ToInt32(tidObj);
                                    using var cmdEmptyTable = new SqlCommand("UPDATE Tables SET Status = 'Empty' WHERE Id = @tid", conn, trans);
                                    cmdEmptyTable.Parameters.AddWithValue("@tid", tid);
                                    cmdEmptyTable.ExecuteNonQuery();
                                }
                            }

                            using var cmdDelOrder = new SqlCommand("DELETE FROM Orders WHERE Id = @oid", conn, trans);
                            cmdDelOrder.Parameters.AddWithValue("@oid", orderId);
                            cmdDelOrder.ExecuteNonQuery();
                        }
                    }
                }

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

    public class MobileUpdateQuantityRequest
    {
        public int OrderDetailId { get; set; }
        public int Quantity { get; set; }
    }

    public class MobileDeleteItemRequest
    {
        public int OrderDetailId { get; set; }
    }

    public class MobileUpdateNoteRequest
    {
        public int OrderDetailId { get; set; }
        public string Note { get; set; } = "";
    }

    public class MobileTableDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Status { get; set; } = "Empty";
        public bool IsOccupied { get; set; }
        public bool HasPrintedBill { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public string OccupiedTimeText { get; set; } = "";
    }

    public class MobileCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class MobileProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public string Unit { get; set; } = "";
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public bool IsExcludeFromKitchen { get; set; }
    }

    public class MobileCartItemDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TotalPrice => Price * Quantity;
        public string Note { get; set; } = "";
        public string KitchenStatus { get; set; } = "Pending";
    }

    public class MobileOrderItemRequest
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string Note { get; set; } = "";
    }

    public class MobileOrderRequest
    {
        public int TableId { get; set; }
        public List<MobileOrderItemRequest> Items { get; set; } = new();
    }

        private static void EnsureStaticFilesExist()
        {
            // Use both possible locations so files are found whether running via dotnet run or published exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();

            WriteStaticFiles(baseDir);
            if (!string.Equals(baseDir.TrimEnd('\\', '/'), currentDir.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
            {
                WriteStaticFiles(currentDir);
            }
        }

        private static void WriteStaticFiles(string rootDir)
        {
            string wwwroot = Path.Combine(rootDir, "wwwroot");
            if (!Directory.Exists(wwwroot))
            {
                Directory.CreateDirectory(wwwroot);
            }

            // Create order.html for mobile preview/PWA
            string orderHtmlPath = Path.Combine(wwwroot, "order.html");
            string sourceOrderHtml = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "order.html");
            if (File.Exists(sourceOrderHtml) && !string.Equals(Path.GetFullPath(sourceOrderHtml), Path.GetFullPath(orderHtmlPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceOrderHtml, orderHtmlPath, true);
            }

            // Create index.html
            string htmlPath = Path.Combine(wwwroot, "index.html");
            string htmlContent = @"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, user-scalable=no"">
    <title>Nha bep - CafeManagement</title>
    <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700;800;900&amp;display=swap"" rel=""stylesheet"">
    <link rel=""stylesheet"" href=""style.css?v=1.2"">
</head>
<body>
    <header class=""header"">
        <div class=""header-left"">
            <span class=""header-icon"">&#127859;</span>
            <h1>NHA BEP</h1>
        </div>
        <div class=""header-right"">
            <div class=""status-indicator"" id=""status-badge"">
                <span class=""indicator-dot""></span>
                <span id=""connection-status"">Dang ket noi...</span>
            </div>
            <div class=""order-count"" id=""order-count"" style=""display:none"">
                <span id=""order-count-num"">0</span> mon
            </div>
        </div>
    </header>

    <main class=""container"">
        <div id=""no-orders"" class=""no-orders"">
            <div class=""empty-icon"">&#127859;</div>
            <p class=""empty-text"">Khong co yeu cau nao can che bien</p>
            <p class=""empty-sub"">He thong se tu dong cap nhat khi co don moi</p>
        </div>

        <div id=""orders-grid"" class=""orders-grid"">
            <!-- Cards populated by JS -->
        </div>
    </main>

    <div id=""toast-container""></div>

    <script src=""app.js?v=1.2""></script>
</body>
</html>";
            File.WriteAllText(htmlPath, htmlContent);

            // Create style.css (Premium Dark Mode style for kitchen screens)
            string cssPath = Path.Combine(wwwroot, "style.css");
            if (!File.Exists(cssPath))
            {
                string cssContent = @"/* ── CafeManagement Kitchen Screen Premium CSS ── */

@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&display=swap');

* {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
}

:root {
    --bg-main: #090d16;
    --bg-card: #151c2c;
    --bg-card-hover: #1c263c;
    --border-color: #263554;
    --text-primary: #f8fafc;
    --text-secondary: #94a3b8;
    --text-muted: #64748b;
    
    --color-primary: #38bdf8;
    --color-primary-glow: rgba(56, 189, 248, 0.15);
    --color-success: #10b981;
    --color-success-hover: #059669;
    --color-success-bg: rgba(16, 185, 129, 0.1);
    --color-warning: #f59e0b;
    --color-warning-bg: rgba(245, 158, 11, 0.1);
    --color-danger: #ef4444;
    --color-danger-bg: rgba(239, 68, 68, 0.1);
    --color-info: #0284c7;
    --color-info-hover: #0369a1;
    
    --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.05);
    --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
    --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.3), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
    --shadow-glow: 0 0 15px rgba(56, 189, 248, 0.3);
}

body {
    background-color: var(--bg-main);
    color: var(--text-primary);
    min-height: 100vh;
    padding-bottom: 40px;
    background-image: 
        radial-gradient(at 0% 0%, rgba(30, 41, 59, 0.3) 0, transparent 50%),
        radial-gradient(at 50% 0%, rgba(15, 23, 42, 0.3) 0, transparent 50%);
}

/* ── HEADER ── */
.header {
    background-color: rgba(21, 28, 44, 0.85);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    padding: 18px 24px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid var(--border-color);
    position: sticky;
    top: 0;
    z-index: 100;
    box-shadow: var(--shadow-md);
}

.header-left {
    display: flex;
    align-items: center;
    gap: 12px;
}

.header-icon {
    font-size: 1.8rem;
    animation: bounce-slow 3s infinite;
}

.header h1 {
    font-size: 1.6rem;
    font-weight: 900;
    letter-spacing: -0.025em;
    background: linear-gradient(to right, #38bdf8, #818cf8);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
}

.header-right {
    display: flex;
    align-items: center;
    gap: 16px;
}

.status-indicator {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--text-secondary);
    background-color: rgba(38, 53, 84, 0.4);
    padding: 6px 12px;
    border-radius: 9999px;
    border: 1px solid rgba(255, 255, 255, 0.05);
    transition: all 0.3s;
}

.indicator-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background-color: #64748b;
    position: relative;
}

.connected .indicator-dot {
    background-color: var(--color-success);
}
.connected .indicator-dot::after {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    border-radius: 50%;
    background-color: var(--color-success);
    animation: pulse-ring 1.5s infinite;
}

.disconnected .indicator-dot {
    background-color: var(--color-danger);
}
.disconnected .indicator-dot::after {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    border-radius: 50%;
    background-color: var(--color-danger);
    animation: pulse-ring 1.2s infinite;
}

.order-count {
    font-size: 0.85rem;
    font-weight: 700;
    color: #fff;
    background-color: var(--color-danger);
    padding: 6px 14px;
    border-radius: 9999px;
    box-shadow: 0 0 10px rgba(239, 68, 68, 0.4);
    animation: pulse-glow 2s infinite;
}

/* ── CONTAINER & GRID ── */
.container {
    max-width: 1600px;
    margin: 24px auto;
    padding: 0 24px;
}

.orders-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
    gap: 24px;
}

/* ── TABLE CARD ── */
.table-card {
    background-color: var(--bg-card);
    border-radius: 16px;
    border: 1px solid var(--border-color);
    overflow: hidden;
    display: flex;
    flex-direction: column;
    box-shadow: var(--shadow-lg);
    transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1), border-color 0.25s, box-shadow 0.25s;
}

.table-card:hover {
    transform: translateY(-4px);
    border-color: #3b5280;
    box-shadow: 0 12px 25px -5px rgba(0, 0, 0, 0.4), 0 0 10px rgba(56, 189, 248, 0.05);
}

.table-card-header {
    background-color: rgba(30, 41, 59, 0.4);
    padding: 14px 18px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid var(--border-color);
}

.table-title {
    font-size: 1.25rem;
    font-weight: 800;
    color: var(--text-primary);
    letter-spacing: -0.02em;
}

.table-time {
    font-size: 0.8rem;
    font-weight: 600;
    color: var(--text-secondary);
    background-color: rgba(15, 23, 42, 0.5);
    padding: 4px 10px;
    border-radius: 8px;
    border: 1px solid rgba(255, 255, 255, 0.02);
}

.table-card-body {
    padding: 10px 14px 14px 14px;
    display: flex;
    flex-direction: column;
    gap: 8px;
}

/* ── ITEM ROW ── */
.item-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 14px;
    border-radius: 10px;
    border: 1px solid rgba(255, 255, 255, 0.03);
    transition: all 0.2s ease;
}

/* Status-based styles for item rows */
.item-row.status-pending {
    background-color: rgba(255, 255, 255, 0.03);
    color: var(--text-primary);
}
.item-row.status-pending:hover {
    background-color: rgba(255, 255, 255, 0.06);
}
.item-row.status-pending .item-product-name {
    color: #fff;
}

.item-row.status-completed {
    background-color: rgba(16, 185, 129, 0.06);
    border-color: rgba(16, 185, 129, 0.15);
    color: #a7f3d0;
}
.item-row.status-completed:hover {
    background-color: rgba(16, 185, 129, 0.09);
}
.item-row.status-completed .item-product-name {
    color: #6ee7b7;
    text-decoration: none;
}
.item-row.status-completed .item-qty {
    color: #a7f3d0;
    opacity: 0.8;
}

.item-row.status-served {
    background-color: rgba(239, 68, 68, 0.08);
    border-color: rgba(239, 68, 68, 0.15);
    color: #fca5a5;
    opacity: 0.8;
}
.item-row.status-served .item-product-name {
    color: #f87171;
    text-decoration: line-through;
}

.item-detail {
    flex-grow: 1;
    padding-right: 12px;
    display: flex;
    flex-direction: column;
    gap: 4px;
}

.item-product-name {
    font-size: 1.05rem;
    font-weight: 700;
    line-height: 1.3;
}

.item-qty-and-note {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
}

.item-qty {
    font-size: 0.85rem;
    font-weight: 700;
    color: var(--text-secondary);
    background-color: rgba(0, 0, 0, 0.2);
    padding: 2px 6px;
    border-radius: 4px;
}

.item-note {
    background-color: rgba(245, 158, 11, 0.15);
    padding: 2px 8px;
    border-radius: 4px;
    border-left: 2px solid var(--color-warning);
    font-size: 0.8rem;
    font-weight: 600;
    color: #fde68a;
    display: inline-block;
    max-width: fit-content;
}

.stage-badge {
    font-size: 0.75rem;
    font-weight: 600;
    padding: 2px 8px;
    border-radius: 6px;
    display: inline-flex;
    align-items: center;
    gap: 4px;
}
.stage-badge.stage-pending {
    background-color: rgba(245, 158, 11, 0.15);
    color: #fbd38d;
    border: 1px solid rgba(245, 158, 11, 0.3);
}
.stage-badge.stage-completed {
    background-color: rgba(16, 185, 129, 0.2);
    color: #34d399;
    border: 1px solid rgba(16, 185, 129, 0.4);
}
.stage-badge.stage-served {
    background-color: rgba(148, 163, 184, 0.15);
    color: #cbd5e1;
}

/* ── BUTTONS ── */
.btn-complete-item {
    color: white;
    border: none;
    padding: 8px 14px;
    border-radius: 8px;
    font-size: 0.85rem;
    font-weight: 700;
    cursor: pointer;
    transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
    min-width: 130px;
    text-align: center;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.btn-complete-item:active {
    transform: scale(0.95);
}

/* Colors based on states */
.status-pending .btn-complete-item {
    background-color: var(--color-success);
}
.status-pending .btn-complete-item:hover {
    background-color: var(--color-success-hover);
    box-shadow: 0 0 10px rgba(16, 185, 129, 0.3);
}

.status-completed .btn-complete-item {
    background-color: var(--color-info);
}
.status-completed .btn-complete-item:hover {
    background-color: var(--color-info-hover);
    box-shadow: 0 0 10px rgba(2, 132, 199, 0.3);
}

.status-served .btn-complete-item {
    background-color: #334155;
    color: var(--text-muted);
    cursor: not-allowed;
    box-shadow: none;
}

/* ── EMPTY STATE ── */
.no-orders {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 400px;
    text-align: center;
    background-color: var(--bg-card);
    border-radius: 20px;
    border: 1px dashed var(--border-color);
    padding: 40px;
    margin-top: 20px;
}

.empty-icon {
    font-size: 5rem;
    margin-bottom: 16px;
    filter: drop-shadow(0 0 10px rgba(245, 158, 11, 0.2));
    animation: rotate-slow 15s linear infinite;
}

.empty-text {
    font-size: 1.3rem;
    font-weight: 800;
    color: var(--text-primary);
    margin-bottom: 6px;
}

.empty-sub {
    font-size: 0.95rem;
    color: var(--text-secondary);
}

/* ── TOAST CONTAINER & ALERTS ── */
#toast-container {
    position: fixed;
    top: 90px;
    right: 24px;
    z-index: 9999;
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.toast {
    background: rgba(21, 28, 44, 0.9);
    border: 1px solid var(--color-primary);
    color: #fff;
    padding: 16px 20px;
    border-radius: 12px;
    font-weight: 600;
    font-size: 0.95rem;
    box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 0 15px rgba(56, 189, 248, 0.15);
    backdrop-filter: blur(8px);
    -webkit-backdrop-filter: blur(8px);
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 280px;
    transform: translateX(120%);
    animation: slideIn 0.35s cubic-bezier(0.16, 1, 0.3, 1) forwards;
}

.toast.toast-fadeout {
    animation: fadeOut 0.4s ease forwards;
}

/* ── ANIMATIONS ── */
@keyframes pulse-ring {
    0% { transform: scale(0.95); opacity: 0.8; }
    50% { transform: scale(1.6); opacity: 0; }
    100% { transform: scale(0.95); opacity: 0; }
}

@keyframes pulse-glow {
    0%, 100% { box-shadow: 0 0 8px rgba(239, 68, 68, 0.4); }
    50% { box-shadow: 0 0 18px rgba(239, 68, 68, 0.7); }
}

@keyframes bounce-slow {
    0%, 100% { transform: translateY(0); }
    50% { transform: translateY(-6px); }
}

@keyframes rotate-slow {
    from { transform: rotate(0deg); }
    to { transform: rotate(360deg); }
}

@keyframes slideIn {
    to { transform: translateX(0); }
}

@keyframes fadeOut {
    to { opacity: 0; transform: translateY(-10px); }
}

/* ── RESPONSIVENESS ── */
@media (max-width: 640px) {
    .header {
        padding: 14px 16px;
    }
    .header h1 {
        font-size: 1.3rem;
    }
    .container {
        padding: 0 16px;
        margin: 16px auto;
    }
    .orders-grid {
        grid-template-columns: 1fr;
        gap: 16px;
    }
}";
                File.WriteAllText(cssPath, cssContent);
            }

            // Always overwrite app.js so notification logic is always up to date
            string jsPath = Path.Combine(wwwroot, "app.js");
            string jsContent = @"const statusText = document.getElementById('connection-status');
const statusBadge = document.getElementById('status-badge');
const orderCountEl = document.getElementById('order-count');
const orderCountNum = document.getElementById('order-count-num');
const noOrdersEl = document.getElementById('no-orders');
const gridEl = document.getElementById('orders-grid');
const toastContainer = document.getElementById('toast-container');

let eventSource = null;
let lastOrderCount = -1;
let wakeLock = null;

// ── WakeLock: Keep screen active ──
async function requestWakeLock() {
    if ('wakeLock' in navigator) {
        try {
            wakeLock = await navigator.wakeLock.request('screen');
            document.addEventListener('visibilitychange', async () => {
                if (document.visibilityState === 'visible' && wakeLock === null) {
                    wakeLock = await navigator.wakeLock.request('screen');
                }
            });
        } catch (e) {
            // WakeLock not active, ignore
        }
    }
}

// ── Notification Permissions ──
async function requestNotificationPermission() {
    if ('Notification' in window && Notification.permission === 'default') {
        await Notification.requestPermission();
    }
}

function sendNotification(title, body) {
    if ('Notification' in window && Notification.permission === 'granted') {
        new Notification(title, {
            body: body,
            icon: 'data:image/svg+xml,<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 100 100""><text y="".9em"" font-size=""90"">🍳</text></svg>',
            tag: 'kitchen-alert',
            renotify: true,
            requireInteraction: false
        });
    }
}

// ── Audio Alert System (Loud Crisp Tinh Tinh Chime) ──
function playAlertSound() {
    if (localStorage.getItem('kitchen_sound_disabled') === 'true') return;
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        if (ctx.state === 'suspended') {
            ctx.resume();
        }
        
        // 'Tinh Tinh' Dual Bell Tones (1174 Hz -> 1568 Hz -> 1760 Hz)
        const notes = [
            { freq: 1174.66, delay: 0.0,  dur: 0.25, volume: 0.8 },
            { freq: 1567.98, delay: 0.20, dur: 0.25, volume: 0.9 },
            { freq: 1760.00, delay: 0.40, dur: 0.45, volume: 1.0 }
        ];

        notes.forEach(function(note) {
            const osc = ctx.createOscillator();
            const oscHarmonic = ctx.createOscillator();
            const gain = ctx.createGain();

            osc.type = 'sine';
            osc.frequency.value = note.freq;

            oscHarmonic.type = 'sine';
            oscHarmonic.frequency.value = note.freq * 2;

            osc.connect(gain);
            oscHarmonic.connect(gain);
            gain.connect(ctx.destination);

            const startTime = ctx.currentTime + note.delay;
            const stopTime = startTime + note.dur;

            gain.gain.setValueAtTime(0, startTime);
            gain.gain.linearRampToValueAtTime(note.volume, startTime + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.001, stopTime);

            osc.start(startTime);
            oscHarmonic.start(startTime);
            osc.stop(stopTime);
            oscHarmonic.stop(stopTime);
        });
    } catch (e) {
        // AudioContext blocked or not supported, ignore
    }
}

// ── Vibration System ──
function vibrate() {
    if ('vibrate' in navigator) {
        navigator.vibrate([200, 100, 200, 100, 400]);
    }
}

// ── Custom Toasts ──
function showToast(message) {
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.innerHTML = '<span>🔔</span> <span>' + message + '</span>';
    toastContainer.appendChild(toast);
    
    // Auto-remove animation
    setTimeout(() => {
        toast.classList.add('toast-fadeout');
        setTimeout(() => toast.remove(), 400);
    }, 2800);
}

// ── Trigger Alerts ──
function triggerAlert(newCount) {
    const itemText = newCount + ' món cần chế biến';
    sendNotification('🍳 Có đơn mới!', itemText);
    playAlertSound();
    vibrate();
    showToast('Đơn mới! ' + itemText);
}

// ── SSE Connection Manager ──
function connectSSE() {
    statusText.innerText = 'Đang kết nối...';
    statusBadge.className = 'status-indicator disconnected';

    eventSource = new EventSource('/api/kitchen/events');

    eventSource.onopen = () => {
        statusText.innerText = 'Đã kết nối';
        statusBadge.className = 'status-indicator connected';
        loadOrders();
    };

    eventSource.onerror = () => {
        statusText.innerText = 'Mất kết nối - Thử lại...';
        statusBadge.className = 'status-indicator disconnected';
        eventSource.close();
        setTimeout(connectSSE, 3000);
    };

    eventSource.addEventListener('ping', () => {});

    eventSource.addEventListener('update', () => {
        loadOrders(true);
    });
}

// ── Load Orders ──
async function loadOrders(fromUpdate = false) {
    try {
        const res = await fetch('/api/kitchen/pending');
        if (!res.ok) throw new Error();
        const orders = await res.json();
        const activeCount = orders.filter(item => item.status !== 'Served').length;

        // Alert only when there are new items added
        if (fromUpdate && lastOrderCount >= 0 && activeCount > lastOrderCount) {
            triggerAlert(activeCount);
        }
        
        lastOrderCount = activeCount;
        
        // Update header count badge
        if (activeCount > 0) {
            orderCountNum.innerText = activeCount;
            orderCountEl.style.display = 'block';
        } else {
            orderCountEl.style.display = 'none';
        }
        
        renderOrders(orders);
    } catch (err) {
        console.error('Error loading orders:', err);
    }
}

// ── Render Orders Grid ──
function renderOrders(orders) {
    gridEl.innerHTML = '';
    if (orders.length === 0) {
        noOrdersEl.style.display = 'flex';
        gridEl.style.display = 'none';
        return;
    }

    // Group items by table
    const groups = {};
    orders.forEach(item => {
        const key = item.tableNumber || 'Mang đi';
        if (!groups[key]) {
            groups[key] = {
                tableName: key,
                items: [],
                minCreatedAt: new Date(item.createdAt)
            };
        } else {
            const itemDate = new Date(item.createdAt);
            if (itemDate < groups[key].minCreatedAt) {
                groups[key].minCreatedAt = itemDate;
            }
        }
        groups[key].items.push(item);
    });

    // Sort tables: oldest minCreatedAt first
    const sortedGroups = Object.values(groups).sort((a, b) => a.minCreatedAt - b.minCreatedAt);

    // Filter out groups where ALL items have been served
    const activeGroups = sortedGroups.filter(group => {
        return group.items.some(item => item.status !== 'Served');
    });

    if (activeGroups.length === 0) {
        noOrdersEl.style.display = 'flex';
        gridEl.style.display = 'none';
        return;
    }

    noOrdersEl.style.display = 'none';
    gridEl.style.display = 'grid';

    activeGroups.forEach(group => {
        const card = document.createElement('div');
        card.className = 'table-card';

        // Calculate time elapsed
        const elapsed = Math.floor((new Date() - group.minCreatedAt) / 60000);
        let urgencyBorder = '5px solid #10b981';
        
        if (elapsed >= 15) {
            urgencyBorder = '5px solid #ef4444';
            card.style.animation = 'pulse-glow 2s infinite';
        } else if (elapsed >= 8) {
            urgencyBorder = '5px solid #f59e0b';
        }
        
        const d = group.minCreatedAt;
        const hh = String(d.getHours()).padStart(2, '0');
        const mm = String(d.getMinutes()).padStart(2, '0');
        const timeText = hh + ':' + mm;

        // Card Header
        let cardHtml = `
            <div class=""table-card-header"" style=""border-left: ${urgencyBorder}"">
                <div class=""table-title"">${escapeHtml(group.tableName)}</div>
                <div class=""table-time"">🕒 ${timeText} (${elapsed}m)</div>
            </div>
            <div class=""table-card-body"">
        `;

        // Render Card Items
        group.items.forEach(item => {
            let noteHtml = item.note ? '<span class=""item-note"">' + escapeHtml(item.note) + '</span>' : '';
            
            // Map statuses to CSS classes, labels, and button texts
            let rowClass = 'item-row status-pending';
            let btnText = 'Chế biến xong';
            let btnDisabled = '';
            let stageBadge = '<span class=""stage-badge stage-pending"">⏳ Chờ chế biến</span>';
            
            if (item.status === 'Completed') {
                rowClass = 'item-row status-completed';
                btnText = 'Chờ in';
                btnDisabled = 'disabled';
                stageBadge = '<span class=""stage-badge stage-completed"">✅ Chờ in (Bếp)</span>';
            } else if (item.status === 'Served') {
                rowClass = 'item-row status-served';
                btnText = 'Đã in & chuyển';
                btnDisabled = 'disabled';
                stageBadge = '<span class=""stage-badge stage-served"">👍 Đã giao</span>';
            }
            
            cardHtml += `
                <div class=""${rowClass}"">
                    <div class=""item-detail"">
                        <div class=""item-product-name"">${escapeHtml(item.productName)}</div>
                        <div class=""item-qty-and-note"">
                            <span class=""item-qty"">SL: ${item.quantity}</span>
                            ${stageBadge}
                            ${noteHtml}
                        </div>
                    </div>
                    <button class=""btn-complete-item"" ${btnDisabled} onclick=""completeItem(${item.id}, this)"">${btnText}</button>
                </div>
            `;
        });

        cardHtml += `</div>`;
        card.innerHTML = cardHtml;
        gridEl.appendChild(card);
    });
}

// ── Complete Item Action ──
async function completeItem(id, btn) {
    btn.disabled = true;
    const oldText = btn.innerText;
    btn.innerText = '...';
    try {
        const res = await fetch('/api/kitchen/complete/' + id, { method: 'POST' });
        if (res.ok) {
            loadOrders();
        } else {
            showToast('Không thể cập nhật món. Thử lại sau.');
            btn.disabled = false;
            btn.innerText = oldText;
        }
    } catch (err) {
        console.error('Error completing item:', err);
        showToast('Kết nối thất bại. Thử lại sau.');
        btn.disabled = false;
        btn.innerText = oldText;
    }
}

// ── Helpers ──
function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;')
              .replace(/</g, '&lt;')
              .replace(/>/g, '&gt;')
              .replace(/""/g, '&quot;')
              .replace(/'/g, '&#039;');
}

// ── Initialization ──
requestWakeLock();
requestNotificationPermission();
connectSSE();
setInterval(() => {
    // Keep elapsed time indicator fresh every minute
    loadOrders(false);
}, 60000);
";
            File.WriteAllText(jsPath, jsContent);
        }
    }
}

