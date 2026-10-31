using System;
using System.IO;
using System.Windows;
using CafeManagement.Views;

namespace CafeManagement.Utilities
{
    public static class AdminSecurityHelper
    {
        private static readonly string PasswordFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "admin_password.txt");
        private const string DefaultPassword = "huytuyen00";

        public static string GetAdminPassword()
        {
            try
            {
                if (!File.Exists(PasswordFilePath))
                {
                    File.WriteAllText(PasswordFilePath, DefaultPassword);
                    return DefaultPassword;
                }

                string pass = File.ReadAllText(PasswordFilePath).Trim();
                if (string.IsNullOrEmpty(pass))
                {
                    File.WriteAllText(PasswordFilePath, DefaultPassword);
                    return DefaultPassword;
                }
                return pass;
            }
            catch
            {
                return DefaultPassword;
            }
        }

        public static void SetAdminPassword(string newPassword)
        {
            try
            {
                string pass = string.IsNullOrWhiteSpace(newPassword) ? DefaultPassword : newPassword.Trim();
                File.WriteAllText(PasswordFilePath, pass);
            }
            catch { }
        }

        public static bool CheckPassword(string inputPassword)
        {
            if (string.IsNullOrEmpty(inputPassword)) return false;
            return string.Equals(inputPassword.Trim(), GetAdminPassword(), StringComparison.Ordinal);
        }

        public static bool RequestAdminPassword(string actionName = "thực hiện thao tác này")
        {
            // Ensure password file exists
            GetAdminPassword();

            var dialog = new AdminPasswordDialog(actionName);
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialog.Owner = Application.Current.MainWindow;
            }
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            bool? result = dialog.ShowDialog();
            return result == true;
        }
    }
}
