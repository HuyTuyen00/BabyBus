using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;
using CafeManagement.Models;
using CafeManagement.WebServer;

namespace CafeManagement.Views
{
    public partial class SalesView : UserControl
    {
        private List<Product> _allProducts = new();
        private List<Category> _categories = new();
        private List<DiningTable> _tables = new();
        private int _selectedCategoryId = 0;
        private int? _selectedTableId = null;
        private int? _lastSelectedTableId = null;
        private bool _isLoadingCart = false;
        private readonly Dictionary<int, List<OrderDetail>> _tempCarts = new();
        private DateTime _lastClickTime = DateTime.MinValue;

        private ObservableCollection<OrderDetail> _cart = new();

        public SalesView()
        {
            InitializeComponent();
            gridCart.ItemsSource = _cart;
            Loaded += SalesView_Loaded;
            Unloaded += SalesView_Unloaded;
            PreviewKeyDown += SalesView_PreviewKeyDown;
        }

        private void SalesView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F2)
            {
                tabTables.IsChecked = true;
                txtTableSearch.Focus();
                txtTableSearch.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F3)
            {
                tabMenu.IsChecked = true;
                txtSearch.Focus();
                txtSearch.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F7)
            {
                BtnTempPrint_Click(btnTempPrint, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F8)
            {
                BtnNotify_Click(btnNotify, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F9)
            {
                BtnPay_Click(btnPay, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        public void HandleGlobalKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Handled) return;
            SalesView_PreviewKeyDown(this, e);
        }

        private bool _isDataLoaded = false;

        private async void SalesView_Loaded(object sender, RoutedEventArgs e)
        {
            KitchenWebServer.KitchenStatusChanged -= OnKitchenStatusChanged;
            KitchenWebServer.KitchenStatusChanged += OnKitchenStatusChanged;
            // Always refresh all data (categories, products with images, table status)
            // when navigating back to SalesView so changes from Products/Tables are immediately visible.
            await LoadDataAsync();
            if (_selectedTableId.HasValue)
            {
                LoadCartForTable(_selectedTableId.Value);
            }
        }

        private void SalesView_Unloaded(object sender, RoutedEventArgs e)
        {
            KitchenWebServer.KitchenStatusChanged -= OnKitchenStatusChanged;
        }

        private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);
        private bool _isRefreshPending = false;
        private long _activeTableSessionId = 0;

        private async void OnKitchenStatusChanged()
        {
            if (_isRefreshPending) return;
            _isRefreshPending = true;

            try
            {
                await Task.Delay(250); // Debounce peak hour bursts (250ms relief for i7 2nd-gen CPU)
                if (!await _refreshLock.WaitAsync(0)) return;

                try
                {
                    var targetTableId = _selectedTableId;
                    var freshTables = await Task.Run(() => FetchTablesFromDb());
                    List<OrderDetail>? freshCartItems = null;
                    if (targetTableId.HasValue)
                    {
                        freshCartItems = await Task.Run(() => FetchUnpaidOrderForTable(targetTableId.Value));
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _tables.Clear();
                        _tables.AddRange(freshTables);

                        PopulateTableCombo();

                        // Only re-render tables grid if user is on the Tables tab
                        if (gridTablesPanel?.Visibility == Visibility.Visible)
                        {
                            FilterAndRenderTables();
                        }

                        // CRITICAL: Only sync cart IF user is STILL on the same table that was fetched!
                        // If cashier switched tables while DB query was in-flight, freshCartItems is stale and MUST be discarded!
                        if (_selectedTableId.HasValue && targetTableId.HasValue &&
                            _selectedTableId.Value == targetTableId.Value && freshCartItems != null)
                        {
                            SelectTableInComboWithoutTrigger(_selectedTableId.Value);
                            SyncCartWithFreshItems(freshCartItems);
                        }
                    });
                }
                finally
                {
                    _refreshLock.Release();
                }
            }
            catch { }
            finally
            {
                _isRefreshPending = false;
            }
        }

