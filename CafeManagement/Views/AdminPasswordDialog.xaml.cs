using System.Windows;
using System.Windows.Input;
using CafeManagement.Utilities;

namespace CafeManagement.Views
{
    public partial class AdminPasswordDialog : Window
    {
        public AdminPasswordDialog(string actionName)
        {
            InitializeComponent();
            txtActionDescription.Text = $"Vui lòng nhập mật khẩu quản lý để {actionName}:";
            Loaded += (s, e) => txtPassword.Focus();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndSubmit();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateAndSubmit();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else
            {
                txtError.Visibility = Visibility.Collapsed;
            }
        }

        private void ValidateAndSubmit()
        {
            string entered = txtPassword.Password;
            if (AdminSecurityHelper.CheckPassword(entered))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Visibility = Visibility.Visible;
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
        }
    }
}
