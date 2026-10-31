using System;
using System.IO;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using CafeManagement.Database;
using CafeManagement.Utilities;

namespace CafeManagement.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadInstalledPrinters();
            LoadCurrentSettings();
            LoadSqlConfiguration();
        }

        private void LoadSqlConfiguration()
        {
            try
            {
                string connStr = DbHelper.ConnectionString;
                DbHelper.ParseConnectionString(connStr, out string server, out string database, out bool integratedSecurity, out string userId, out string password);

                txtSqlServer.Text = server;
                txtSqlDatabase.Text = database;

                if (integratedSecurity)
                {
                    rbWindowsAuth.IsChecked = true;
                    rbSqlAuth.IsChecked = false;
                    panelSqlAuth.Visibility = Visibility.Collapsed;
                }
                else
                {
                    rbWindowsAuth.IsChecked = false;
                    rbSqlAuth.IsChecked = true;
                    panelSqlAuth.Visibility = Visibility.Visible;
                }

                txtSqlUser.Text = string.IsNullOrEmpty(userId) ? "sa" : userId;
                txtSqlPassword.Password = password;

                string localIp = DbHelper.GetLocalIPAddress();
                txtCurrentMachineIp.Text = localIp;
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi LoadSqlConfiguration: {ex.Message}\n");
            }
        }

        private void AuthMode_Changed(object sender, RoutedEventArgs e)
        {
            if (panelSqlAuth == null) return;
            panelSqlAuth.Visibility = (rbSqlAuth.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ip = txtCurrentMachineIp.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(ip))
                {
                    Clipboard.SetText(ip);
                    MessageBox.Show($"Đã sao chép IP '{ip}' vào Bộ nhớ tạm (Clipboard)!\n\nBạn có thể dán IP này vào mục 'Địa chỉ Máy chủ SQL' trên Laptop bên cạnh.", "Sao chép thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi sao chép: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnTestSqlConn_Click(object sender, RoutedEventArgs e)
        {
            string server = txtSqlServer.Text?.Trim() ?? "";
            string database = txtSqlDatabase.Text?.Trim() ?? "CafeManagement";
            bool integratedSecurity = rbWindowsAuth.IsChecked == true;
            string userId = txtSqlUser.Text?.Trim() ?? "";
            string password = txtSqlPassword.Password ?? "";

            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Vui lòng nhập Địa chỉ Máy chủ SQL (Server / IP)!", "Nhập thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string testConnStr = DbHelper.BuildConnectionString(server, database, integratedSecurity, userId, password);
            bool success = DbHelper.TestConnection(testConnStr, out string message);

            txtSqlStatus.Visibility = Visibility.Visible;
            if (success)
            {
                txtSqlStatus.Foreground = System.Windows.Media.Brushes.Green;
                txtSqlStatus.Text = $"✅ {message}";
            }
            else
            {
                txtSqlStatus.Foreground = System.Windows.Media.Brushes.Red;
                txtSqlStatus.Text = $"❌ {message}";
            }
        }

        private void BtnSaveSqlConn_Click(object sender, RoutedEventArgs e)
        {
            string server = txtSqlServer.Text?.Trim() ?? "";
            string database = txtSqlDatabase.Text?.Trim() ?? "CafeManagement";
            bool integratedSecurity = rbWindowsAuth.IsChecked == true;
            string userId = txtSqlUser.Text?.Trim() ?? "";
            string password = txtSqlPassword.Password ?? "";

            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Vui lòng nhập Địa chỉ Máy chủ SQL (Server / IP)!", "Nhập thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string newConnStr = DbHelper.BuildConnectionString(server, database, integratedSecurity, userId, password);
            bool success = DbHelper.TestConnection(newConnStr, out string message);

            DbHelper.SaveConnectionString(newConnStr);

            if (success)
            {
                MessageBox.Show("Đã lưu cấu hình kết nối SQL Server thành công!\nHệ thống sẽ kết nối đến CSDL mới ngay lập tức.", "Lưu cấu hình SQL", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Đã lưu chuỗi kết nối vào file appsettings.json!\n\nLưu ý: {message}\nHãy kiểm tra lại địa chỉ IP máy chủ hoặc SQL Server trên máy chủ.", "Lưu cấu hình SQL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadInstalledPrinters()
        {
            cmbPrinters.Items.Clear();
            cmbPrinters.Items.Add("-- Sử dụng máy in mặc định hệ thống --");

            cmbKitchenPrinters.Items.Clear();
            cmbKitchenPrinters.Items.Add("-- Sử dụng máy in mặc định hệ thống --");

            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    cmbPrinters.Items.Add(printer);
                    cmbKitchenPrinters.Items.Add(printer);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải danh sách máy in: {ex.Message}", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadCurrentSettings()
        {
            var settings = SettingsHelper.Current;

            // Select active printer in combo
            if (string.IsNullOrEmpty(settings.PrinterName))
            {
                cmbPrinters.SelectedIndex = 0;
            }
            else
            {
                int index = cmbPrinters.Items.IndexOf(settings.PrinterName);
                if (index >= 0)
                {
                    cmbPrinters.SelectedIndex = index;
                }
                else
                {
                    // If stored printer is not found in system list, append and select it
                    cmbPrinters.Items.Add(settings.PrinterName);
                    cmbPrinters.SelectedIndex = cmbPrinters.Items.Count - 1;
                }
            }

            // Select active kitchen printer in combo
            if (string.IsNullOrEmpty(settings.KitchenPrinterName))
            {
                cmbKitchenPrinters.SelectedIndex = 0;
            }
            else
            {
                int index = cmbKitchenPrinters.Items.IndexOf(settings.KitchenPrinterName);
                if (index >= 0)
                {
                    cmbKitchenPrinters.SelectedIndex = index;
                }
                else
                {
                    cmbKitchenPrinters.Items.Add(settings.KitchenPrinterName);
                    cmbKitchenPrinters.SelectedIndex = cmbKitchenPrinters.Items.Count - 1;
                }
            }

            txtTempCopies.Text = settings.TempInvoiceCopies.ToString();
            txtPaidCopies.Text = settings.PaidInvoiceCopies.ToString();
            txtKitchenCopies.Text = settings.KitchenInvoiceCopies.ToString();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validation
            if (!int.TryParse(txtTempCopies.Text?.Trim(), out int tempCopies) || tempCopies < 0)
            {
                MessageBox.Show("Số lượng bản in Tạm tính phải là số nguyên không âm (>= 0)!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtPaidCopies.Text?.Trim(), out int paidCopies) || paidCopies < 0)
            {
                MessageBox.Show("Số lượng bản in Thanh toán phải là số nguyên không âm (>= 0)!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtKitchenCopies.Text?.Trim(), out int kitchenCopies) || kitchenCopies < 0)
            {
                MessageBox.Show("Số lượng bản in Chế biến (Bếp) phải là số nguyên không âm (>= 0)!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string printerName = "";
            if (cmbPrinters.SelectedIndex > 0)
            {
                printerName = cmbPrinters.SelectedItem.ToString() ?? "";
            }

            string kitchenPrinterName = "";
            if (cmbKitchenPrinters.SelectedIndex > 0)
            {
                kitchenPrinterName = cmbKitchenPrinters.SelectedItem.ToString() ?? "";
            }

            var settings = new AppSettings
            {
                PrinterName = printerName,
                KitchenPrinterName = kitchenPrinterName,
                TempInvoiceCopies = tempCopies,
                PaidInvoiceCopies = paidCopies,
                KitchenInvoiceCopies = kitchenCopies
            };

            SettingsHelper.Save(settings);

            MessageBox.Show("Cấu hình hệ thống đã được lưu thành công!", "Cài đặt", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnUpdateAdminPassword_Click(object sender, RoutedEventArgs e)
        {
            string oldPass = txtOldAdminPassword.Password?.Trim() ?? "";
            string newPass = txtNewAdminPassword.Password?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(oldPass))
            {
                MessageBox.Show("Vui lòng nhập Mật khẩu hiện tại (cũ)!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOldAdminPassword.Focus();
                return;
            }

            if (!AdminSecurityHelper.CheckPassword(oldPass))
            {
                MessageBox.Show("Mật khẩu hiện tại (cũ) không chính xác! Vui lòng thử lại.", "Sai mật khẩu", MessageBoxButton.OK, MessageBoxImage.Error);
                txtOldAdminPassword.SelectAll();
                txtOldAdminPassword.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(newPass))
            {
                MessageBox.Show("Vui lòng nhập Mật khẩu mới cần đổi!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewAdminPassword.Focus();
                return;
            }

            if (newPass == oldPass)
            {
                MessageBox.Show("Mật khẩu mới không được trùng với mật khẩu cũ!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewAdminPassword.Focus();
                return;
            }

            AdminSecurityHelper.SetAdminPassword(newPass);
            txtOldAdminPassword.Clear();
            txtNewAdminPassword.Clear();
            MessageBox.Show("✅ Đã đổi mật khẩu quản lý mới thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnResetDb_Click(object sender, RoutedEventArgs e)
        {
            if (!AdminSecurityHelper.RequestAdminPassword("Khởi tạo lại toàn bộ CSDL"))
            {
                return;
            }

            var confirm = MessageBox.Show(
                "⚠️ BẠN CÓ CHẮC CHẮN MUỐN KHỞI TẠO LẠI CSDL KHÔNG?\n\n" +
                "• Toàn bộ hóa đơn cũ và đơn bếp sẽ được làm sạch.\n" +
                "• Hệ thống sẽ tự động tạo lại 30 Bàn trống sạch sẽ.\n" +
                "• Tự động nạp lại Thực đơn mới chuẩn từ file Excel (Không có hóa đơn mẫu).\n\n" +
                "Thao tác này phù hợp khi bạn muốn thiết lập lại dữ liệu sạch sẽ như một máy mới hoàn toàn!",
                "Xác nhận Khởi tạo lại CSDL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                btnResetDb.IsEnabled = false;
                btnResetDb.Content = "⏳ Đang khởi tạo lại CSDL...";

                await Task.Run(() =>
                {
                    DbHelper.ResetAndReinitializeDatabase();
                });

                MessageBox.Show("✅ Đã khởi tạo lại CSDL thành công!\nToàn bộ 30 Bàn trống và Thực đơn sạch sẽ đã được thiết lập sẵn sàng.",
                    "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi khởi tạo lại CSDL: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnResetDb.IsEnabled = true;
                btnResetDb.Content = "🔄 Khởi tạo lại CSDL sạch sẽ";
            }
        }
    }
}