        private static List<OrderDetail> FetchUnpaidOrderForTable(int tableId)
        {
            var list = new List<OrderDetail>();
            try
            {
                using var conn = new SqlConnection(DbHelper.ConnectionString);
                conn.Open();

                const string sqlOrder = "SELECT TOP 1 Id FROM Orders WHERE TableId=@tid AND Status='Pending' ORDER BY Id DESC";
                using var cmdOrder = new SqlCommand(sqlOrder, conn);
                cmdOrder.Parameters.AddWithValue("@tid", tableId);
                var orderIdObj = cmdOrder.ExecuteScalar();
                if (orderIdObj != null)
                {
                    long orderId = Convert.ToInt64(orderIdObj);

                    const string sqlDetails = @"
                        SELECT d.Id, d.OrderId, d.ProductId, d.Quantity, d.Price, d.Note, p.Name,
                               CASE 
                                   WHEN p.IsExcludeFromKitchen = 1 THEN 'Served' 
                                   ELSE ISNULL((SELECT TOP 1 k.Status FROM KitchenQueue k WHERE k.OrderDetailId = d.Id ORDER BY k.Id DESC), 
                                        ISNULL((SELECT TOP 1 kq.Status FROM KitchenQueue kq WHERE kq.OrderId = d.OrderId AND kq.ProductId = d.ProductId ORDER BY kq.Id DESC), 'Pending')) 
                               END AS KitchenStatus
                        FROM OrderDetails d
                        JOIN Products p ON d.ProductId = p.Id
                        WHERE d.OrderId = @oid
                        ORDER BY d.Id ASC";
                    using var cmdDetails = new SqlCommand(sqlDetails, conn);
                    cmdDetails.Parameters.AddWithValue("@oid", orderId);
                    using var reader = cmdDetails.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new OrderDetail
                        {
                            Id            = Convert.ToInt32(reader.GetValue(0)),
                            OrderId       = Convert.ToInt32(reader.GetValue(1)),
                            ProductId     = Convert.ToInt32(reader.GetValue(2)),
                            Quantity      = Convert.ToInt32(reader.GetValue(3)),
                            Price         = Convert.ToDecimal(reader.GetValue(4)),
                            Note          = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            ProductName   = reader.GetString(6),
                            KitchenStatus = reader.GetString(7)
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        private void SyncCartWithFreshItems(List<OrderDetail> freshCartItems)
        {
            // 1. Check if user is actively typing in a Note TextBox in the cart
            var focusedTb = System.Windows.Input.Keyboard.FocusedElement as TextBox;
            bool isTypingInCart = focusedTb != null && focusedTb.DataContext is OrderDetail;

            if (isTypingInCart)
            {
                // CRUCIAL: Do not disrupt UI or steal focus if cashier is currently typing a note!
                // Only update status strings in place
                var statusMap = freshCartItems.ToDictionary(i => i.Id, i => i.KitchenStatus);
                foreach (var item in _cart)
                {
                    if (item.Id > 0 && statusMap.TryGetValue(item.Id, out var newStatus))
                    {
                        if (item.KitchenStatus != newStatus)
                        {
                            item.KitchenStatus = newStatus;
                        }
                    }
                }
                return;
            }

            // 2. Preserve any local notes typed by cashier for items
            var localNotes = new Dictionary<int, string>();
            foreach (var item in _cart)
            {
                if (item.Id > 0 && !string.IsNullOrEmpty(item.Note))
                {
                    localNotes[item.Id] = item.Note;
                }
            }

            foreach (var fItem in freshCartItems)
            {
                if (localNotes.TryGetValue(fItem.Id, out var lNote) && string.IsNullOrEmpty(fItem.Note))
                {
                    fItem.Note = lNote;
                }
            }

            // 3. Check if the list of saved items in DB is identical to current saved items in _cart
            var currentSaved = _cart.Where(i => i.Id > 0).ToList();
            bool isSameStructure = currentSaved.Count == freshCartItems.Count;
            if (isSameStructure)
            {
                for (int i = 0; i < freshCartItems.Count; i++)
                {
                    if (currentSaved[i].Id != freshCartItems[i].Id ||
                        currentSaved[i].Quantity != freshCartItems[i].Quantity)
                    {
                        isSameStructure = false;
                        break;
                    }
                }
            }

            if (isSameStructure)
            {
                // In-place update only: No UI tree destruction, 0 flicker, 0 loss!
                for (int i = 0; i < freshCartItems.Count; i++)
                {
                    currentSaved[i].KitchenStatus = freshCartItems[i].KitchenStatus;
                    if (!string.IsNullOrEmpty(freshCartItems[i].Note) && string.IsNullOrEmpty(currentSaved[i].Note))
                    {
                        currentSaved[i].Note = freshCartItems[i].Note;
                    }
                }
                UpdateBillSummary();
            }
            else
            {
                // Structure changed (e.g. mobile added new items or deleted items)
                var localDrafts = _cart.Where(i => i.Id == 0).ToList();

                _cart.Clear();
                foreach (var item in freshCartItems)
                {
                    _cart.Add(item);
                }
                foreach (var draft in localDrafts)
                {
                    _cart.Add(draft);
                }

                UpdateBillSummary();
                SafeRefreshCartGrid();
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var (cats, prods, tables) = await Task.Run(() =>
                {
                    var c = FetchCategoriesFromDb();
                    var p = FetchProductsFromDb();
                    var t = FetchTablesFromDb();
                    return (c, p, t);
                });

                _categories.Clear();
                _categories.AddRange(cats);

                _allProducts.Clear();
                _allProducts.AddRange(prods);

                _tables.Clear();
                _tables.AddRange(tables);

                RenderCategoriesButtons();
                FilterAndRenderProducts();
                PopulateTableCombo();
                FilterAndRenderTables();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải dữ liệu: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static List<Category> FetchCategoriesFromDb()
        {
            var list = new List<Category>();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT Id, Name FROM Categories ORDER BY Name", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Category
                {
                    Id = Convert.ToInt32(reader.GetValue(0)),
                    Name = reader.GetString(1)
                });
            }
            return list;
        }

        private static List<Product> FetchProductsFromDb()
        {
            var list = new List<Product>();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT Id, Name, Price, CategoryId, ImagePath, IsExcludeFromKitchen FROM Products ORDER BY Name", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string? imgPath = reader.IsDBNull(4) ? null : reader.GetString(4);
                bool isExclude = reader.IsDBNull(5) ? false : reader.GetBoolean(5);

                list.Add(new Product
                {
                    Id = Convert.ToInt32(reader.GetValue(0)),
                    Name = reader.GetString(1),
                    Price = Convert.ToDecimal(reader.GetValue(2)),
                    CategoryId = Convert.ToInt32(reader.GetValue(3)),
                    ImagePath = imgPath,
                    IsExcludeFromKitchen = isExclude
                });
            }
            return list;
        }

        private static List<DiningTable> FetchTablesFromDb()
        {
            var list = new List<DiningTable>();
            using var conn = new SqlConnection(DbHelper.ConnectionString);
            conn.Open();
            string sql = @"
                SELECT 
                    t.Id, 
                    t.Name, 
                    CASE WHEN p.OrderId IS NOT NULL THEN 'Occupied' ELSE 'Empty' END AS Status,
                    ISNULL(p.TotalAmount, 0) AS TotalAmount,
                    ISNULL(p.IsPrinted, 0) AS IsPrinted,
                    p.OrderDate
                FROM Tables t
                LEFT JOIN (
                    SELECT 
                        o.TableId,
                        MAX(o.Id) AS OrderId,
                        ISNULL(SUM(d.Quantity * d.Price), 0) AS TotalAmount,
                        MAX(CAST(o.IsPrinted AS INT)) AS IsPrinted,
                        MIN(o.OrderDate) AS OrderDate
                    FROM Orders o
                    LEFT JOIN OrderDetails d ON o.Id = d.OrderId
                    WHERE o.Status = 'Pending' AND o.TableId IS NOT NULL
                    GROUP BY o.TableId
                ) p ON t.Id = p.TableId
                ORDER BY t.SortOrder, t.Name";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime? occupiedSince = null;
                if (!reader.IsDBNull(5))
                {
                    string dateStr = reader.GetString(5);
                    if (DateTime.TryParse(dateStr, out DateTime dt))
                    {
                        occupiedSince = dt;
                    }
                }

                list.Add(new DiningTable
                {
                    Id            = Convert.ToInt32(reader.GetValue(0)),
                    Name          = reader.GetString(1),
                    Status        = reader.GetString(2),
                    TotalAmount   = Convert.ToDecimal(reader.GetValue(3)),
                    IsPrinted     = Convert.ToBoolean(reader.GetValue(4)),
                    OccupiedSince = occupiedSince
                });
            }
            return list;
        }

        private void PopulateTableCombo()
        {
            var targetTableId = _selectedTableId;

            // Temporarily detach handler to avoid trigger loops during reload
            cmbTable.SelectionChanged -= CmbTable_SelectionChanged;

            try
            {
                // In-place update if count matches to avoid clearing and rebuilding ComboBox items repeatedly
                if (cmbTable.Items.Count == _tables.Count && _tables.Count > 0)
                {
                    ComboBoxItem? selectedItem = null;
                    for (int i = 0; i < _tables.Count; i++)
                    {
                        var t = _tables[i];
                        if (cmbTable.Items[i] is ComboBoxItem item)
                        {
                            item.Tag = t.Id;
                            string expectedContent = t.IsOccupied ? $"{t.Name} (Đang có khách)" : t.Name;
                            if ((string)item.Content != expectedContent)
                            {
                                item.Content = expectedContent;
                            }
                            var color = t.IsOccupied ? Color.FromRgb(239, 68, 68) : Color.FromRgb(15, 23, 42);
                            item.Foreground = new SolidColorBrush(color);

                            if (targetTableId.HasValue && t.Id == targetTableId.Value)
                            {
                                selectedItem = item;
                            }
                        }
                    }

                    if (selectedItem != null)
                    {
                        if (cmbTable.SelectedItem != selectedItem)
                            cmbTable.SelectedItem = selectedItem;
                        _selectedTableId = targetTableId;
                    }
                    else if (!targetTableId.HasValue)
                    {
                        cmbTable.SelectedIndex = -1;
                        _selectedTableId = null;
                    }
                    return;
                }

                // Full rebuild only when table count changed or first initialization
                cmbTable.Items.Clear();

                ComboBoxItem? newSelectedItem = null;
                foreach (var t in _tables)
                {
                    var item = new ComboBoxItem { Content = t.Name, Tag = t.Id };
                    if (t.IsOccupied)
                    {
                        item.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                        item.Content = t.Name + " (Đang có khách)";
                    }
                    cmbTable.Items.Add(item);

                    if (targetTableId.HasValue && t.Id == targetTableId.Value)
                    {
                        newSelectedItem = item;
                    }
                }

                if (newSelectedItem != null)
                {
                    cmbTable.SelectedItem = newSelectedItem;
                    _selectedTableId = targetTableId;
                }
                else if (!targetTableId.HasValue)
                {
                    cmbTable.SelectedIndex = -1;
                    _selectedTableId = null;
                }
            }
            finally
            {
                cmbTable.SelectionChanged += CmbTable_SelectionChanged;
            }
        }

        private void SelectTableInComboWithoutTrigger(int tableId)
        {
            _selectedTableId = tableId;
            _lastSelectedTableId = tableId;

            cmbTable.SelectionChanged -= CmbTable_SelectionChanged;
            try
            {
                foreach (ComboBoxItem cItem in cmbTable.Items)
                {
                    if (cItem.Tag is int id && id == tableId)
                    {
                        cmbTable.SelectedItem = cItem;
                        return;
                    }
                }
            }
            finally
            {
                cmbTable.SelectionChanged += CmbTable_SelectionChanged;
            }
        }

        private void SaveCurrentCartToTemp(int? tableId)
        {
            if (_isLoadingCart) return; // Prevent overwriting cache with transient empty cart state during active loads

            int key = tableId ?? 0;
            _tempCarts[key] = _cart.Select(item => new OrderDetail
            {
                Id = item.Id,
                OrderId = item.OrderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Price = item.Price,
                Note = item.Note,
                ProductName = item.ProductName,
                KitchenStatus = item.KitchenStatus
            }).ToList();
        }

        private void LoadCartForTable(int? tableId, long? sessionId = null)
        {
            if (!sessionId.HasValue)
            {
                sessionId = Interlocked.Increment(ref _activeTableSessionId);
            }
            long currentSession = sessionId.Value;

            _isLoadingCart = true;
            try
            {
                // Clear cart immediately so old table data is never visible while switching
                _cart.Clear();
                UpdateBillSummary();

                if (!tableId.HasValue) return;

                if (currentSession != _activeTableSessionId || _selectedTableId != tableId)
                    return;

                // 1. Load saved items from database (unpaid Pending order)
                var items = FetchUnpaidOrderForTable(tableId.Value);

                // Double check session guard after DB query
                if (currentSession != _activeTableSessionId || _selectedTableId != tableId)
                    return;

                foreach (var item in items)
                {
                    _cart.Add(item);
                }

                // 2. Load draft items and local unsaved notes from RAM cache
                int key = tableId.Value;
                if (_tempCarts.TryGetValue(key, out var temp) && temp.Count > 0)
                {
                    var tempNoteMap = temp.Where(i => i.Id > 0 && !string.IsNullOrEmpty(i.Note))
                                          .ToDictionary(i => i.Id, i => i.Note);
                    foreach (var item in _cart)
                    {
                        if (item.Id > 0 && tempNoteMap.TryGetValue(item.Id, out var localNote))
                        {
                            item.Note = localNote;
                        }
                    }

                    foreach (var draft in temp)
                    {
                        if (draft.Id == 0)
                        {
                            _cart.Add(draft);
                        }
                    }
                }

                UpdateBillSummary();
                SafeRefreshCartGrid();
            }
            finally
            {
                _isLoadingCart = false;
            }
        }

        private void CmbTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTable.SelectedItem is ComboBoxItem item)
            {
                int? newTableId = item.Tag as int?;
                int? oldTableId = _selectedTableId;

                if (newTableId == oldTableId) return;

                long sessionId = Interlocked.Increment(ref _activeTableSessionId);

                // 1. Save draft changes of old table
                SaveCurrentCartToTemp(oldTableId);

                // 2. Switch to new table
                _selectedTableId = newTableId;
                _lastSelectedTableId = _selectedTableId;

                // 3. Load cart of the new table with session guard
                LoadCartForTable(newTableId, sessionId);
            }
            else if (cmbTable.SelectedIndex == -1)
            {
                long sessionId = Interlocked.Increment(ref _activeTableSessionId);
                SaveCurrentCartToTemp(_selectedTableId);
                _selectedTableId = null;
                _lastSelectedTableId = null;
                _cart.Clear();
                UpdateBillSummary();
            }
        }

