using System.Windows;
using System.Windows.Controls;
using CafeManagement.WebServer;
using CafeManagement.Views;

namespace CafeManagement
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SalesView? _salesView;
        private TablesView? _tablesView;
        private KitchenView? _kitchenView;
        private ProductsView? _productsView;
        private OrdersView? _ordersView;
        private ReportsView? _reportsView;
        private SettingsView? _settingsView;
        private bool _isSidebarCollapsed = false;

        public MainWindow()
        {
            InitializeComponent();
            CafeManagement.Utilities.SettingsHelper.Load();
            Loaded += MainWindow_Loaded;
            Closed += (s, e) => _sseCts?.Cancel();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Display LAN Kitchen server IP address URL
            txtServerUrl.Text = KitchenWebServer.ServerUrl;
            lblServerUrlHeader.Text = KitchenWebServer.ServerUrl;

            // Load SalesView by default
            ShowSalesView();

            // Start LAN real-time sync listener
            StartSseListener();
        }

        private void BtnSales_Click(object sender, RoutedEventArgs e)
        {
            ShowSalesView();
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Handled) return;
            if (contentArea.Content is SalesView salesView)
            {
                if (e.Key == System.Windows.Input.Key.F2 ||
                    e.Key == System.Windows.Input.Key.F3 ||
                    e.Key == System.Windows.Input.Key.F7 ||
                    e.Key == System.Windows.Input.Key.F8 ||
                    e.Key == System.Windows.Input.Key.F9)
                {
                    salesView.HandleGlobalKeyDown(e);
                }
            }
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (_isSidebarCollapsed)
            {
                colSidebar.Width = new GridLength(230);
                _isSidebarCollapsed = false;
            }
            else
            {
                colSidebar.Width = new GridLength(0);
                _isSidebarCollapsed = true;
            }
        }

        private void BtnKitchen_Click(object sender, RoutedEventArgs e)
        {
            if (_kitchenView == null) _kitchenView = new KitchenView();
            contentArea.Content = _kitchenView;
            SetActiveButton(btnKitchen);
        }

        private void BtnTables_Click(object sender, RoutedEventArgs e)
        {
            // Always recreate to refresh table data
            _tablesView = new TablesView();
            contentArea.Content = _tablesView;
            SetActiveButton(btnTables);
        }

        private void BtnProducts_Click(object sender, RoutedEventArgs e)
        {
            if (_productsView == null)
            {
                _productsView = new ProductsView();
            }
            contentArea.Content = _productsView;
            SetActiveButton(btnProducts);
        }

        private void BtnOrders_Click(object sender, RoutedEventArgs e)
        {
            if (_ordersView == null)
            {
                _ordersView = new OrdersView();
            }
            contentArea.Content = _ordersView;
            SetActiveButton(btnOrders);
        }

        private void BtnReports_Click(object sender, RoutedEventArgs e)
        {
            if (_reportsView == null)
            {
                _reportsView = new ReportsView();
            }
            contentArea.Content = _reportsView;
            SetActiveButton(btnReports);
        }

        public void ShowSettingsView()
        {
            if (_settingsView == null)
            {
                _settingsView = new SettingsView();
            }
            contentArea.Content = _settingsView;
            SetActiveButton(btnSettings);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsView();
        }

        private void ShowSalesView()
        {
            if (_salesView == null)
            {
                _salesView = new SalesView();
            }
            contentArea.Content = _salesView;
            SetActiveButton(btnSales);
        }

        private void SetActiveButton(Button activeButton)
        {
            // Reset background of all sidebar buttons
            var buttons = new[] { btnSales, btnTables, btnKitchen, btnProducts, btnOrders, btnReports, btnSettings };
            foreach (var btn in buttons)
            {
                if (btn != null)
                {
                    btn.Background = System.Windows.Media.Brushes.Transparent;
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // gray
                }
            }

            // Set active color
            if (activeButton != null)
            {
                activeButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59)); // Slate Medium Dark
                activeButton.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private static System.Net.Http.HttpClient? _sseHttpClient;
        private static System.Threading.CancellationTokenSource? _sseCts;

        private void StartSseListener()
        {
            _sseCts = new System.Threading.CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_sseCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        _sseHttpClient = new System.Net.Http.HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                        // Connect to local kitchen Events API to synchronize LAN updates
                        using var stream = await _sseHttpClient.GetStreamAsync("http://127.0.0.1:5000/api/kitchen/events", _sseCts.Token);
                        using var reader = new System.IO.StreamReader(stream);
                        while (!reader.EndOfStream && !_sseCts.Token.IsCancellationRequested)
                        {
                            string? line = await reader.ReadLineAsync();
                            if (line != null && line.StartsWith("event:"))
                            {
                                string eventName = line.Replace("event:", "").Trim();
                                string? dataLine = await reader.ReadLineAsync();
                                if (dataLine != null && dataLine.StartsWith("data:"))
                                {
                                    // Trigger local StatusChanged UI refresh across all views without re-broadcasting SSE
                                    App.Current?.Dispatcher.Invoke(() =>
                                    {
                                        KitchenWebServer.RaiseKitchenStatusChangedLocally();
                                    });
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Wait and reconnect
                        await Task.Delay(3000);
                    }
                }
            });
        }
    }
}