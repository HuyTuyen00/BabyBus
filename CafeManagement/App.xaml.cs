using System;
using System.Windows;
using CafeManagement.Database;
using CafeManagement.WebServer;

namespace CafeManagement
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool initDbSuccess = false;
            try
            {
                // Initialize Database
                DbHelper.InitializeDatabase();
                initDbSuccess = true;
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    $"Không thể kết nối đến CSDL SQL Server!\nLỗi: {ex.Message}\n\n" +
                    "Bạn có muốn mở màn hình Cấu hình Kết nối CSDL để kiểm tra Địa chỉ IP máy chủ không?",
                    "Cảnh Báo Kết Nối SQL",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    Shutdown();
                    return;
                }
            }

            try
            {
                // Start LAN Kitchen Web Server
                KitchenWebServer.Start();
            }
            catch { }

            if (!initDbSuccess)
            {
                var mainWin = new MainWindow();
                mainWin.Show();
                mainWin.ShowSettingsView();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                KitchenWebServer.Stop();
            }
            catch
            {
                // Ignore errors during exit
            }

            base.OnExit(e);
        }
    }
}