        // ── Category & Product rendering ────────────────────────────────────

        private void RenderCategoriesButtons()
        {
            panelCategories.Children.Clear();

            var allBtn = CreateCategoryButton("Tất cả", 0);
            panelCategories.Children.Add(allBtn);

            foreach (var cat in _categories)
                panelCategories.Children.Add(CreateCategoryButton(cat.Name, cat.Id));

            HighlightSelectedCategory();
        }

        private Button CreateCategoryButton(string name, int id)
        {
            var btn = new Button
            {
                Content = name,
                Tag     = id,
                Margin  = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(14, 6, 14, 6),
                Style   = (Style)FindResource("ModernButton")
            };
            btn.Click += BtnCategory_Click;
            return btn;
        }

        private void BtnCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int catId)
            {
                _selectedCategoryId = catId;
                HighlightSelectedCategory();
                FilterAndRenderProducts();
            }
        }

        private void HighlightSelectedCategory()
        {
            var accent = (Brush)FindResource("PrimaryBrush");
            var muted  = new SolidColorBrush(Color.FromRgb(51, 65, 85));

            foreach (Button btn in panelCategories.Children)
            {
                btn.Background = btn.Tag is int id && id == _selectedCategoryId ? accent : muted;
            }
        }

        public static string RemoveVietnameseAccents(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char c in normalized)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D').ToLower().Trim();
        }

        private void FilterAndRenderProducts()
        {
            if (itemsProducts == null || txtSearch == null) return;

            string search = txtSearch.Text?.Trim() ?? "";
            bool isPlaceholder = search == "Tìm kiếm món (F3)...";
            if (isPlaceholder) search = "";

            string cleanSearch = RemoveVietnameseAccents(search);

            var filtered = _allProducts
                .Where(p => (_selectedCategoryId == 0 || p.CategoryId == _selectedCategoryId)
                         && (string.IsNullOrEmpty(cleanSearch) || 
                             p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                             RemoveVietnameseAccents(p.Name).Contains(cleanSearch, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            itemsProducts.ItemsSource = filtered;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string search = txtSearch.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(search) || search == "Tìm kiếm món (F3)...") return;

                string cleanSearch = RemoveVietnameseAccents(search);
                var matching = _allProducts.Where(p => 
                    (_selectedCategoryId == 0 || p.CategoryId == _selectedCategoryId) &&
                    (p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     RemoveVietnameseAccents(p.Name).Contains(cleanSearch, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (matching.Count > 0)
                {
                    // Tự động chọn ngay gợi ý xuất hiện đầu tiên!
                    AddProductToCart(matching[0]);
                    txtSearch.Text = "";
                    FilterAndRenderProducts();
                }
            }
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearch.Text == "Tìm kiếm món (F3)...")
            {
                txtSearch.Text = "";
                txtSearch.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.Text = "Tìm kiếm món (F3)...";
                txtSearch.Foreground = Brushes.Gray;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => FilterAndRenderProducts();

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
            => FilterAndRenderProducts();

        // ── Cart ────────────────────────────────────────────────────────────

        private void AddProductToCart(Product product)
        {
            if (_isLoadingCart || product == null) return;
            var existing = _cart.FirstOrDefault(i => i.ProductId == product.Id && (i.KitchenStatus == "Draft" || i.Id == 0));
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                _cart.Add(new OrderDetail
                {
                    ProductId   = product.Id,
                    ProductName = product.Name,
                    Price       = product.Price,
                    Quantity    = 1,
                    Note        = "",
                    KitchenStatus = "Draft"
                });
            }
            UpdateBillSummary();
            SaveCurrentCartToTemp(_selectedTableId);
            SafeRefreshCartGrid();
        }

        private void BtnProductCard_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingCart) return;
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 150) return;
            _lastClickTime = DateTime.Now;

            if (sender is Button btn && btn.Tag is int prodId)
            {
                var product = _allProducts.FirstOrDefault(p => p.Id == prodId);
                if (product != null)
                {
                    AddProductToCart(product);
                }
            }
        }

        private void BtnIncreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingCart) return;
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 150) return;
            _lastClickTime = DateTime.Now;

            if (sender is Button btn && btn.DataContext is OrderDetail item)
            {
                if (item.KitchenStatus == "Completed" || item.KitchenStatus == "Served")
                {
                    // Đồ đã chế biến xong: Bấm (+) sẽ tạo dòng mới (Draft) riêng biệt!
                    var product = _allProducts.FirstOrDefault(p => p.Id == item.ProductId);
                    if (product != null)
                    {
                        AddProductToCart(product);
                    }
                    return;
                }

                item.Quantity++;
                if (item.Id > 0)
                {
                    try
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();
                        using var cmd = new SqlCommand(@"
                            UPDATE OrderDetails SET Quantity = @qty WHERE Id = @did;
                            UPDATE KitchenQueue 
                            SET Quantity = @qty,
                                Status = CASE 
                                    WHEN ISNULL(PrintedQuantity, 0) >= @qty THEN 'Served' 
                                    WHEN ISNULL(CompletedQuantity, 0) > 0 THEN 'Completed' 
                                    ELSE 'Pending' 
                                END 
                            WHERE OrderDetailId = @did;
                            UPDATE Orders SET TotalAmount = ISNULL((SELECT SUM(Quantity * Price) FROM OrderDetails WHERE OrderId = @oid), 0) WHERE Id = @oid;", conn);
                        cmd.Parameters.AddWithValue("@qty", item.Quantity);
                        cmd.Parameters.AddWithValue("@did", item.Id);
                        cmd.Parameters.AddWithValue("@oid", item.OrderId);
                        cmd.ExecuteNonQuery();
                        _ = SseManager.BroadcastAsync("update", "kitchen_update");
                        KitchenWebServer.NotifyKitchenStatusChanged();
                    }
                    catch { }
                }
                UpdateBillSummary();
                SaveCurrentCartToTemp(_selectedTableId);
                SafeRefreshCartGrid();
            }
        }

        private void BtnDecreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingCart) return;
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 150) return;
            _lastClickTime = DateTime.Now;

            if (sender is Button btn && btn.DataContext is OrderDetail item)
            {
                if (item.KitchenStatus == "Completed" || item.KitchenStatus == "Served")
                {
                    var confirm = MessageBox.Show($"Món [{item.ProductName}] đã được bếp chế biến xong.\nBạn có chắc chắn muốn giảm số lượng không?", 
                                                  "Xác nhận giảm món đã xong", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }

                if (item.Quantity > 1)
                {
                    item.Quantity--;
                    if (item.Id > 0)
                    {
                        try
                        {
                            using var conn = new SqlConnection(DbHelper.ConnectionString);
                            conn.Open();
                            using var cmd = new SqlCommand(@"
                                UPDATE OrderDetails SET Quantity = @qty WHERE Id = @did;
                                UPDATE KitchenQueue 
                                SET Quantity = @qty,
                                    CompletedQuantity = CASE WHEN CompletedQuantity > @qty THEN @qty ELSE CompletedQuantity END,
                                    PrintedQuantity = CASE WHEN PrintedQuantity > @qty THEN @qty ELSE PrintedQuantity END,
                                    Status = CASE 
                                        WHEN ISNULL(PrintedQuantity, 0) >= @qty THEN 'Served' 
                                        WHEN ISNULL(CompletedQuantity, 0) > 0 THEN 'Completed' 
                                        ELSE 'Pending' 
                                    END 
                                WHERE OrderDetailId = @did;
                                UPDATE Orders SET TotalAmount = ISNULL((SELECT SUM(Quantity * Price) FROM OrderDetails WHERE OrderId = @oid), 0) WHERE Id = @oid;", conn);
                            cmd.Parameters.AddWithValue("@qty", item.Quantity);
                            cmd.Parameters.AddWithValue("@did", item.Id);
                            cmd.Parameters.AddWithValue("@oid", item.OrderId);
                            cmd.ExecuteNonQuery();
                            _ = SseManager.BroadcastAsync("update", "kitchen_update");
                            KitchenWebServer.NotifyKitchenStatusChanged();
                        }
                        catch { }
                    }
                    UpdateBillSummary();
                    SaveCurrentCartToTemp(_selectedTableId);
                    SafeRefreshCartGrid();
                }
                else
                {
                    BtnRemoveCartItem_Click(sender, e);
                }
            }
        }

        private void BtnRemoveCartItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OrderDetail item)
            {
                if (item.KitchenStatus == "Completed" || item.KitchenStatus == "Served")
                {
                    var confirm = MessageBox.Show($"Món [{item.ProductName}] đã được bếp chế biến xong.\nBạn có thực sự muốn xóa/hủy món này không?", 
                                                  "Xác nhận xóa món", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }

                // If saved in DB, delete from DB & KitchenQueue
                if (item.Id > 0)
                {
                    try
                    {
                        using var conn = new SqlConnection(DbHelper.ConnectionString);
                        conn.Open();
                        using var trans = conn.BeginTransaction();
                        try
                        {
                            using (var cmdK = new SqlCommand("DELETE FROM KitchenQueue WHERE OrderDetailId = @did", conn, trans))
                            {
                                cmdK.Parameters.AddWithValue("@did", item.Id);
                                cmdK.ExecuteNonQuery();
                            }

                            using (var cmdD = new SqlCommand("DELETE FROM OrderDetails WHERE Id = @did", conn, trans))
                            {
                                cmdD.Parameters.AddWithValue("@did", item.Id);
                                cmdD.ExecuteNonQuery();
                            }

                            if (item.OrderId > 0)
                            {
                                using (var cmdCount = new SqlCommand("SELECT COUNT(1) FROM OrderDetails WHERE OrderId = @oid", conn, trans))
                                {
                                    cmdCount.Parameters.AddWithValue("@oid", item.OrderId);
                                    int remainingCount = Convert.ToInt32(cmdCount.ExecuteScalar());
                                    if (remainingCount == 0)
                                    {
                                        using var cmdDelOrder = new SqlCommand("DELETE FROM Orders WHERE Id = @oid", conn, trans);
                                        cmdDelOrder.Parameters.AddWithValue("@oid", item.OrderId);
                                        cmdDelOrder.ExecuteNonQuery();

                                        if (_selectedTableId.HasValue)
                                        {
                                            using var cmdEmptyTable = new SqlCommand("UPDATE Tables SET Status = 'Empty' WHERE Id = @tid", conn, trans);
                                            cmdEmptyTable.Parameters.AddWithValue("@tid", _selectedTableId.Value);
                                            cmdEmptyTable.ExecuteNonQuery();
                                        }
                                    }
                                    else
                                    {
                                        using var cmdO = new SqlCommand("UPDATE Orders SET TotalAmount = ISNULL((SELECT SUM(Quantity * Price) FROM OrderDetails WHERE OrderId = @oid), 0) WHERE Id = @oid", conn, trans);
                                        cmdO.Parameters.AddWithValue("@oid", item.OrderId);
                                        cmdO.ExecuteNonQuery();
                                    }
                                }
                            }

                            trans.Commit();
                        }
                        catch
                        {
                            trans.Rollback();
                            throw;
                        }

                        _ = SseManager.BroadcastAsync("update", "kitchen_delete");
                        KitchenWebServer.NotifyKitchenStatusChanged();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Lỗi xóa món: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                _cart.Remove(item);
                UpdateBillSummary();
                SaveCurrentCartToTemp(_selectedTableId);
                SafeRefreshCartGrid();
            }
        }

        private void TxtCartNote_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingCart) return;
            SaveCurrentCartToTemp(_selectedTableId);
        }

        private void TxtCartNote_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

        private void UpdateBillSummary()
        {
            if (lblPayable == null) return;
            decimal total = _cart.Sum(i => i.TotalPrice);
            lblPayable.Text = total.ToString("N0", CultureInfo.InvariantCulture) + " đ";
        }

        private void SafeRefreshCartGrid()
        {
            try
            {
                if (gridCart != null)
                {
                    gridCart.CommitEdit(DataGridEditingUnit.Cell, true);
                    gridCart.CommitEdit(DataGridEditingUnit.Row, true);
                    gridCart.Items.Refresh();
                }
            }
            catch
            {
                gridCart?.UpdateLayout();
            }
        }

        // ── Payment ─────────────────────────────────────────────────────────

        private async void BtnNotify_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any())
            {
                MessageBox.Show("Giỏ hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? tableId = _selectedTableId;
            var cartSnapshot = _cart.ToList();
            decimal total = _cart.Sum(i => i.TotalPrice);

            this.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var cmd = new SqlCommand("SELECT 1;", conn);
                    cmd.ExecuteNonQuery();

                    using var trans = conn.BeginTransaction();
                    try
                    {
                        long orderId = 0;
                        bool isNewOrder = true;

                        // 1. Check if table has a pending order
                        if (tableId.HasValue)
                        {
                            using var cmdCheck = new SqlCommand(
                                "SELECT TOP 1 Id FROM Orders WHERE TableId = @t AND Status = 'Pending'", conn, trans);
                            cmdCheck.Parameters.AddWithValue("@t", tableId.Value);
                            var obj = cmdCheck.ExecuteScalar();
                            if (obj != null)
                            {
                                orderId = Convert.ToInt64(obj);
                                isNewOrder = false;
                            }
                        }

                        if (isNewOrder)
                        {
                            // Create new Order with status 'Pending'
                            using var cmdOrder = new SqlCommand(@"
                                INSERT INTO Orders (OrderDate, TableId, TotalAmount, Status)
                                VALUES (@d, @t, @a, 'Pending');
                                SELECT SCOPE_IDENTITY();", conn, trans);
                            cmdOrder.Parameters.AddWithValue("@d", DateTime.Now.ToString("o"));
                            cmdOrder.Parameters.AddWithValue("@t", tableId.HasValue ? (object)tableId.Value : DBNull.Value);
                            cmdOrder.Parameters.AddWithValue("@a", (double)total);
                            orderId = Convert.ToInt64(cmdOrder.ExecuteScalar());
                        }
                        else
                        {
                            // Update total amount of existing order
                            using var cmdUpdateAmt = new SqlCommand(
                                "UPDATE Orders SET TotalAmount = @a WHERE Id = @oid", conn, trans);
                            cmdUpdateAmt.Parameters.AddWithValue("@a", (double)total);
                            cmdUpdateAmt.Parameters.AddWithValue("@oid", orderId);
                            cmdUpdateAmt.ExecuteNonQuery();
                        }

                        // 2. Process each item in cartSnapshot
                        foreach (var item in cartSnapshot)
                        {
                            if (item.Id > 0)
                            {
                                // Old item: Update details in DB
                                using var cmdUpdateDetail = new SqlCommand(@"
                                    UPDATE OrderDetails 
                                    SET Quantity = @qty, Note = @note, Price = @price 
                                    WHERE Id = @id", conn, trans);
                                cmdUpdateDetail.Parameters.AddWithValue("@qty", item.Quantity);
                                cmdUpdateDetail.Parameters.AddWithValue("@note", item.Note ?? "");
                                cmdUpdateDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdUpdateDetail.Parameters.AddWithValue("@id", item.Id);
                                cmdUpdateDetail.ExecuteNonQuery();

                                // Also update matching KitchenQueue item
                                using var cmdUpdateKitchen = new SqlCommand(@"
                                    UPDATE KitchenQueue 
                                    SET Quantity = @qty, Note = @note 
                                    WHERE OrderDetailId = @id", conn, trans);
                                cmdUpdateKitchen.Parameters.AddWithValue("@qty", item.Quantity);
                                cmdUpdateKitchen.Parameters.AddWithValue("@note", item.Note ?? "");
                                cmdUpdateKitchen.Parameters.AddWithValue("@id", item.Id);
                                cmdUpdateKitchen.ExecuteNonQuery();
                            }
                            else
                            {
                                // New item: Insert into OrderDetails
                                using var cmdInsertDetail = new SqlCommand(@"
                                    INSERT INTO OrderDetails (OrderId, ProductId, Quantity, Price, Note)
                                    VALUES (@oid, @pid, @qty, @price, @note);
                                    SELECT SCOPE_IDENTITY();", conn, trans);
                                cmdInsertDetail.Parameters.AddWithValue("@oid",   orderId);
                                cmdInsertDetail.Parameters.AddWithValue("@pid",   item.ProductId);
                                cmdInsertDetail.Parameters.AddWithValue("@qty",   item.Quantity);
                                cmdInsertDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdInsertDetail.Parameters.AddWithValue("@note",  item.Note ?? "");
                                
                                long newDetailId = Convert.ToInt64(cmdInsertDetail.ExecuteScalar());
                                item.Id = (int)newDetailId; // update in-memory object

                                var prod = _allProducts.FirstOrDefault(p => p.Id == item.ProductId);
                                bool isExclude = prod != null && prod.IsExcludeFromKitchen;

                                if (!isExclude)
                                {
                                    // Push to KitchenQueue linked to this new detail row
                                    using var cmdKitchen = new SqlCommand(@"
                                        INSERT INTO KitchenQueue (OrderId, OrderDetailId, ProductId, Quantity, Note, Status, CreatedAt)
                                        VALUES (@oid, @odid, @pid, @qty, @note, 'Pending', @at)", conn, trans);
                                    cmdKitchen.Parameters.AddWithValue("@oid",  orderId);
                                    cmdKitchen.Parameters.AddWithValue("@odid", newDetailId);
                                    cmdKitchen.Parameters.AddWithValue("@pid",  item.ProductId);
                                    cmdKitchen.Parameters.AddWithValue("@qty",  item.Quantity);
                                    cmdKitchen.Parameters.AddWithValue("@note", item.Note ?? "");
                                    cmdKitchen.Parameters.AddWithValue("@at",   DateTime.Now.ToString("o"));
                                    cmdKitchen.ExecuteNonQuery();
                                }
                            }
                        }

                        // 3. Mark table as Occupied
                        if (tableId.HasValue)
                        {
                            using var cmdTable = new SqlCommand(
                                "UPDATE Tables SET Status='Occupied' WHERE Id=@id", conn, trans);
                            cmdTable.Parameters.AddWithValue("@id", tableId.Value);
                            cmdTable.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                });

                // Broadcast to LAN kitchen screen
                await SseManager.BroadcastAsync("update", "new_order");

                _tempCarts.Remove(tableId ?? 0);
                _isDataLoaded = false;
                await LoadDataAsync();
                
                if (tableId.HasValue)
                {
                    SelectTableInComboWithoutTrigger(tableId.Value);
                    LoadCartForTable(tableId.Value);
                }
                UpdateBillSummary();

                // Reload local WPF kitchen screen and notify LAN
                KitchenWebServer.NotifyKitchenStatusChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi báo bếp: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        private async void BtnTempPrint_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any())
            {
                MessageBox.Show("Giỏ hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedTableName = cmbTable.SelectedItem is ComboBoxItem cbi ? cbi.Content.ToString()!.Split('(')[0].Trim() : "Bàn";
            var settings = CafeManagement.Utilities.SettingsHelper.Current;
            int tempCopies = settings.TempInvoiceCopies;
            var cartSnapshot = _cart.ToList();
            decimal total = _cart.Sum(i => i.TotalPrice);
            long tempOrderId = _cart.FirstOrDefault(i => i.OrderId > 0)?.OrderId ?? 0;
            int? tableId = _selectedTableId;

            this.IsEnabled = false; // Lock UI during background printing tasks
            try
            {
                // 1. Save order, notify kitchen (Báo bếp), set IsPrinted = 1 and print Temp Bill
                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();

                        // Check if table has a pending order in DB
                        if (tempOrderId <= 0 && tableId.HasValue)
                        {
                            using var cmdFind = new SqlCommand(
                                "SELECT TOP 1 Id FROM Orders WHERE TableId = @t AND Status = 'Pending' ORDER BY Id DESC", conn);
                            cmdFind.Parameters.AddWithValue("@t", tableId.Value);
                            var obj = cmdFind.ExecuteScalar();
                            if (obj != null) tempOrderId = Convert.ToInt64(obj);
                        }

                        using var trans = conn.BeginTransaction();
                        try
                        {
                            bool isNewOrder = tempOrderId <= 0;
                            if (isNewOrder)
                            {
                                // Create new Order with status 'Pending' and IsPrinted = 1
                                using var cmdOrder = new SqlCommand(@"
                                    INSERT INTO Orders (OrderDate, TableId, TotalAmount, Status, IsPrinted)
                                    VALUES (@d, @t, @a, 'Pending', 1);
                                    SELECT SCOPE_IDENTITY();", conn, trans);
                                cmdOrder.Parameters.AddWithValue("@d", DateTime.Now.ToString("o"));
                                cmdOrder.Parameters.AddWithValue("@t", tableId.HasValue ? (object)tableId.Value : DBNull.Value);
                                cmdOrder.Parameters.AddWithValue("@a", (double)total);
                                tempOrderId = Convert.ToInt64(cmdOrder.ExecuteScalar());
                            }
                            else
                            {
                                // Update total amount and IsPrinted = 1 on existing order
                                using var cmdUpdateAmt = new SqlCommand(
                                    "UPDATE Orders SET TotalAmount = @a, IsPrinted = 1 WHERE Id = @oid", conn, trans);
                                cmdUpdateAmt.Parameters.AddWithValue("@a", (double)total);
                                cmdUpdateAmt.Parameters.AddWithValue("@oid", tempOrderId);
                                cmdUpdateAmt.ExecuteNonQuery();
                            }

                            // 2. Process items into OrderDetails and KitchenQueue (Báo bếp tự động)
                            foreach (var item in cartSnapshot)
                            {
                                if (item.Id > 0)
                                {
                                    // Update existing details in DB
                                    using var cmdUpdateDetail = new SqlCommand(@"
                                        UPDATE OrderDetails 
                                        SET Quantity = @qty, Note = @note, Price = @price 
                                        WHERE Id = @id", conn, trans);
                                    cmdUpdateDetail.Parameters.AddWithValue("@qty", item.Quantity);
                                    cmdUpdateDetail.Parameters.AddWithValue("@note", item.Note ?? "");
                                    cmdUpdateDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                    cmdUpdateDetail.Parameters.AddWithValue("@id", item.Id);
                                    cmdUpdateDetail.ExecuteNonQuery();

                                    // Also update matching KitchenQueue item
                                    using var cmdUpdateKitchen = new SqlCommand(@"
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
                                        WHERE OrderDetailId = @id", conn, trans);
                                    cmdUpdateKitchen.Parameters.AddWithValue("@qty", item.Quantity);
                                    cmdUpdateKitchen.Parameters.AddWithValue("@note", item.Note ?? "");
                                    cmdUpdateKitchen.Parameters.AddWithValue("@id", item.Id);
                                    cmdUpdateKitchen.ExecuteNonQuery();
                                }
                                else
                                {
                                    // New item: Insert into OrderDetails
                                    using var cmdInsertDetail = new SqlCommand(@"
                                        INSERT INTO OrderDetails (OrderId, ProductId, Quantity, Price, Note)
                                        VALUES (@oid, @pid, @qty, @price, @note);
                                        SELECT SCOPE_IDENTITY();", conn, trans);
                                    cmdInsertDetail.Parameters.AddWithValue("@oid", tempOrderId);
                                    cmdInsertDetail.Parameters.AddWithValue("@pid", item.ProductId);
                                    cmdInsertDetail.Parameters.AddWithValue("@qty", item.Quantity);
                                    cmdInsertDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                    cmdInsertDetail.Parameters.AddWithValue("@note", item.Note ?? "");
                                    long newDetailId = Convert.ToInt64(cmdInsertDetail.ExecuteScalar());
                                    item.Id = (int)newDetailId;

                                    var prod = _allProducts.FirstOrDefault(p => p.Id == item.ProductId);
                                    bool isExclude = prod != null && prod.IsExcludeFromKitchen;

                                    if (!isExclude)
                                    {
                                        // Push to KitchenQueue linked to this new detail row
                                        using var cmdKitchen = new SqlCommand(@"
                                            INSERT INTO KitchenQueue (OrderId, OrderDetailId, ProductId, Quantity, Note, Status, CreatedAt)
                                            VALUES (@oid, @odid, @pid, @qty, @note, 'Pending', @at)", conn, trans);
                                        cmdKitchen.Parameters.AddWithValue("@oid", tempOrderId);
                                        cmdKitchen.Parameters.AddWithValue("@odid", newDetailId);
                                        cmdKitchen.Parameters.AddWithValue("@pid", item.ProductId);
                                        cmdKitchen.Parameters.AddWithValue("@qty", item.Quantity);
                                        cmdKitchen.Parameters.AddWithValue("@note", item.Note ?? "");
                                        cmdKitchen.Parameters.AddWithValue("@at", DateTime.Now.ToString("o"));
                                        cmdKitchen.ExecuteNonQuery();
                                    }
                                }
                            }

                            // 3. Mark table as Occupied
                            if (tableId.HasValue)
                            {
                                using var cmdTable = new SqlCommand(
                                    "UPDATE Tables SET Status='Occupied' WHERE Id=@id", conn, trans);
                                cmdTable.Parameters.AddWithValue("@id", tableId.Value);
                                cmdTable.ExecuteNonQuery();
                            }

                            trans.Commit();
                        }
                        catch
                        {
                            trans.Rollback();
                            throw;
                        }
                    }

                    // Print Temp Bill
                    CafeManagement.Utilities.PrinterHelper.PrintInvoice(
                        tempOrderId, selectedTableName, cartSnapshot, total, 0, "PHIẾU TẠM TÍNH", tempCopies);
                });

                // Broadcast to LAN kitchen screen & Mobile App
                await SseManager.BroadcastAsync("update", "new_order");
                _ = SseManager.BroadcastAsync("update", "temp_printed");
                KitchenWebServer.NotifyKitchenStatusChanged();

                _tempCarts.Remove(tableId ?? 0);
                _isDataLoaded = false;
                await LoadDataAsync();

                if (tableId.HasValue)
                {
                    SelectTableInComboWithoutTrigger(tableId.Value);
                    LoadCartForTable(tableId.Value);
                }
                UpdateBillSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi in tạm tính: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        private async void BtnPay_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any())
            {
                MessageBox.Show("Giỏ hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal total = _cart.Sum(i => i.TotalPrice);
            var cartSnapshot = _cart.ToList();
            int? tableId     = _selectedTableId;

            long createdOrderId = 0;
            this.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(DbHelper.ConnectionString);
                    conn.Open();
                    using var cmd = new SqlCommand("SELECT 1;", conn);
                    cmd.ExecuteNonQuery();

                    using var trans = conn.BeginTransaction();
                    try
                    {
                        long orderId = 0;
                        bool isNewOrder = true;

                        // 1. Check if table has a pending order
                        if (tableId.HasValue)
                        {
                            using var cmdCheck = new SqlCommand(
                                "SELECT TOP 1 Id FROM Orders WHERE TableId = @t AND Status = 'Pending'", conn, trans);
                            cmdCheck.Parameters.AddWithValue("@t", tableId.Value);
                            var obj = cmdCheck.ExecuteScalar();
                            if (obj != null)
                            {
                                orderId = Convert.ToInt64(obj);
                                isNewOrder = false;
                            }
                        }

                        if (isNewOrder)
                        {
                            // Create new Order directly as Paid
                            using var cmdOrder = new SqlCommand(@"
                                INSERT INTO Orders (OrderDate, TableId, TotalAmount, Status)
                                VALUES (@d, @t, @a, 'Paid');
                                SELECT SCOPE_IDENTITY();", conn, trans);
                            cmdOrder.Parameters.AddWithValue("@d", DateTime.Now.ToString("o"));
                            cmdOrder.Parameters.AddWithValue("@t", tableId.HasValue ? (object)tableId.Value : DBNull.Value);
                            cmdOrder.Parameters.AddWithValue("@a", (double)total);
                            orderId = Convert.ToInt64(cmdOrder.ExecuteScalar());
                            createdOrderId = orderId;

                            // Insert Details only (No kitchen queue push)
                            foreach (var item in cartSnapshot)
                            {
                                using var cmdDetail = new SqlCommand(@"
                                    INSERT INTO OrderDetails (OrderId, ProductId, Quantity, Price, Note)
                                    VALUES (@oid, @pid, @qty, @price, @note)", conn, trans);
                                cmdDetail.Parameters.AddWithValue("@oid",   orderId);
                                cmdDetail.Parameters.AddWithValue("@pid",   item.ProductId);
                                cmdDetail.Parameters.AddWithValue("@qty",   item.Quantity);
                                cmdDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdDetail.Parameters.AddWithValue("@note",  item.Note ?? "");
                                cmdDetail.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // Mark existing Order as Paid
                            createdOrderId = orderId;
                            using var cmdOrderUpdate = new SqlCommand(@"
                                UPDATE Orders 
                                SET Status = 'Paid', TotalAmount = @a, OrderDate = @d 
                                WHERE Id = @oid", conn, trans);
                                cmdOrderUpdate.Parameters.AddWithValue("@a", (double)total);
                            cmdOrderUpdate.Parameters.AddWithValue("@d", DateTime.Now.ToString("o"));
                            cmdOrderUpdate.Parameters.AddWithValue("@oid", orderId);
                            cmdOrderUpdate.ExecuteNonQuery();

                             // Delete all kitchen queue items for this order upon payment
                             using var cmdDelKitchen = new SqlCommand(
                                 "DELETE FROM KitchenQueue WHERE OrderId = @oid", conn, trans);
                             cmdDelKitchen.Parameters.AddWithValue("@oid", orderId);
                             cmdDelKitchen.ExecuteNonQuery();

                             // Overwrite Details only (No kitchen queue push)
                             using var cmdDelDetails = new SqlCommand(
                                 "DELETE FROM OrderDetails WHERE OrderId = @oid", conn, trans);
                             cmdDelDetails.Parameters.AddWithValue("@oid", orderId);
                             cmdDelDetails.ExecuteNonQuery();

                            foreach (var item in cartSnapshot)
                            {
                                using var cmdDetail = new SqlCommand(@"
                                    INSERT INTO OrderDetails (OrderId, ProductId, Quantity, Price, Note)
                                    VALUES (@oid, @pid, @qty, @price, @note)", conn, trans);
                                cmdDetail.Parameters.AddWithValue("@oid",   orderId);
                                cmdDetail.Parameters.AddWithValue("@pid",   item.ProductId);
                                cmdDetail.Parameters.AddWithValue("@qty",   item.Quantity);
                                cmdDetail.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdDetail.Parameters.AddWithValue("@note",  item.Note ?? "");
                                cmdDetail.ExecuteNonQuery();
                            }
                        }

                        // 3. Mark all pending/processing kitchen items for this order as Completed (to clear kitchen board)
                        using var cmdKitchenComp = new SqlCommand(
                            "UPDATE KitchenQueue SET Status = 'Completed' WHERE OrderId = @oid", conn, trans);
                        cmdKitchenComp.Parameters.AddWithValue("@oid", createdOrderId);
                        cmdKitchenComp.ExecuteNonQuery();

                        // 4. Mark table as Empty (Paid)
                        if (tableId.HasValue)
                        {
                            using var cmdTable = new SqlCommand(
                                "UPDATE Tables SET Status='Empty' WHERE Id=@id", conn, trans);
                            cmdTable.Parameters.AddWithValue("@id", tableId.Value);
                            cmdTable.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                });

                // Auto-print styled invoice in the background using Windows print manager
                string selectedTableName = cmbTable.SelectedItem is ComboBoxItem cbi ? cbi.Content.ToString()!.Split('(')[0].Trim() : "Mang đi";
                var cartSnapshotForPrint = cartSnapshot;
                decimal totalForPrint = total;
                long finalOrderId = createdOrderId;

                var settings = CafeManagement.Utilities.SettingsHelper.Current;
                int paidCopies = settings.PaidInvoiceCopies;

                Task.Run(() =>
                {
                    CafeManagement.Utilities.PrinterHelper.PrintInvoice(
                        finalOrderId, selectedTableName, cartSnapshotForPrint, totalForPrint, 0, "HÓA ĐƠN THANH TOÁN", paidCopies);
                });

                await SseManager.BroadcastAsync("update", "paid_order");

                // Reload local WPF kitchen screen
                KitchenWebServer.NotifyKitchenStatusChanged();

                _cart.Clear();
                _tempCarts.Remove(tableId ?? 0);
                _lastSelectedTableId = _selectedTableId;
                _isDataLoaded = false;
                await LoadDataAsync();
                UpdateBillSummary();

                // Return to the first page (Phòng bàn tab)
                tabTables.IsChecked = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi thanh toán: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        private static string ResolveImagePath(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath)) return "";
            
            string abs1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", dbPath);
            if (File.Exists(abs1)) return abs1;

            string abs2 = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", dbPath);
            if (File.Exists(abs2)) return abs2;

            return "";
        }

        private async void TabTables_Checked(object sender, RoutedEventArgs e)
        {
            if (gridTablesPanel != null && gridMenuPanel != null)
            {
                gridTablesPanel.Visibility = Visibility.Visible;
                gridMenuPanel.Visibility = Visibility.Collapsed;
                
                var freshTables = await Task.Run(() => FetchTablesFromDb());
                _tables.Clear();
                _tables.AddRange(freshTables);
                FilterAndRenderTables();
            }
        }

        private void TabMenu_Checked(object sender, RoutedEventArgs e)
        {
            if (gridTablesPanel != null && gridMenuPanel != null)
            {
                gridTablesPanel.Visibility = Visibility.Collapsed;
                gridMenuPanel.Visibility = Visibility.Visible;
            }
        }

        private void SelectTableAndOpen(int tableId)
        {
            long sessionId = Interlocked.Increment(ref _activeTableSessionId);

            // 1. Save previous table draft
            SaveCurrentCartToTemp(_selectedTableId);

            // 2. Update active selection immediately
            _selectedTableId = tableId;
            _lastSelectedTableId = tableId;
            SelectTableInComboWithoutTrigger(tableId);

            // 3. Switch to Menu tab immediately so UI is responsive
            tabMenu.IsChecked = true;

            // 4. Load cart for the new table with session guard
            LoadCartForTable(tableId, sessionId);

            // Reset table search box
            txtTableSearch.Text = "Tìm kiếm bàn (F2)...";
            txtTableSearch.Foreground = Brushes.Gray;

            // Focus on product search box for immediate fast typing
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtSearch?.Focus();
                if (txtSearch?.Text == "Tìm kiếm món (F3)...")
                {
                    txtSearch.Text = "";
                    txtSearch.Foreground = Brushes.Black;
                }
                else
                {
                    txtSearch?.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void BtnTableCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int tableId)
            {
                SelectTableAndOpen(tableId);
            }
        }

        private void FilterAndRenderTables()
        {
            if (itemsTablesGrid == null || txtTableSearch == null) return;

            string search = txtTableSearch.Text?.Trim() ?? "";
            bool isPlaceholder = search == "Tìm kiếm bàn (F2)...";
            if (isPlaceholder) search = "";

            string cleanSearch = RemoveVietnameseAccents(search);

            var filtered = _tables.AsEnumerable();

            // 1. Filter by search query (accented & unaccented)
            if (!string.IsNullOrEmpty(cleanSearch))
            {
                filtered = filtered.Where(t => 
                    t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    RemoveVietnameseAccents(t.Name).Contains(cleanSearch, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Filter by table status RadioButtons
            if (radTableFilterOccupied != null && radTableFilterOccupied.IsChecked == true)
            {
                filtered = filtered.Where(t => t.IsOccupied);
            }
            else if (radTableFilterEmpty != null && radTableFilterEmpty.IsChecked == true)
            {
                filtered = filtered.Where(t => !t.IsOccupied);
            }

            itemsTablesGrid.ItemsSource = null;
            itemsTablesGrid.ItemsSource = filtered.ToList();
        }

        private void TxtTableSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string search = txtTableSearch.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(search) || search == "Tìm kiếm bàn (F2)...") return;

                string cleanSearch = RemoveVietnameseAccents(search);

                // 1. Exact name match (with/without accents)
                var target = _tables.FirstOrDefault(t => 
                    t.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                    RemoveVietnameseAccents(t.Name).Equals(cleanSearch, StringComparison.OrdinalIgnoreCase));

                // 2. Match "Bàn X" if user typed just the number
                if (target == null)
                {
                    target = _tables.FirstOrDefault(t => 
                        RemoveVietnameseAccents(t.Name).Equals("ban " + cleanSearch, StringComparison.OrdinalIgnoreCase) ||
                        RemoveVietnameseAccents(t.Name).Equals("b " + cleanSearch, StringComparison.OrdinalIgnoreCase) ||
                        RemoveVietnameseAccents(t.Name).Equals(cleanSearch, StringComparison.OrdinalIgnoreCase));
                }

                // 3. Match digits from table name
                if (target == null && int.TryParse(cleanSearch, out int searchNum))
                {
                    target = _tables.FirstOrDefault(t => {
                        var match = System.Text.RegularExpressions.Regex.Match(t.Name, @"\d+");
                        return match.Success && int.TryParse(match.Value, out int tableNum) && tableNum == searchNum;
                    });
                }

                // 4. Starts with
                if (target == null)
                {
                    target = _tables.FirstOrDefault(t => 
                        RemoveVietnameseAccents(t.Name).StartsWith(cleanSearch, StringComparison.OrdinalIgnoreCase));
                }

                // 5. Contains
                if (target == null)
                {
                    target = _tables.FirstOrDefault(t => 
                        RemoveVietnameseAccents(t.Name).Contains(cleanSearch, StringComparison.OrdinalIgnoreCase));
                }

                if (target != null)
                {
                    SelectTableAndOpen(target.Id);
                }
            }
        }

        private void RadTableFilter_Checked(object sender, RoutedEventArgs e)
        {
            FilterAndRenderTables();
        }

        private void TxtTableSearch_TextChanged(object sender, TextChangedEventArgs e)
            => FilterAndRenderTables();

        private void BtnTableSearch_Click(object sender, RoutedEventArgs e)
            => FilterAndRenderTables();

        private void TxtTableSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtTableSearch.Text == "Tìm kiếm bàn (F2)...")
            {
                txtTableSearch.Text = "";
                txtTableSearch.Foreground = Brushes.Black;
            }
        }

        private void TxtTableSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTableSearch.Text))
            {
                txtTableSearch.Text = "Tìm kiếm bàn (F2)...";
                txtTableSearch.Foreground = Brushes.Gray;
            }
        }
    }
}
