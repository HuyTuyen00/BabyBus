using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;
using CafeManagement.Models;
using CafeManagement.WebServer;

namespace CafeManagement.Views
{
    public partial class KitchenView : UserControl
    {
        private readonly HashSet<int> _previousItemIds = new();
        private bool _isFirstLoad = true;

        private Point _scrollStartPoint;
        private double _scrollStartOffset;
        private bool _isDraggingScroll = false;

        private static readonly string ConfigPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kitchen_sound.txt");

        public KitchenView()
        {
            InitializeComponent();
            Loaded += KitchenView_Loaded;
            Unloaded += KitchenView_Unloaded;
        }

        private async void KitchenView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSoundSetting();
            // Subscribe to real-time events from the mobile clients/web API
            KitchenWebServer.KitchenStatusChanged += OnKitchenStatusChanged;
            await LoadKitchenItemsAsync();
        }

        private void LoadSoundSetting()
        {
            try
            {
                if (System.IO.File.Exists(ConfigPath))
                {
                    string txt = System.IO.File.ReadAllText(ConfigPath).Trim();
                    if (bool.TryParse(txt, out bool enabled))
                    {
                        chkEnableSound.IsChecked = enabled;
                    }
                }
            }
            catch {}
        }

        private void ChkEnableSound_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.File.WriteAllText(ConfigPath, chkEnableSound.IsChecked == true ? "true" : "false");
            }
            catch {}
        }

        private void BtnTestSound_Click(object sender, RoutedEventArgs e)
        {
            CafeManagement.Utilities.ChimePlayer.PlayTinhTinh();
        }

        private void KitchenView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe to avoid memory leaks
            KitchenWebServer.KitchenStatusChanged -= OnKitchenStatusChanged;
        }

        private async void OnKitchenStatusChanged()
        {
            // Re-run connection-safe async load
            await LoadKitchenItemsAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadKitchenItemsAsync();
        }

        private void RadTab_Checked(object sender, RoutedEventArgs e)
        {
            if (panelByTable == null || panelByProduct == null) return;
            if (radTabByProduct?.IsChecked == true)
            {
                panelByTable.Visibility = Visibility.Collapsed;
                panelByProduct.Visibility = Visibility.Visible;
            }
            else
            {
                panelByTable.Visibility = Visibility.Visible;
                panelByProduct.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadKitchenItemsAsync()
        {
            try
            {
                var rawItems = await Task.Run(() => GetPendingKitchenItemsFromDb());

                // Detect new items
                var activeItems = rawItems.FindAll(item => item.Status != "Served");
                bool hasNewItem = false;
                if (!_isFirstLoad)
                {
                    foreach (var item in activeItems)
                    {
                        if (!_previousItemIds.Contains(item.Id))
                        {
                            hasNewItem = true;
                            break;
                        }
                    }
                }
                else
                {
                    _isFirstLoad = false;
                }

                _previousItemIds.Clear();
                foreach (var item in activeItems)
                {
                    _previousItemIds.Add(item.Id);
                }

                if (hasNewItem && (chkEnableSound?.IsChecked == true))
                {
                    CafeManagement.Utilities.ChimePlayer.PlayTinhTinh();
                }

                // 1. Group by table for "Theo bàn" Tab
                var groupsDict = new Dictionary<string, KitchenTableGroup>();
                foreach (var item in rawItems)
                {
                    string key = string.IsNullOrEmpty(item.TableNumber) ? "Mang đi" : item.TableNumber;
                    if (!groupsDict.TryGetValue(key, out var group))
                    {
                        group = new KitchenTableGroup
                        {
                            TableName = key,
                            MinCreatedAt = item.CreatedAt
                        };
                        groupsDict[key] = group;
                    }
                    else
                    {
                        if (item.CreatedAt < group.MinCreatedAt)
                        {
                            group.MinCreatedAt = item.CreatedAt;
                        }
                    }
                    group.Items.Add(item);
                }

                // Sort and filter out tables where ALL items have been served
                var sortedGroups = new List<KitchenTableGroup>();
                foreach (var g in groupsDict.Values)
                {
                    bool hasActiveItems = false;
                    foreach (var item in g.Items)
                    {
                        if (item.Status != "Served" || item.RemainingQuantity > 0)
                        {
                            hasActiveItems = true;
                            break;
                        }
                    }
                    if (hasActiveItems)
                    {
                        sortedGroups.Add(g);
                    }
                }
                sortedGroups.Sort((a, b) => a.MinCreatedAt.CompareTo(b.MinCreatedAt));

                // Bind to itemsTables
                itemsTables.ItemsSource = sortedGroups;

                // 2. Aggregate remaining quantities for "Theo món" Tab (Chờ chế biến)
                var productSummaryDict = new Dictionary<string, int>();
                foreach (var item in rawItems)
                {
                    if (item.Status != "Served" && item.RemainingQuantity > 0)
                    {
                        if (!productSummaryDict.ContainsKey(item.ProductName))
                        {
                            productSummaryDict[item.ProductName] = 0;
                        }
                        productSummaryDict[item.ProductName] += item.RemainingQuantity;
                    }
                }

                var productSummaryList = productSummaryDict
                    .Select(kv => new KitchenProductSummary
                    {
                        ProductName = kv.Key,
                        RemainingQuantity = kv.Value
                    })
                    .OrderBy(p => p.ProductName)
                    .ToList();

                itemsProductSummary.ItemsSource = productSummaryList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải danh sách chế biến: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<KitchenItem> GetPendingKitchenItemsFromDb()
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

        // Single Increment: [ > ] increases completed quantity by 1
        private async void BtnIncrementItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int itemId)
            {
                btn.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();
                        using var cmd = new SqlCommand(@"
                            UPDATE KitchenQueue 
                            SET CompletedQuantity = CASE 
                                WHEN CompletedQuantity + 1 <= (Quantity - ISNULL(PrintedQuantity, 0)) THEN CompletedQuantity + 1 
                                ELSE (Quantity - ISNULL(PrintedQuantity, 0)) 
                            END,
                            Status = 'Completed'
                            WHERE Id = @Id AND (Quantity - ISNULL(PrintedQuantity, 0)) > 0", conn);
                        cmd.Parameters.AddWithValue("@Id", itemId);
                        cmd.ExecuteNonQuery();
                    });

                    _ = SseManager.BroadcastAsync("update", "completed");
                    KitchenWebServer.NotifyKitchenStatusChanged();
                    await LoadKitchenItemsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi cập nhật số lượng món: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        // Complete All Remaining for Item: [ >> ] sets completed quantity = remaining
        private async void BtnCompleteAllItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int itemId)
            {
                btn.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();
                        using var cmd = new SqlCommand(@"
                            UPDATE KitchenQueue 
                            SET CompletedQuantity = (Quantity - ISNULL(PrintedQuantity, 0)),
                                Status = 'Completed'
                            WHERE Id = @Id AND (Quantity - ISNULL(PrintedQuantity, 0)) > 0", conn);
                        cmd.Parameters.AddWithValue("@Id", itemId);
                        cmd.ExecuteNonQuery();
                    });

                    _ = SseManager.BroadcastAsync("update", "completed");
                    KitchenWebServer.NotifyKitchenStatusChanged();
                    await LoadKitchenItemsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi cập nhật món: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        // Complete All Items for the Entire Table: [ >> ] in table header
        private async void BtnCompleteTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tableName)
            {
                btn.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();
                        using var cmd = new SqlCommand(@"
                            UPDATE k 
                            SET k.CompletedQuantity = (k.Quantity - ISNULL(k.PrintedQuantity, 0)),
                                k.Status = 'Completed'
                            FROM KitchenQueue k
                            JOIN Orders o ON k.OrderId = o.Id
                            LEFT JOIN Tables t ON o.TableId = t.Id
                            WHERE (t.Name = @TableName OR (@TableName = 'Mang đi' AND t.Name IS NULL))
                              AND o.Status = 'Pending'
                              AND (k.Quantity - ISNULL(k.PrintedQuantity, 0)) > 0", conn);
                        cmd.Parameters.AddWithValue("@TableName", tableName);
                        cmd.ExecuteNonQuery();
                    });

                    _ = SseManager.BroadcastAsync("update", "completed");
                    KitchenWebServer.NotifyKitchenStatusChanged();
                    await LoadKitchenItemsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi hoàn thành bàn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        // Print Kitchen Slip for Completed Items
        private async void BtnPrintKitchenSlip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tableName)
            {
                btn.IsEnabled = false;
                try
                {
                    var printItems = new List<OrderDetail>();
                    var completedQueueIds = new List<int>();

                    await Task.Run(() =>
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();

                        // 1. Find all items with CompletedQuantity > 0 for this table
                        string sql = @"
                            SELECT k.Id, k.OrderId, k.ProductId, k.CompletedQuantity, ISNULL(k.PrintedQuantity, 0), k.Quantity, k.Note, p.Name AS ProductName
                            FROM KitchenQueue k
                            JOIN Products p ON k.ProductId = p.Id
                            JOIN Orders o ON k.OrderId = o.Id
                            LEFT JOIN Tables t ON o.TableId = t.Id
                            WHERE (t.Name = @TableName OR (@TableName = 'Mang đi' AND t.Name IS NULL))
                              AND o.Status = 'Pending'
                              AND ISNULL(k.CompletedQuantity, 0) > 0";

                        using (var cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@TableName", tableName);
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                int qId = Convert.ToInt32(reader.GetValue(0));
                                int orderId = Convert.ToInt32(reader.GetValue(1));
                                int prodId = Convert.ToInt32(reader.GetValue(2));
                                int completedQty = Convert.ToInt32(reader.GetValue(3));
                                string note = reader.IsDBNull(6) ? "" : reader.GetString(6);
                                string prodName = reader.GetString(7);

                                completedQueueIds.Add(qId);
                                printItems.Add(new OrderDetail
                                {
                                    Id = qId,
                                    OrderId = orderId,
                                    ProductId = prodId,
                                    Quantity = completedQty,
                                    Note = note,
                                    ProductName = prodName
                                });
                            }
                        }

                        // 2. If items found, update PrintedQuantity += CompletedQuantity, CompletedQuantity = 0, and Status
                        if (completedQueueIds.Count > 0)
                        {
                            string idsStr = string.Join(",", completedQueueIds);
                            string updateSql = $@"
                                UPDATE KitchenQueue
                                SET PrintedQuantity = ISNULL(PrintedQuantity, 0) + ISNULL(CompletedQuantity, 0),
                                    CompletedQuantity = 0,
                                    Status = CASE 
                                        WHEN ISNULL(PrintedQuantity, 0) + ISNULL(CompletedQuantity, 0) >= Quantity THEN 'Served' 
                                        ELSE 'Pending' 
                                    END
                                WHERE Id IN ({idsStr})";

                            using var cmdUpdate = new SqlCommand(updateSql, conn);
                            cmdUpdate.ExecuteNonQuery();
                        }
                    });

                    if (printItems.Count == 0)
                    {
                        MessageBox.Show("Không có món nào đang ở trạng thái 'Đã làm xong' để in!\nVui lòng bấm nút [ > ] hoặc [ >> ] để xác nhận số lượng món đã làm xong trước khi in.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Print using printer utility
                    CafeManagement.Utilities.PrinterHelper.PrintKitchenSlip(
                        tableName, 
                        printItems, 
                        CafeManagement.Utilities.SettingsHelper.Current.KitchenInvoiceCopies);

                    // Broadcast to LAN and refresh
                    await SseManager.BroadcastAsync("update", "completed");
                    KitchenWebServer.NotifyKitchenStatusChanged();
                    await LoadKitchenItemsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi in bếp: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        // ================= TOUCH & MOUSE DRAG-SCROLL HELPER =================
        private void ScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer sv && e.OriginalSource is not Button && e.OriginalSource is not TextBox && e.OriginalSource is not CheckBox && e.OriginalSource is not RadioButton)
            {
                _scrollStartPoint = e.GetPosition(sv);
                _scrollStartOffset = sv.VerticalOffset;
                _isDraggingScroll = true;
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingScroll && sender is ScrollViewer sv && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(sv);
                double delta = _scrollStartPoint.Y - currentPoint.Y;
                if (Math.Abs(delta) > 4)
                {
                    sv.ScrollToVerticalOffset(_scrollStartOffset + delta);
                }
            }
        }

        private void ScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingScroll = false;
        }

        public class KitchenTableGroup
        {
            public string TableName { get; set; } = string.Empty;
            public DateTime MinCreatedAt { get; set; }
            public string TimeText => MinCreatedAt.ToString("HH:mm") + $" ({Math.Floor((DateTime.Now - MinCreatedAt).TotalMinutes)}m)";
            public List<KitchenItem> Items { get; set; } = new();

            public string HeaderColorBrush
            {
                get
                {
                    double elapsed = (DateTime.Now - MinCreatedAt).TotalMinutes;
                    if (elapsed >= 15) return "#ef4444"; // Urgent Red
                    if (elapsed >= 8) return "#f59e0b";  // Medium Orange
                    return "#0f766e";                    // Normal Teal
                }
            }
        }

        public class KitchenProductSummary
        {
            public string ProductName { get; set; } = string.Empty;
            public int RemainingQuantity { get; set; }
        }
    }
}


