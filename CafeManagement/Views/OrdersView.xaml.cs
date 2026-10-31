using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;
using CafeManagement.Models;
using CafeManagement.Utilities;

namespace CafeManagement.Views
{
    public partial class OrdersView : UserControl
    {
        private List<Order> _allOrders = new List<Order>();

        public OrdersView()
        {
            InitializeComponent();
            dpStart.SelectedDate = DateTime.Today.AddDays(-30);
            dpEnd.SelectedDate = DateTime.Today;
            Loaded += OrdersView_Loaded;
        }

        private async void OrdersView_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadOrdersAsync();
        }

        private async void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            await LoadOrdersAsync();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Simple in-memory filter to keep search responsive
            FilterAndRenderOrders();
        }

        private void DpStart_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterAndRenderOrders();
        }

        private void DpEnd_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterAndRenderOrders();
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearch.Text == "Tìm theo mã hóa đơn...")
            {
                txtSearch.Text = "";
                txtSearch.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.Text = "Tìm theo mã hóa đơn...";
                txtSearch.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private async Task LoadOrdersAsync()
        {
            try
            {
                await Task.Run(() => LoadOrdersFromDb());
                FilterAndRenderOrders();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải lịch sử hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadOrdersFromDb()
        {
            _allOrders.Clear();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            const string sql = @"
                SELECT o.Id, o.OrderDate, ISNULL(t.Name,'') AS TableName, o.TotalAmount, o.Status
                FROM   Orders o
                LEFT JOIN Tables t ON t.Id = o.TableId
                ORDER BY o.OrderDate DESC";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _allOrders.Add(new Order
                {
                    Id          = Convert.ToInt32(reader.GetValue(0)),
                    OrderDate   = DateTime.Parse(reader.GetString(1)),
                    TableName   = reader.GetString(2),
                    TotalAmount = Convert.ToDecimal(reader.GetValue(3)),
                    Status      = reader.GetString(4)
                });
            }
        }

        private void FilterAndRenderOrders()
        {
            if (gridOrders == null || txtSearch == null || dpStart == null || dpEnd == null) return;

            string keyword = txtSearch.Text.Trim().ToLower();
            if (keyword == "tìm theo mã hóa đơn...") keyword = "";

            DateTime startDate = dpStart.SelectedDate ?? DateTime.Today.AddDays(-7);
            DateTime endDate = dpEnd.SelectedDate ?? DateTime.Today;
            // Set endDate to 23:59:59 to include full day
            endDate = endDate.Date.AddDays(1).AddSeconds(-1);

            var filtered = _allOrders.Where(o =>
                o.OrderDate >= startDate && o.OrderDate <= endDate &&
                (string.IsNullOrEmpty(keyword) || o.Id.ToString().Contains(keyword))
            ).ToList();

            gridOrders.ItemsSource = filtered;
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && gridOrders.ItemsSource is IEnumerable<Order> list)
            {
                bool isChecked = chk.IsChecked ?? false;
                foreach (var ord in list)
                {
                    ord.IsSelected = isChecked;
                }
            }
        }

        private async void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedOrders = _allOrders.Where(o => o.IsSelected).ToList();
            if (selectedOrders.Count == 0)
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất một hóa đơn cần xóa!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!AdminSecurityHelper.RequestAdminPassword($"xóa {selectedOrders.Count} hóa đơn đã chọn"))
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn XÓA VĨNH VIỄN {selectedOrders.Count} hóa đơn đã chọn không?\n\n(Dữ liệu các hóa đơn này và chi tiết món sẽ bị xóa hoàn toàn khỏi cơ sở dữ liệu)",
                "Xác nhận xóa nhiều hóa đơn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var trans = conn.BeginTransaction();
                    try
                    {
                        foreach (var ord in selectedOrders)
                        {
                            using (var cmd1 = new SqlCommand("DELETE FROM KitchenQueue WHERE OrderId = @oid", conn, trans))
                            {
                                cmd1.Parameters.AddWithValue("@oid", ord.Id);
                                cmd1.ExecuteNonQuery();
                            }

                            using (var cmd2 = new SqlCommand("DELETE FROM OrderDetails WHERE OrderId = @oid", conn, trans))
                            {
                                cmd2.Parameters.AddWithValue("@oid", ord.Id);
                                cmd2.ExecuteNonQuery();
                            }

                            using (var cmd3 = new SqlCommand("DELETE FROM Orders WHERE Id = @oid", conn, trans))
                            {
                                cmd3.Parameters.AddWithValue("@oid", ord.Id);
                                cmd3.ExecuteNonQuery();
                            }
                        }

                        using (var cmdSyncTables = new SqlCommand(
                            "UPDATE Tables SET Status = 'Empty' WHERE Id NOT IN (SELECT TableId FROM Orders WHERE Status = 'Pending' AND TableId IS NOT NULL)", conn, trans))
                        {
                            cmdSyncTables.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                });

                MessageBox.Show($"Đã xóa thành công {selectedOrders.Count} hóa đơn!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOrdersAsync();
                ClearDetailFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xóa hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            if (_allOrders.Count == 0)
            {
                MessageBox.Show("Hiện không có hóa đơn nào trong danh sách!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!AdminSecurityHelper.RequestAdminPassword("XÓA TOÀN BỘ LỊCH SỬ HÓA ĐƠN"))
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"⚠️ CẢNH BÁO NGUY HIỂM!\n\nBạn có chắc chắn muốn XÓA TOÀN BỘ TẤT CẢ ({_allOrders.Count}) HÓA ĐƠN trong lịch sử không?\n\n• Toàn bộ lịch sử bán hàng và đơn bếp sẽ được làm sạch.\n• Thao tác này không thể hoàn tác sau khi thực hiện!",
                "Xác nhận xóa TOÀN BỘ hóa đơn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var trans = conn.BeginTransaction();
                    try
                    {
                        using (var cmdK = new SqlCommand("DELETE FROM KitchenQueue", conn, trans))
                            cmdK.ExecuteNonQuery();

                        using (var cmdD = new SqlCommand("DELETE FROM OrderDetails", conn, trans))
                            cmdD.ExecuteNonQuery();

                        using (var cmdO = new SqlCommand("DELETE FROM Orders", conn, trans))
                            cmdO.ExecuteNonQuery();

                        using (var cmdSyncTables = new SqlCommand("UPDATE Tables SET Status = 'Empty'", conn, trans))
                            cmdSyncTables.ExecuteNonQuery();

                        using (var cmdReseed = new SqlCommand(@"
                            DBCC CHECKIDENT ('Orders', RESEED, 0);
                            DBCC CHECKIDENT ('OrderDetails', RESEED, 0);
                            DBCC CHECKIDENT ('KitchenQueue', RESEED, 0);", conn, trans))
                        {
                            try { cmdReseed.ExecuteNonQuery(); } catch { }
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                });

                MessageBox.Show("Đã xóa sạch toàn bộ hóa đơn thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOrdersAsync();
                ClearDetailFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xóa toàn bộ hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GridOrders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridOrders.SelectedItem is Order ord)
            {
                lblDetailOrderId.Text = ord.Id.ToString();
                lblDetailOrderDate.Text = ord.OrderDate.ToString("dd/MM/yyyy HH:mm");
                lblDetailTotal.Text = ord.TotalAmount.ToString("N0", CultureInfo.InvariantCulture) + " đ";

                // Enable/Disable buttons
                btnCancelOrder.IsEnabled = ord.Status != "Cancelled";
                btnDeleteSingleOrder.IsEnabled = true;

                try
                {
                    var details = await Task.Run(() => GetOrderDetailsFromDb(ord.Id));
                    gridDetails.ItemsSource = details;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi tải chi tiết: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                ClearDetailFields();
            }
        }

        private void ClearDetailFields()
        {
            lblDetailOrderId.Text = "-";
            lblDetailOrderDate.Text = "-";
            lblDetailTotal.Text = "0 đ";
            gridDetails.ItemsSource = null;
            btnCancelOrder.IsEnabled = false;
            btnDeleteSingleOrder.IsEnabled = false;
        }

        private List<OrderDetail> GetOrderDetailsFromDb(int orderId)
        {
            var list = new List<OrderDetail>();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            const string sql = @"
                SELECT od.Id, od.OrderId, od.ProductId, od.Quantity, od.Price, od.Note, p.Name,
                       ISNULL(od.IsCancelled, 0) AS IsCancelled
                FROM   OrderDetails od
                JOIN   Products p ON od.ProductId = p.Id
                WHERE  od.OrderId = @oid";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@oid", orderId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OrderDetail
                {
                    Id          = Convert.ToInt32(reader.GetValue(0)),
                    OrderId     = Convert.ToInt32(reader.GetValue(1)),
                    ProductId   = Convert.ToInt32(reader.GetValue(2)),
                    Quantity    = Convert.ToInt32(reader.GetValue(3)),
                    Price       = Convert.ToDecimal(reader.GetValue(4)),
                    Note        = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    ProductName = reader.GetString(6),
                    IsCancelled = Convert.ToBoolean(reader.GetValue(7))
                });
            }
            return list;
        }

        // Cancel order: transaction-based database query + refund stock + disable button
        private async void BtnCancelOrder_Click(object sender, RoutedEventArgs e)
        {
            if (gridOrders.SelectedItem is not Order ord) return;

            var confirm = MessageBox.Show($"Bạn có chắc chắn muốn HỦY hóa đơn #{ord.Id}?\nThao tác này sẽ cập nhật trạng thái hóa đơn thành 'Cancelled'!", 
                                            "Xác nhận hủy hóa đơn", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (confirm != MessageBoxResult.Yes) return;

            btnCancelOrder.IsEnabled = false;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var trans = conn.BeginTransaction();
                    try
                    {
                        using var cmd1 = new SqlCommand(
                            "UPDATE Orders SET Status='Cancelled' WHERE Id=@id", conn, trans);
                        cmd1.Parameters.AddWithValue("@id", ord.Id);
                        cmd1.ExecuteNonQuery();

                        using var cmd2 = new SqlCommand(
                            "UPDATE KitchenQueue SET Status='Completed' WHERE OrderId=@oid AND Status!='Completed'",
                            conn, trans);
                        cmd2.Parameters.AddWithValue("@oid", ord.Id);
                        cmd2.ExecuteNonQuery();

                        trans.Commit();
                    }
                    catch { trans.Rollback(); throw; }
                });

                MessageBox.Show($"Hủy hóa đơn #{ord.Id} thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                
                await LoadOrdersAsync();
                ClearDetailFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi hủy hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                btnCancelOrder.IsEnabled = true;
            }
        }

        private async void BtnDeleteSingleOrder_Click(object sender, RoutedEventArgs e)
        {
            if (gridOrders.SelectedItem is not Order ord) return;

            if (!AdminSecurityHelper.RequestAdminPassword($"xóa hóa đơn #{ord.Id}"))
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn XÓA VĨNH VIỄN hóa đơn #{ord.Id}?\n(Thao tác này không thể hoàn tác)",
                "Xác nhận xóa hóa đơn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var trans = conn.BeginTransaction();
                    try
                    {
                        using (var cmd1 = new SqlCommand("DELETE FROM KitchenQueue WHERE OrderId = @oid", conn, trans))
                        {
                            cmd1.Parameters.AddWithValue("@oid", ord.Id);
                            cmd1.ExecuteNonQuery();
                        }

                        using (var cmd2 = new SqlCommand("DELETE FROM OrderDetails WHERE OrderId = @oid", conn, trans))
                        {
                            cmd2.Parameters.AddWithValue("@oid", ord.Id);
                            cmd2.ExecuteNonQuery();
                        }

                        using (var cmd3 = new SqlCommand("DELETE FROM Orders WHERE Id = @oid", conn, trans))
                        {
                            cmd3.Parameters.AddWithValue("@oid", ord.Id);
                            cmd3.ExecuteNonQuery();
                        }

                        using (var cmdSyncTables = new SqlCommand(
                            "UPDATE Tables SET Status = 'Empty' WHERE Id NOT IN (SELECT TableId FROM Orders WHERE Status = 'Pending' AND TableId IS NOT NULL)", conn, trans))
                        {
                            cmdSyncTables.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                });

                MessageBox.Show($"Đã xóa hóa đơn #{ord.Id} thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOrdersAsync();
                ClearDetailFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xóa hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (gridOrders.ItemsSource == null) return;
            var listToExport = ((IEnumerable<Order>)gridOrders.ItemsSource).ToList();

            var saveDlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"DanhSachHoaDon_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (saveDlg.ShowDialog() == true)
            {
                try
                {
                    ExcelHelper.ExportOrders(saveDlg.FileName, listToExport);
                    MessageBox.Show("Xuất Excel thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
