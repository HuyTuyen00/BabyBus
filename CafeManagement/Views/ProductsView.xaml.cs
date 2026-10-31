using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;
using CafeManagement.Database;
using CafeManagement.Models;
using CafeManagement.Utilities;

namespace CafeManagement.Views
{
    public partial class ProductsView : UserControl
    {
        private List<Product> _allProducts = new List<Product>();
        private List<Category> _categories = new List<Category>();

        public ProductsView()
        {
            InitializeComponent();
            Loaded += ProductsView_Loaded;
        }

        private bool _isDataLoaded = false;

        private async void ProductsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isDataLoaded)
            {
                await LoadDataAsync();
                _isDataLoaded = true;
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    LoadCategoriesFromDb();
                    LoadProductsFromDb();
                });

                // Bind Edit ComboBox
                cbFormCategory.ItemsSource = _categories.ToList();

                // Bind Delete Category ComboBox
                cbDeleteCategory.ItemsSource = _categories.ToList();

                // Bind Filter ComboBox
                var filterCats = new List<Category> { new Category { Id = 0, Name = "-- Tất cả nhóm hàng --" } };
                filterCats.AddRange(_categories);
                cbGridCategory.ItemsSource = filterCats;
                cbGridCategory.SelectedIndex = 0;

                FilterAndRenderProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadCategoriesFromDb()
        {
            _categories.Clear();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                string sql = "SELECT Id, Name FROM Categories ORDER BY Name";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _categories.Add(new Category
                        {
                            Id = Convert.ToInt32(reader.GetValue(0)),
                            Name = reader.GetString(1)
                        });
                    }
                }
            }
        }

        private void LoadProductsFromDb()
        {
            _allProducts.Clear();
            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT p.Id, p.Name, p.Price, p.Unit, p.CategoryId, p.ImagePath, c.Name AS CategoryName, p.IsExcludeFromKitchen
                    FROM Products p 
                    JOIN Categories c ON p.CategoryId = c.Id 
                    ORDER BY p.Id";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _allProducts.Add(new Product
                        {
                            Id = Convert.ToInt32(reader.GetValue(0)),
                            Name = reader.GetString(1),
                            Price = Convert.ToDecimal(reader.GetValue(2)),
                            Unit = reader.GetString(3),
                            CategoryId = Convert.ToInt32(reader.GetValue(4)),
                            ImagePath = reader.IsDBNull(5) ? "" : ResolveImagePath(reader.GetString(5)),
                            CategoryName = reader.GetString(6),
                            IsExcludeFromKitchen = reader.IsDBNull(7) ? false : Convert.ToBoolean(reader.GetValue(7))
                        });
                    }
                }
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
            if (gridProducts == null || txtSearchGrid == null || cbGridCategory == null) return;

            string keyword = txtSearchGrid.Text.Trim();
            if (keyword == "Tìm theo tên sản phẩm...") keyword = "";

            string cleanKeyword = RemoveVietnameseAccents(keyword);
            int selectedCatId = cbGridCategory.SelectedValue as int? ?? 0;

            var filtered = _allProducts.Where(p =>
                (selectedCatId == 0 || p.CategoryId == selectedCatId) &&
                (string.IsNullOrEmpty(cleanKeyword) || 
                 p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 RemoveVietnameseAccents(p.Name).Contains(cleanKeyword, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            gridProducts.ItemsSource = filtered;
        }

        private void TxtSearchGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearchGrid.Text == "Tìm theo tên sản phẩm...")
            {
                txtSearchGrid.Text = "";
                txtSearchGrid.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void TxtSearchGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearchGrid.Text))
            {
                txtSearchGrid.Text = "Tìm theo tên sản phẩm...";
                txtSearchGrid.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void TxtSearchGrid_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndRenderProducts();
        }

        private void CbGridCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterAndRenderProducts();
        }

        private void GridProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridProducts.SelectedItem is Product prod)
            {
                lblProductId.Text = prod.Id.ToString();
                txtName.Text = prod.Name;
                cbFormCategory.SelectedValue = prod.CategoryId;
                txtPrice.Text = prod.Price.ToString("F0", CultureInfo.InvariantCulture);
                // Extract relative path from absolute path if starts with wwwroot
                string relPath = prod.ImagePath ?? "";
                if (relPath.Contains("wwwroot"))
                {
                    int idx = relPath.IndexOf("images");
                    if (idx >= 0) relPath = relPath.Substring(idx);
                }
                txtImagePath.Text = relPath;
                txtUnit.Text = prod.Unit;
                chkIsExcludeFromKitchen.IsChecked = prod.IsExcludeFromKitchen;

                lblFormTitle.Text = "CẬP NHẬT SẢN PHẨM";
                btnSave.Content = "Lưu (Cập nhật)";
                btnDelete.IsEnabled = true;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetFormFields();
        }

        private void ResetFormFields()
        {
            lblProductId.Text = "";
            txtName.Text = "";
            cbFormCategory.SelectedIndex = -1;
            txtPrice.Text = "0";
            txtImagePath.Text = "";
            txtUnit.Text = "Ly";
            chkIsExcludeFromKitchen.IsChecked = false;

            lblFormTitle.Text = "THÊM SẢN PHẨM MỚI";
            btnSave.Content = "Lưu (Thêm)";
            btnDelete.IsEnabled = false;
            gridProducts.SelectedItem = null;
        }

        // Form save (anti-double click + async + validation rules)
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Vui lòng nhập tên sản phẩm!", "Bắt buộc", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cbFormCategory.SelectedValue == null)
            {
                MessageBox.Show("Vui lòng chọn nhóm sản phẩm!", "Bắt buộc", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int categoryId = (int)cbFormCategory.SelectedValue;

            if (!decimal.TryParse(txtPrice.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price) || price < 0)
            {
                MessageBox.Show("Giá bán phải là số dương!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string imagePath = txtImagePath.Text.Trim();

            string unit = txtUnit.Text.Trim();
            if (string.IsNullOrEmpty(unit)) unit = "Ly";

            btnSave.IsEnabled = false;
            btnReset.IsEnabled = false;

            bool isExclude = chkIsExcludeFromKitchen.IsChecked ?? false;

            try
            {
                bool isUpdate = !string.IsNullOrEmpty(lblProductId.Text);
                int productId = isUpdate ? int.Parse(lblProductId.Text) : 0;

                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();
                        if (isUpdate)
                        {
                            string sql = @"
                                UPDATE Products 
                                SET Name = @Name, CategoryId = @CategoryId, Price = @Price, 
                                    Unit = @Unit, ImagePath = @ImagePath, IsExcludeFromKitchen = @IsExcludeFromKitchen
                                WHERE Id = @Id";
                            using (var cmd = new SqlCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@Id", productId);
                                cmd.Parameters.AddWithValue("@Name", name);
                                cmd.Parameters.AddWithValue("@CategoryId", categoryId);
                                cmd.Parameters.AddWithValue("@Price", price);
                                cmd.Parameters.AddWithValue("@Unit", unit);
                                cmd.Parameters.AddWithValue("@ImagePath", imagePath);
                                cmd.Parameters.AddWithValue("@IsExcludeFromKitchen", isExclude);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            string sql = @"
                                INSERT INTO Products (Name, CategoryId, Price, Unit, ImagePath, IsExcludeFromKitchen) 
                                VALUES (@Name, @CategoryId, @Price, @Unit, @ImagePath, @IsExcludeFromKitchen)";
                            using (var cmd = new SqlCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@Name", name);
                                cmd.Parameters.AddWithValue("@CategoryId", categoryId);
                                cmd.Parameters.AddWithValue("@Price", price);
                                cmd.Parameters.AddWithValue("@Unit", unit);
                                cmd.Parameters.AddWithValue("@ImagePath", imagePath);
                                cmd.Parameters.AddWithValue("@IsExcludeFromKitchen", isExclude);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                });

                MessageBox.Show("Lưu thông tin sản phẩm thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetFormFields();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi lưu sản phẩm: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSave.IsEnabled = true;
                btnReset.IsEnabled = true;
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(lblProductId.Text)) return;
            int productId = int.Parse(lblProductId.Text);
            string productName = txtName.Text.Trim();

            var confirm = MessageBox.Show($"Bạn có thực sự muốn xóa sản phẩm [{productName}]?\n(Toàn bộ liên kết trong nhà bếp và lịch sử nếu có sẽ được làm sạch tự động)", 
                                          "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            btnDelete.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();
                        using var trans = conn.BeginTransaction();
                        try
                        {
                            // 1. Xóa các bản ghi liên quan trong hàng đợi bếp (KitchenQueue)
                            using (var cmdK = new SqlCommand("DELETE FROM KitchenQueue WHERE ProductId = @Id", conn, trans))
                            {
                                cmdK.Parameters.AddWithValue("@Id", productId);
                                cmdK.ExecuteNonQuery();
                            }

                            // 2. Xóa các dòng chi tiết hóa đơn (OrderDetails) chứa sản phẩm này
                            using (var cmdO = new SqlCommand("DELETE FROM OrderDetails WHERE ProductId = @Id", conn, trans))
                            {
                                cmdO.Parameters.AddWithValue("@Id", productId);
                                cmdO.ExecuteNonQuery();
                            }

                            // 3. Cập nhật lại tổng tiền các hóa đơn bị ảnh hưởng (nếu có)
                            string sqlRecalc = @"
                                UPDATE Orders
                                SET TotalAmount = ISNULL((SELECT SUM(Quantity * Price) FROM OrderDetails WHERE OrderId = Orders.Id), 0)
                                WHERE Status = 'Pending';
                                
                                -- Xóa các hóa đơn đang chờ (Pending) nếu không còn món nào
                                DELETE FROM Orders 
                                WHERE Status = 'Pending' AND Id NOT IN (SELECT DISTINCT OrderId FROM OrderDetails);
                            ";
                            using (var cmdR = new SqlCommand(sqlRecalc, conn, trans))
                            {
                                cmdR.ExecuteNonQuery();
                            }

                            // 4. Xóa chính thức sản phẩm khỏi bảng Products
                            using (var cmdP = new SqlCommand("DELETE FROM Products WHERE Id = @Id", conn, trans))
                            {
                                cmdP.Parameters.AddWithValue("@Id", productId);
                                cmdP.ExecuteNonQuery();
                            }

                            trans.Commit();
                        }
                        catch
                        {
                            trans.Rollback();
                            throw;
                        }
                    }
                });

                MessageBox.Show($"Xóa sản phẩm [{productName}] thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetFormFields();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xóa sản phẩm: {ex.Message}", "Lỗi xóa sản phẩm", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDelete.IsEnabled = true;
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (gridProducts.ItemsSource == null) return;
            var listToExport = ((IEnumerable<Product>)gridProducts.ItemsSource).ToList();

            var saveDlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"DanhSachSanPham_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (saveDlg.ShowDialog() == true)
            {
                try
                {
                    ExcelHelper.ExportProducts(saveDlg.FileName, listToExport);
                    MessageBox.Show("Xuất Excel thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new OpenFileDialog
            {
                Filter = "File Excel (*.xlsx)|*.xlsx",
                Title = "Chọn file danh sách sản phẩm Excel (KiotViet / Excel)"
            };

            if (openDlg.ShowDialog() == true)
            {
                try
                {
                    string filePath = openDlg.FileName;
                    var items = await Task.Run(() => ExcelMenuImporter.ReadKiotVietExcel(filePath));
                    if (items.Count == 0)
                    {
                        MessageBox.Show("Không tìm thấy món hợp lệ trong file Excel. Vui lòng kiểm tra lại cấu trúc file.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var result = MessageBox.Show(
                        $"Tìm thấy {items.Count} món trong file Excel!\n\n" +
                        "• Bấm [YES] để XÓA THỰC ĐƠN CŨ và nạp mới toàn bộ thực đơn này.\n" +
                        "• Bấm [NO] để GIỮ THỰC ĐƠN HIỆN TẠI và thêm mới/cập nhật các món từ Excel.\n" +
                        "• Bấm [CANCEL] để hủy bỏ.",
                        "Tùy chọn nhập Excel",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel) return;

                    bool clearExisting = (result == MessageBoxResult.Yes);

                    var (added, updated) = await Task.Run(() => ExcelMenuImporter.ImportToDatabase(items, clearExisting));

                    MessageBox.Show($"Nhập thực đơn từ Excel thành công!\n• Đã thêm mới: {added} món\n• Đã cập nhật: {updated} món", 
                                    "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    await LoadDataAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi nhập Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnChooseImage_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
            };

            if (openDlg.ShowDialog() == true)
            {
                try
                {
                    string sourceFile = openDlg.FileName;
                    string filename = Path.GetFileName(sourceFile);

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string currentDir = Directory.GetCurrentDirectory();

                    CopyImageToDir(sourceFile, filename, baseDir);
                    if (!string.Equals(baseDir.TrimEnd('\\', '/'), currentDir.TrimEnd('\\', '/'),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        CopyImageToDir(sourceFile, filename, currentDir);
                    }

                    // Save relative path
                    txtImagePath.Text = "images/" + filename;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi chọn hình ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void CopyImageToDir(string sourceFile, string filename, string rootDir)
        {
            string targetDir = Path.Combine(rootDir, "wwwroot", "images");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            string targetFile = Path.Combine(targetDir, filename);
            File.Copy(sourceFile, targetFile, true);
        }

        private async void BtnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            string catName = txtNewCategoryName.Text.Trim();
            if (string.IsNullOrEmpty(catName))
            {
                MessageBox.Show("Vui lòng nhập tên nhóm sản phẩm mới!", "Bắt buộc", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAddCategory.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();
                        
                        // Check if exists
                        string sqlCheck = "SELECT COUNT(Id) FROM Categories WHERE Name = @Name";
                        using (var cmd = new SqlCommand(sqlCheck, conn))
                        {
                            cmd.Parameters.AddWithValue("@Name", catName);
                            int count = Convert.ToInt32(cmd.ExecuteScalar());
                            if (count > 0)
                            {
                                throw new Exception("Nhóm sản phẩm này đã tồn tại!");
                            }
                        }

                        string sqlInsert = "INSERT INTO Categories (Name) VALUES (@Name)";
                        using (var cmd = new SqlCommand(sqlInsert, conn))
                        {
                            cmd.Parameters.AddWithValue("@Name", catName);
                            cmd.ExecuteNonQuery();
                        }
                    }
                });

                MessageBox.Show($"Thêm nhóm sản phẩm [{catName}] thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                txtNewCategoryName.Text = "";
                await LoadDataAsync(); // Reload comboboxes
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi thêm nhóm hàng: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnAddCategory.IsEnabled = true;
            }
        }

        private async void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (cbDeleteCategory.SelectedValue == null)
            {
                MessageBox.Show("Vui lòng chọn nhóm muốn xóa!", "Bắt buộc", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int catId = (int)cbDeleteCategory.SelectedValue;
            string catName = (cbDeleteCategory.SelectedItem as Category)?.Name ?? "";

            var result = MessageBox.Show($"Bạn chắc chắn muốn xóa nhóm [{catName}]?\nLưu ý: Không thể xóa nhóm đang có sản phẩm.",
                "Xác nhận xóa nhóm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            btnDeleteCategory.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(DbHelper.ConnectionString))
                    {
                        conn.Open();

                        // Check if category has products
                        string sqlCheck = "SELECT COUNT(Id) FROM Products WHERE CategoryId = @Id";
                        using (var cmd = new SqlCommand(sqlCheck, conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", catId);
                            int count = Convert.ToInt32(cmd.ExecuteScalar());
                            if (count > 0)
                            {
                                throw new Exception($"Nhóm [{catName}] đang có {count} sản phẩm. Vui lòng xóa hoặc chuyển các sản phẩm trước.");
                            }
                        }

                        string sqlDelete = "DELETE FROM Categories WHERE Id = @Id";
                        using (var cmd = new SqlCommand(sqlDelete, conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", catId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                });

                MessageBox.Show($"Xóa nhóm [{catName}] thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadDataAsync(); // Reload comboboxes
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xóa nhóm: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDeleteCategory.IsEnabled = true;
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
    }
}

