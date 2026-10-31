using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Data.SqlClient;
using CafeManagement.Database;

namespace CafeManagement.Utilities
{
    public class ExcelProductItem
    {
        public string CategoryName { get; set; } = "Khác";
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Unit { get; set; } = "Ly";
        public bool IsExcludeFromKitchen { get; set; } = false;
    }

    public static class ExcelMenuImporter
    {
        public static List<ExcelProductItem> ReadKiotVietExcel(string filePath)
        {
            var result = new List<ExcelProductItem>();
            if (!File.Exists(filePath)) return result;

            string tempZip = Path.Combine(Path.GetTempPath(), $"excel_import_{Guid.NewGuid():N}.zip");
            string extractDir = Path.Combine(Path.GetTempPath(), $"excel_dir_{Guid.NewGuid():N}");

            try
            {
                File.Copy(filePath, tempZip, true);
                ZipFile.ExtractToDirectory(tempZip, extractDir);

                // 1. Read Shared Strings
                var sharedStrings = new List<string>();
                string ssPath = Path.Combine(extractDir, "xl", "sharedStrings.xml");
                if (File.Exists(ssPath))
                {
                    var ssDoc = new XmlDocument();
                    ssDoc.Load(ssPath);
                    var siNodes = ssDoc.GetElementsByTagName("si");
                    foreach (XmlNode si in siNodes)
                    {
                        var tNode = si["t"];
                        if (tNode != null)
                        {
                            sharedStrings.Add(tNode.InnerText);
                        }
                        else
                        {
                            var rNodes = si.SelectNodes(".//t");
                            string text = "";
                            if (rNodes != null)
                            {
                                foreach (XmlNode r in rNodes) text += r.InnerText;
                            }
                            sharedStrings.Add(text);
                        }
                    }
                }

                // 2. Read Worksheet
                string sheetPath = Path.Combine(extractDir, "xl", "worksheets", "sheet1.xml");
                if (File.Exists(sheetPath))
                {
                    var sheetDoc = new XmlDocument();
                    sheetDoc.Load(sheetPath);

                    string colCat = "A";
                    string colName = "B";
                    string colPrice = "C";
                    string colUnit = "D";

                    var rowNodes = sheetDoc.GetElementsByTagName("row");
                    bool isFirstRow = true;

                    foreach (XmlNode rowNode in rowNodes)
                    {
                        var cellValues = new Dictionary<string, string>();
                        foreach (XmlNode cell in rowNode.ChildNodes)
                        {
                            if (cell.Name != "c") continue;
                            string cellRef = cell.Attributes?["r"]?.Value ?? "";
                            string cellType = cell.Attributes?["t"]?.Value ?? "";
                            string val = cell["v"]?.InnerText ?? "";

                            if (cellType == "s" && int.TryParse(val, out int sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                            {
                                val = sharedStrings[sIdx];
                            }
                            else if (cell["is"]?["t"] != null)
                            {
                                val = cell["is"]["t"].InnerText;
                            }

                            string colLetters = Regex.Replace(cellRef, @"[\d]", "").ToUpper();
                            cellValues[colLetters] = val.Trim();
                        }

                        if (isFirstRow)
                        {
                            // Detect column mappings from header names
                            foreach (var kv in cellValues)
                            {
                                string h = kv.Value.ToLower();
                                if (h.Contains("nhóm") || h.Contains("loại") || h.Contains("category")) colCat = kv.Key;
                                else if (h.Contains("tên") || h.Contains("món") || h.Contains("sản phẩm") || h.Contains("hàng hóa")) colName = kv.Key;
                                else if (h.Contains("giá") || h.Contains("bán") || h.Contains("price")) colPrice = kv.Key;
                                else if (h.Contains("đơn vị") || h.Contains("dvt") || h.Contains("unit")) colUnit = kv.Key;
                            }
                            isFirstRow = false;
                            continue;
                        }

                        cellValues.TryGetValue(colCat, out string? catVal);
                        cellValues.TryGetValue(colName, out string? nameVal);
                        cellValues.TryGetValue(colPrice, out string? priceVal);
                        cellValues.TryGetValue(colUnit, out string? unitVal);

                        if (string.IsNullOrWhiteSpace(nameVal)) continue;

                        string category = string.IsNullOrWhiteSpace(catVal) ? "Khác" : catVal.Trim();
                        string prodName = nameVal.Trim();

                        // Clean leading numbering like "1. ", "2.", "3 - ", "01. "
                        prodName = Regex.Replace(prodName, @"^\d+[\.\-\)]\s*", "").Trim();
                        category = Regex.Replace(category, @"^\d+[\.\-\)]\s*", "").Trim();

                        decimal price = 0;

                        if (!string.IsNullOrWhiteSpace(priceVal))
                        {
                            // Parse numeric price
                            string cleanedPrice = Regex.Replace(priceVal, @"[^\d\.]", "");
                            decimal.TryParse(cleanedPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                        }

                        // Determine default unit & kitchen exclusion
                        string unit = string.IsNullOrWhiteSpace(unitVal) ? "Ly" : unitVal.Trim();
                        bool isExclude = false;
                        string catLower = category.ToLower();
                        string nameLower = prodName.ToLower();

                        if (catLower.Contains("vé") || catLower.Contains("ve") || catLower.Contains("xu") || nameLower.Contains("vé") || nameLower.Contains("xu"))
                        {
                            unit = "Vé";
                            isExclude = true;
                        }
                        else if (catLower.Contains("tượng") || catLower.Contains("tuong") || catLower.Contains("kem") || catLower.Contains("đồ ăn") || catLower.Contains("do an"))
                        {
                            if (catLower.Contains("tượng")) unit = "Con";
                            else if (catLower.Contains("kem")) unit = "Cây";
                            else if (string.IsNullOrWhiteSpace(unitVal)) unit = "Phần";

                            if (catLower.Contains("tượng") || catLower.Contains("kem") || nameLower.Contains("bắp ngô") || nameLower.Contains("hướng dương") || nameLower.Contains("bim bim"))
                            {
                                isExclude = true;
                            }
                        }

                        result.Add(new ExcelProductItem
                        {
                            CategoryName = category,
                            ProductName = prodName,
                            Price = price,
                            Unit = unit,
                            IsExcludeFromKitchen = isExclude
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("excel_import_log.txt", $"{DateTime.Now}: Lỗi đọc Excel: {ex.Message}\n{ex.StackTrace}\n");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                }
                catch { }
            }

            return result;
        }

        public static (int added, int updated) ImportToDatabase(List<ExcelProductItem> items, bool clearExisting = false)
        {
            if (items == null || items.Count == 0) return (0, 0);

            int addedCount = 0;
            int updatedCount = 0;

            using (var conn = new SqlConnection(DbHelper.ConnectionString))
            {
                conn.Open();
                using var trans = conn.BeginTransaction();
                try
                {
                    if (clearExisting)
                    {
                        // Clean existing menu safely
                        using var cmdClean = new SqlCommand(@"
                            DELETE FROM KitchenQueue;
                            DELETE FROM OrderDetails;
                            DELETE FROM Products;
                            DELETE FROM Categories;
                            DBCC CHECKIDENT ('Categories', RESEED, 0);
                            DBCC CHECKIDENT ('Products', RESEED, 0);", conn, trans);
                        cmdClean.ExecuteNonQuery();
                    }

                    var catCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    // Load existing categories
                    using (var cmdCat = new SqlCommand("SELECT Id, Name FROM Categories", conn, trans))
                    using (var rd = cmdCat.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            catCache[rd.GetString(1).Trim()] = Convert.ToInt32(rd.GetValue(0));
                        }
                    }

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.ProductName)) continue;

                        string catName = item.CategoryName?.Trim();
                        if (string.IsNullOrWhiteSpace(catName)) catName = "Khác";

                        // Ensure category exists
                        if (!catCache.TryGetValue(catName, out int catId))
                        {
                            using (var cmdInsCat = new SqlCommand(@"
                                INSERT INTO Categories (Name) VALUES (@name);
                                SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans))
                            {
                                cmdInsCat.Parameters.AddWithValue("@name", catName);
                                catId = (int)cmdInsCat.ExecuteScalar();
                                catCache[catName] = catId;
                            }
                        }

                        // Check if product exists
                        int existingId = 0;
                        using (var cmdCheck = new SqlCommand("SELECT Id FROM Products WHERE Name = @name", conn, trans))
                        {
                            cmdCheck.Parameters.AddWithValue("@name", item.ProductName.Trim());
                            var res = cmdCheck.ExecuteScalar();
                            if (res != null) existingId = Convert.ToInt32(res);
                        }

                        if (existingId > 0)
                        {
                            using (var cmdUpd = new SqlCommand(@"
                                UPDATE Products 
                                SET CategoryId = @catId, Price = @price, Unit = @unit, IsExcludeFromKitchen = @isExclude
                                WHERE Id = @id", conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@id", existingId);
                                cmdUpd.Parameters.AddWithValue("@catId", catId);
                                cmdUpd.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdUpd.Parameters.AddWithValue("@unit", item.Unit ?? "Ly");
                                cmdUpd.Parameters.AddWithValue("@isExclude", item.IsExcludeFromKitchen);
                                cmdUpd.ExecuteNonQuery();
                                updatedCount++;
                            }
                        }
                        else
                        {
                            using (var cmdIns = new SqlCommand(@"
                                INSERT INTO Products (Name, CategoryId, Price, Unit, ImagePath, IsExcludeFromKitchen)
                                VALUES (@name, @catId, @price, @unit, '', @isExclude)", conn, trans))
                            {
                                cmdIns.Parameters.AddWithValue("@name", item.ProductName.Trim());
                                cmdIns.Parameters.AddWithValue("@catId", catId);
                                cmdIns.Parameters.AddWithValue("@price", (double)item.Price);
                                cmdIns.Parameters.AddWithValue("@unit", item.Unit ?? "Ly");
                                cmdIns.Parameters.AddWithValue("@isExclude", item.IsExcludeFromKitchen);
                                cmdIns.ExecuteNonQuery();
                                addedCount++;
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
            }

            return (addedCount, updatedCount);
        }
    }
}
