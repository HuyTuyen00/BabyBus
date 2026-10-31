using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using CafeManagement.Models;

namespace CafeManagement.Utilities
{
    public static class ExcelHelper
    {
        public static void ExportProducts(string filePath, List<Product> products)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Sản phẩm");
                
                // Show grid lines
                worksheet.ShowGridLines = true;

                // Headers
                string[] headers = { "Mã SP", "Tên sản phẩm", "Đơn giá", "Đơn vị tính", "Danh mục" };
                
                // Set Header Row Height
                worksheet.Row(1).Height = 28;

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.FontName = "Segoe UI";
                    cell.Style.Font.FontSize = 11;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A"); // Navy Blue
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#808080");
                }

                // Data Rows
                int row = 2;
                foreach (var prod in products)
                {
                    worksheet.Row(row).Height = 22;

                    // Write values
                    worksheet.Cell(row, 1).Value = prod.Id;
                    worksheet.Cell(row, 2).Value = prod.Name;
                    worksheet.Cell(row, 3).Value = prod.Price;
                    worksheet.Cell(row, 4).Value = prod.Unit;
                    worksheet.Cell(row, 5).Value = prod.CategoryName;

                    // Apply formatting and alignment
                    for (int col = 1; col <= headers.Length; col++)
                    {
                        var cell = worksheet.Cell(row, col);
                        cell.Style.Font.FontName = "Segoe UI";
                        cell.Style.Font.FontSize = 10;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#808080");
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                        // Zebra striping
                        if (row % 2 == 0)
                        {
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                        }

                        // Alignments
                        if (col == 1) // Mã SP
                        {
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        else if (col == 2 || col == 4 || col == 5) // Tên, ĐVT, Danh mục
                        {
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        }
                        else if (col == 3) // Đơn giá (căn phải, định dạng tiền tệ)
                        {
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            cell.Style.NumberFormat.Format = "#,##0";
                        }
                    }
                    row++;
                }

                int totalRows = row - 1;

                // Auto filter
                worksheet.Range(1, 1, totalRows, headers.Length).SetAutoFilter();

                // Auto-fit column widths with Unicode multiplier
                AdjustColumnWidths(worksheet, headers.Length, totalRows);

                workbook.SaveAs(filePath);
            }
        }

        public static void ExportOrders(string filePath, List<Order> orders)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Hóa đơn");
                worksheet.ShowGridLines = true;

                string[] headers = { "Mã HĐ", "Ngày bán", "Bàn", "Tổng tiền", "Trạng thái" };
                
                worksheet.Row(1).Height = 28;

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.FontName = "Segoe UI";
                    cell.Style.Font.FontSize = 11;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A"); // Navy Blue
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#808080");
                }

                int row = 2;
                foreach (var ord in orders)
                {
                    worksheet.Row(row).Height = 22;

                    worksheet.Cell(row, 1).Value = ord.Id;
                    worksheet.Cell(row, 2).Value = ord.OrderDate.ToString("dd/MM/yyyy HH:mm");
                    worksheet.Cell(row, 3).Value = ord.TableName;
                    worksheet.Cell(row, 4).Value = ord.TotalAmount;
                    worksheet.Cell(row, 5).Value = ord.Status == "Paid" ? "Đã thanh toán" : (ord.Status == "Cancelled" ? "Đã hủy" : "Chờ xử lý");

                    for (int col = 1; col <= headers.Length; col++)
                    {
                        var cell = worksheet.Cell(row, col);
                        cell.Style.Font.FontName = "Segoe UI";
                        cell.Style.Font.FontSize = 10;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#808080");
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                        if (row % 2 == 0)
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");

                        if (col == 1 || col == 2 || col == 5) // Mã HĐ, Ngày, Trạng thái
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        else if (col == 3) // Bàn
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        else if (col == 4) // Tiền
                        {
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            cell.Style.NumberFormat.Format = "#,##0";
                        }
                    }
                    row++;
                }

                int totalRows = row - 1;
                worksheet.Range(1, 1, totalRows, headers.Length).SetAutoFilter();
                AdjustColumnWidths(worksheet, headers.Length, totalRows);

                workbook.SaveAs(filePath);
            }
        }

        private static void AdjustColumnWidths(IXLWorksheet worksheet, int colCount, int rowCount)
        {
            for (int col = 1; col <= colCount; col++)
            {
                double maxWidth = 10; // minimum width
                for (int row = 1; row <= rowCount; row++)
                {
                    var cellValue = worksheet.Cell(row, col).Value.ToString() ?? "";
                    double len = 0;
                    foreach (char c in cellValue)
                    {
                        // Add 1.8 multiplier for Unicode characters, 1.0 for ASCII
                        len += c > 127 ? 1.8 : 1.0;
                    }

                    if (len > maxWidth)
                    {
                        maxWidth = len;
                    }
                }
                worksheet.Column(col).Width = maxWidth + 4; // Add padding
            }
        }
    }
}
