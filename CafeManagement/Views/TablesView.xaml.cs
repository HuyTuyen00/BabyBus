using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;
using CafeManagement.Models;
using CafeManagement.WebServer;

namespace CafeManagement.Views
{
    public partial class TablesView : UserControl
    {
        private List<DiningTable> _tables = new();
        private int _selectedTableId = -1;
        private DispatcherTimer _refreshTimer;

        public TablesView()
        {
            InitializeComponent();
            Loaded += TablesView_Loaded;
            Unloaded += TablesView_Unloaded;

            // Auto-refresh every 30 seconds
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _refreshTimer.Tick += async (_, _) => await LoadTablesAsync();
            _refreshTimer.Start();
        }

        private async void TablesView_Loaded(object sender, RoutedEventArgs e)
        {
            KitchenWebServer.KitchenStatusChanged += OnKitchenStatusChanged;
            await LoadTablesAsync();
        }

        private void TablesView_Unloaded(object sender, RoutedEventArgs e)
        {
            KitchenWebServer.KitchenStatusChanged -= OnKitchenStatusChanged;
        }

        private async void OnKitchenStatusChanged()
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    await LoadTablesAsync();
                });
            }
            catch { }
        }

        // ── Data ─────────────────────────────────────────────────────────────

        private async Task LoadTablesAsync()
        {
            try
            {
                var tables = await Task.Run(FetchTablesFromDb);
                _tables = tables;
                RenderTableGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải bàn: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<DiningTable> FetchTablesFromDb()
        {
            var result = new List<DiningTable>();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();

            // Load tables with latest pending order info including total amount
            const string sql = @"
                SELECT t.Id, t.Name, 
                       CASE 
                           WHEN o.Id IS NOT NULL THEN 'Occupied' 
                           ELSE 'Empty' 
                       END AS Status, 
                       t.SortOrder,
                       o.OrderDate, ISNULL(o.TotalAmount, 0) AS TotalAmount,
                       STRING_AGG(CONCAT(p.Name, ' x', od.Quantity), '|') AS Items
                FROM   Tables t
                LEFT JOIN Orders o ON o.TableId = t.Id AND o.Status = 'Pending'
                LEFT JOIN OrderDetails od ON od.OrderId = o.Id
                LEFT JOIN Products p ON p.Id = od.ProductId
                GROUP BY t.Id, t.Name, o.Id, t.SortOrder, o.OrderDate, o.TotalAmount
                ORDER BY t.SortOrder, t.Name";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var table = new DiningTable
                {
                    Id        = Convert.ToInt32(reader.GetValue(0)),
                    Name      = reader.GetString(1),
                    Status    = reader.GetString(2),
                    SortOrder = Convert.ToInt32(reader.GetValue(3))
                };

                if (!reader.IsDBNull(4))
                    table.OccupiedSince = DateTime.Parse(reader.GetString(4));

                if (!reader.IsDBNull(5))
                    table.TotalAmount = Convert.ToDecimal(reader.GetValue(5));

                if (!reader.IsDBNull(6))
                {
                    var items = reader.GetString(6);
                    table.CurrentItems = items.Split("|").ToList();
                }

                result.Add(table);
            }
            return result;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void RenderTableGrid()
        {
            panelTables.Children.Clear();

            foreach (var table in _tables)
            {
                panelTables.Children.Add(CreateTableCard(table));
            }

            // Update selected table label
            UpdateSelectionLabel();
        }

        private Border CreateTableCard(DiningTable table)
        {
            bool occupied = table.IsOccupied;

            var bgBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };

            if (occupied)
            {
                bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(185, 28, 28), 0.0));
                bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(127, 29, 29), 1.0));
            }
            else
            {
                bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 41, 59), 0.0));
                bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(15, 23, 42), 1.0));
            }

            var borderBrush = new SolidColorBrush(occupied
                ? Color.FromRgb(248, 113, 113)
                : Color.FromRgb(71, 85, 105));

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Watermark icon
            var watermarkIcon = new TextBlock
            {
                Text = occupied ? "\ud83d\udd25" : "\u2615",
                FontSize = occupied ? 40 : 48,
                Opacity = occupied ? 0.12 : 0.06,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 4),
                IsHitTestVisible = false
            };
            Grid.SetRowSpan(watermarkIcon, 2);
            grid.Children.Add(watermarkIcon);

            // ROW 0: Header
            var headerPanel = new Grid { Margin = new Thickness(14, 12, 14, 0) };
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = table.Name,
                FontSize = 16,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameBlock, 0);
            headerPanel.Children.Add(nameBlock);

            if (occupied && !string.IsNullOrEmpty(table.ElapsedText))
            {
                var timeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                timeBorder.Child = new TextBlock
                {
                    Text = table.ElapsedText,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(254, 226, 226))
                };
                Grid.SetColumn(timeBorder, 1);
                headerPanel.Children.Add(timeBorder);
            }
            else if (!occupied)
            {
                var statusBlock = new TextBlock
                {
                    Text = "Trong",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(statusBlock, 1);
                headerPanel.Children.Add(statusBlock);
            }

            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            // ROW 1: Items preview (max 3 lines)
            if (occupied && table.CurrentItems.Any())
            {
                var itemsPanel = new StackPanel { Margin = new Thickness(14, 6, 14, 8) };
                int maxPreview = 3;
                foreach (var item in table.CurrentItems.Take(maxPreview))
                {
                    var itemText = new TextBlock
                    {
                        Text = "\u2022 " + item.Trim(),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    itemsPanel.Children.Add(itemText);
                }

                if (table.CurrentItems.Count > maxPreview)
                {
                    itemsPanel.Children.Add(new TextBlock
                    {
                        Text = $"  + {table.CurrentItems.Count - maxPreview} mon khac...",
                        FontSize = 9,
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }

                Grid.SetRow(itemsPanel, 1);
                grid.Children.Add(itemsPanel);
            }

            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 315,
                ShadowDepth = 3,
                Opacity = 0.3,
                BlurRadius = 6
            };

            // Uniform card size for all tables
            var card = new Border
            {
                Width = 160,
                Height = 135,
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(14),
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1.5),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = shadow,
                Child = grid,
                Tag = table.Id
            };

            if (table.Id == _selectedTableId)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                card.BorderThickness = new Thickness(3);
                shadow.BlurRadius = 10;
                shadow.Opacity = 0.5;
            }

            card.MouseLeftButtonUp += TableCard_Click;
            return card;
        }

        private void TableCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border card && card.Tag is int id)
            {
                if (_selectedTableId == id)
                {
                    // Second click on same table => show detail popup
                    ShowTableDetailPopup(id);
                }
                else
                {
                    _selectedTableId = id;
                    RenderTableGrid();
                }
            }
        }

        private void ShowTableDetailPopup(int tableId)
        {
            var table = _tables.FirstOrDefault(t => t.Id == tableId);
            if (table == null) return;

            var win = new Window
            {
                Title = $"Chi tiet {table.Name}",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var mainPanel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            // Header row: Name + Time badge
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = table.Name,
                FontSize = 20,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            if (table.IsOccupied && !string.IsNullOrEmpty(table.ElapsedText))
            {
                var timeBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 4, 10, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                timeBadge.Child = new TextBlock
                {
                    Text = table.ElapsedText,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                Grid.SetColumn(timeBadge, 1);
                headerGrid.Children.Add(timeBadge);
            }

            mainPanel.Children.Add(headerGrid);

            // Status
            var statusColor = table.IsOccupied
                ? Color.FromRgb(248, 113, 113)
                : Color.FromRgb(74, 222, 128);
            mainPanel.Children.Add(new TextBlock
            {
                Text = table.IsOccupied ? "Dang co khach" : "Trong",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(statusColor),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Separator
            mainPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            if (table.IsOccupied && table.CurrentItems.Any())
            {
                // Section title
                mainPanel.Children.Add(new TextBlock
                {
                    Text = $"Danh sach mon ({table.CurrentItems.Count})",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    Margin = new Thickness(0, 0, 0, 8)
                });

                // Items list (scrollable)
                var itemsScroll = new ScrollViewer
                {
                    MaxHeight = 320,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                var itemsList = new StackPanel();

                int idx = 0;
                foreach (var item in table.CurrentItems)
                {
                    idx++;
                    var rowBg = idx % 2 == 0
                        ? new SolidColorBrush(Color.FromArgb(15, 255, 255, 255))
                        : new SolidColorBrush(Color.FromArgb(6, 255, 255, 255));

                    var rowBorder = new Border
                    {
                        Background = rowBg,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 2, 0, 2)
                    };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Index number with accent background
                    var numBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(30, 56, 189, 248)),
                        CornerRadius = new CornerRadius(5),
                        Width = 26,
                        Height = 22,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    numBorder.Child = new TextBlock
                    {
                        Text = idx.ToString(),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(numBorder, 0);
                    rowGrid.Children.Add(numBorder);

                    var nameText = new TextBlock
                    {
                        Text = item.Trim(),
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameText, 1);
                    rowGrid.Children.Add(nameText);

                    rowBorder.Child = rowGrid;
                    itemsList.Children.Add(rowBorder);
                }

                itemsScroll.Content = itemsList;
                mainPanel.Children.Add(itemsScroll);
            }
            else
            {
                mainPanel.Children.Add(new TextBlock
                {
                    Text = "Ban dang trong, chua co don hang nao.",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            win.Content = mainPanel;
            win.ShowDialog();
        }

        private void UpdateSelectionLabel()
        {
            if (_selectedTableId < 0)
            {
                lblSelectedTable.Text = "(Chưa chọn bàn nào)";
                btnFreeTable.IsEnabled  = false;
                btnDeleteTable.IsEnabled = false;
                return;
            }

            var t = _tables.FirstOrDefault(x => x.Id == _selectedTableId);
            if (t == null)
            {
                lblSelectedTable.Text = "(Chưa chọn bàn nào)";
                btnFreeTable.IsEnabled  = false;
                btnDeleteTable.IsEnabled = false;
                return;
            }

            lblSelectedTable.Text = $"{t.Name}  —  {(t.IsOccupied ? "Đang có khách" : "Trống")}";
            btnFreeTable.IsEnabled  = t.IsOccupied;
            btnDeleteTable.IsEnabled = !t.IsOccupied; // only allow delete if empty
        }

        // ── Button Handlers ───────────────────────────────────────────────────

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTablesAsync();
        }

        private async void BtnAddTable_Click(object sender, RoutedEventArgs e)
        {
            string name = txtNewTableName.Text.Trim();
            if (string.IsNullOrEmpty(name) || name == "Tên bàn (VD: Bàn 7, VIP...)")
            {
                MessageBox.Show("Nhập tên bàn trước!", "Thông báo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAddTable.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var cmd = new SqlCommand(
                        "INSERT INTO Tables (Name, Status, SortOrder) VALUES (@n,'Empty',(SELECT ISNULL(MAX(SortOrder),0)+1 FROM Tables))",
                        conn);
                    cmd.Parameters.AddWithValue("@n", name);
                    cmd.ExecuteNonQuery();
                });

                txtNewTableName.Text = "Tên bàn (VD: Bàn 7, VIP...)";
                txtNewTableName.Foreground = Brushes.Gray;
                await LoadTablesAsync();
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("UNIQUE")
                    ? $"Bàn '{name}' đã tồn tại!"
                    : ex.Message;
                MessageBox.Show(msg, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnAddTable.IsEnabled = true;
            }
        }

        private async void BtnFreeTable_Click(object sender, RoutedEventArgs e)
        {
            var t = _tables.FirstOrDefault(x => x.Id == _selectedTableId);
            if (t == null) return;

            var confirm = MessageBox.Show(
                $"Đánh dấu '{t.Name}' là Trống?\n(Dùng khi khách đã về)",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            btnFreeTable.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var cmd = new SqlCommand(
                        "UPDATE Tables SET Status='Empty' WHERE Id=@id", conn);
                    cmd.Parameters.AddWithValue("@id", _selectedTableId);
                    cmd.ExecuteNonQuery();
                });

                _selectedTableId = -1;
                await LoadTablesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnFreeTable.IsEnabled = true;
            }
        }

        private async void BtnDeleteTable_Click(object sender, RoutedEventArgs e)
        {
            var t = _tables.FirstOrDefault(x => x.Id == _selectedTableId);
            if (t == null) return;

            var confirm = MessageBox.Show(
                $"Xóa bàn '{t.Name}'?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            btnDeleteTable.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var cmd = new SqlCommand("DELETE FROM Tables WHERE Id=@id", conn);
                    cmd.Parameters.AddWithValue("@id", _selectedTableId);
                    cmd.ExecuteNonQuery();
                });

                _selectedTableId = -1;
                await LoadTablesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDeleteTable.IsEnabled = true;
            }
        }

        // ── TextBox placeholder ───────────────────────────────────────────────

        private void TxtNewTableName_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtNewTableName.Text == "Tên bàn (VD: Bàn 7, VIP...)")
            {
                txtNewTableName.Text = "";
                txtNewTableName.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void TxtNewTableName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNewTableName.Text))
            {
                txtNewTableName.Text = "Tên bàn (VD: Bàn 7, VIP...)";
                txtNewTableName.Foreground = Brushes.Gray;
            }
        }
    }
}
