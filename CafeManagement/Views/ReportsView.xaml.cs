using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;

namespace CafeManagement.Views
{
    public partial class ReportsView : UserControl
    {
        public class TopProductItem
        {
            public int Rank { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public int SoldQuantity { get; set; }
            public decimal TotalRevenue { get; set; }
        }

        public ReportsView()
        {
            InitializeComponent();
            dpStart.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dpEnd.SelectedDate = DateTime.Today;
            Loaded += ReportsView_Loaded;
        }

        private async void ReportsView_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshReportAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshReportAsync();
        }

        private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            await RefreshReportAsync();
        }

        private async Task RefreshReportAsync()
        {
            if (dpStart == null || dpEnd == null || btnRefresh == null || gridTopProducts == null) return;

            DateTime start = dpStart.SelectedDate ?? DateTime.Today;
            DateTime end = dpEnd.SelectedDate ?? DateTime.Today;

            // Normalize dates to encompass the entire end day
            start = start.Date;
            end = end.Date.AddDays(1).AddSeconds(-1);

            btnRefresh.IsEnabled = false;

            try
            {
                decimal revenue = 0;
                int orderCount = 0;
                var topProducts = new List<TopProductItem>();

                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();

                        // 1. Get Revenue & Order Count
                        string sqlRev = @"
                            SELECT COALESCE(SUM(TotalAmount), 0), COUNT(Id) 
                            FROM Orders 
                            WHERE Status = 'Paid' AND OrderDate >= @Start AND OrderDate <= @End";
                        using (var cmd = new SqlCommand(sqlRev, conn))
                        {
                            cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
                            cmd.Parameters.AddWithValue("@End", end.ToString("o"));
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    revenue = Convert.ToDecimal(reader.GetValue(0));
                                    orderCount = Convert.ToInt32(reader.GetValue(1));
                                }
                            }
                        }

                        // 3. Get Top Products
                        string sqlTop = @"
                            SELECT TOP 15 p.Name, SUM(od.Quantity) AS SoldQty, SUM(od.Quantity * od.Price) AS TotalRev
                            FROM OrderDetails od
                            JOIN Products p ON od.ProductId = p.Id
                            JOIN Orders o ON od.OrderId = o.Id
                            WHERE o.Status = 'Paid' AND o.OrderDate >= @Start AND o.OrderDate <= @End
                            GROUP BY p.Name
                            ORDER BY SoldQty DESC";
                        using (var cmd = new SqlCommand(sqlTop, conn))
                        {
                            cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
                            cmd.Parameters.AddWithValue("@End", end.ToString("o"));
                            using (var reader = cmd.ExecuteReader())
                            {
                                int rank = 1;
                                while (reader.Read())
                                {
                                    topProducts.Add(new TopProductItem
                                    {
                                        Rank = rank++,
                                        ProductName = reader.GetString(0),
                                        SoldQuantity = Convert.ToInt32(reader.GetValue(1)),
                                        TotalRevenue = Convert.ToDecimal(reader.GetValue(2))
                                    });
                                }
                            }
                        }
                    }
                });

                // Display KPIs
                lblRevenue.Text = revenue.ToString("N0", CultureInfo.InvariantCulture) + " đ";
                lblOrderCount.Text = orderCount.ToString() + " đơn hàng";

                // Display grid
                gridTopProducts.ItemsSource = topProducts;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi lập báo cáo: {ex.Message}", "Lỗi báo cáo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRefresh.IsEnabled = true;
            }
        }
    }
}

