using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using CafeManagement.Models;

namespace CafeManagement.Utilities
{
    public static class PrinterHelper
    {
        public static void PrintInvoice(long orderId, string tableName, List<OrderDetail> cartItems, decimal totalAmount, decimal discount = 0, string title = "PHIẾU TÍNH TIỀN", int copies = 1)
        {
            try
            {
                using (var pd = new PrintDocument())
                {
                    // 1. Get target printer name from SettingsHelper or printer_config.txt
                    string targetPrinter = SettingsHelper.Current.PrinterName;
                    if (string.IsNullOrEmpty(targetPrinter))
                    {
                        string configFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printer_config.txt");
                        if (System.IO.File.Exists(configFile))
                        {
                            targetPrinter = System.IO.File.ReadAllText(configFile).Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(targetPrinter))
                    {
                        pd.PrinterSettings.PrinterName = targetPrinter;
                    }

                    pd.PrintPage += (sender, e) =>
                    {
                        if (e.Graphics == null) return;
                        RenderInvoice(e.Graphics, orderId, tableName, cartItems, totalAmount, discount, title);
                    };

                    // Print loop (allows 0 copies to skip printing)
                    if (copies > 0)
                    {
                        for (int i = 0; i < copies; i++)
                        {
                            pd.Print();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi kết nối máy in: {ex.Message}\nVui lòng kiểm tra lại thiết bị máy in mặc định trên Windows.",
                                "Lỗi in ấn", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private static void RenderInvoice(Graphics g, long orderId, string tableName, List<OrderDetail> cartItems, decimal totalAmount, decimal discount, string title)
        {
            // Standard K80 printable width is approx 280-285 units (1 unit = 1/100 inch) -> 72mm width
            float width = 280;
            float margin = 10;
            float usableWidth = width - (margin * 2);

            // Setup Font Families and Styles to avoid clashes with WPF
            var family = new FontFamily("Segoe UI");
            var fontTitle = new Font(family, 18, FontStyle.Bold);
            var fontSubTitle = new Font(family, 11, FontStyle.Bold);
            var fontBody = new Font(family, 9, FontStyle.Regular);
            var fontBodyBold = new Font(family, 9, FontStyle.Bold);
            var fontBodyItalic = new Font(family, 8.5f, FontStyle.Italic);
            var fontHeaderTable = new Font(family, 9.5f, FontStyle.Bold);
            var fontAddr = new Font(family, 8.5f, FontStyle.Bold);

            var brush = Brushes.Black;
            var penSolid = new Pen(Color.Black, 1f);
            var penDash = new Pen(Color.Black, 1f) { DashPattern = new float[] { 3, 3 } };

            float y = 10;

            // 1. Header: BABYBUS COFFEE & TEA
            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("BABYBUS", fontTitle, brush, new RectangleF(margin, y, usableWidth, 30), sfCenter);
                y += 28;
                g.DrawString("COFFEE & TEA", fontSubTitle, brush, new RectangleF(margin, y, usableWidth, 20), sfCenter);
                y += 22;

                g.DrawString("KĐT Phía Tây - TT.Nam Sách - Hải Dương", fontAddr, brush, new RectangleF(margin, y, usableWidth, 16), sfCenter);
                y += 16;
                g.DrawString("ĐT: 0936.591.456 - 0936.000.159", fontAddr, brush, new RectangleF(margin, y, usableWidth, 16), sfCenter);
                y += 22;
            }

            // 2. Table badge & Invoice title side-by-side
            float badgeWidth = 75;
            float badgeHeight = 32;
            var rectBadge = new RectangleF(margin, y, badgeWidth, badgeHeight);
            
            // Draw a rounded-corner look rectangle
            float radius = 6;
            using (var path = new GraphicsPath())
            {
                path.AddArc(rectBadge.X, rectBadge.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(rectBadge.Right - radius * 2, rectBadge.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(rectBadge.Right - radius * 2, rectBadge.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(rectBadge.X, rectBadge.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseAllFigures();
                g.DrawPath(penSolid, path);
            }

            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(string.IsNullOrEmpty(tableName) ? "Mang đi" : tableName, fontSubTitle, brush, rectBadge, sfCenter);
            }

            // Right side: PHIẾU TÍNH TIỀN & Invoice Code
            float rightX = margin + badgeWidth + 10;
            float rightWidth = usableWidth - badgeWidth - 10;
            
            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString(title, fontSubTitle, brush, new RectangleF(rightX, y, rightWidth, 16), sfCenter);
                if (orderId > 0)
                {
                    g.DrawString($"Số: {orderId}", fontBodyBold, brush, new RectangleF(rightX, y + 16, rightWidth, 16), sfCenter);
                }
            }
            y += badgeHeight + 15;

            // 3. Time info: Giờ vào, Giờ in (on separate lines for perfect readability)
            string timeIn = DateTime.Now.AddMinutes(-10).ToString("dd/MM/yyyy HH:mm");
            string timePrint = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            g.DrawString($"Giờ vào: {timeIn}", fontBody, brush, margin, y);
            y += 15;
            g.DrawString($"Giờ in:  {timePrint}", fontBody, brush, margin, y);
            y += 18;

            // Header columns line separator
            g.DrawLine(penSolid, margin, y, width - margin, y);
            y += 4;

            // Table headers: Tên hàng, Đ.Giá, SL, Thành tiền
            g.DrawString("Tên hàng", fontHeaderTable, brush, margin, y);
            
            using (var sfRight = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString("Đ.Giá", fontHeaderTable, brush, margin + 185, y, sfRight);
                g.DrawString("SL", fontHeaderTable, brush, margin + 215, y, sfRight);
                g.DrawString("Thành tiền", fontHeaderTable, brush, usableWidth + margin, y, sfRight);
            }
            y += 18;

            g.DrawLine(penSolid, margin, y, width - margin, y);
            y += 6;

            // 4. Invoice Items rendering loop
            foreach (var item in cartItems)
            {
                string nameAndNote = item.ProductName;
                if (!string.IsNullOrEmpty(item.Note))
                {
                    nameAndNote += $"\n({item.Note})";
                }

                // Measure height of the item name
                var sizeName = g.MeasureString(nameAndNote, fontBody, 125);
                g.DrawString(nameAndNote, fontBody, brush, new RectangleF(margin, y, 125, sizeName.Height));

                using (var sfRight = new StringFormat { Alignment = StringAlignment.Far })
                {
                    string qtyText = $"{item.Quantity}";
                    string priceText = $"{item.Price:N0} x";
                    string totalText = $"{item.TotalPrice:N0}";

                    float itemY = y;
                    
                    g.DrawString(priceText, fontBody, brush, margin + 185, itemY, sfRight);
                    g.DrawString(qtyText, fontBody, brush, margin + 215, itemY, sfRight);
                    g.DrawString(totalText, fontBody, brush, usableWidth + margin, itemY, sfRight);
                }

                y += Math.Max(sizeName.Height, 16) + 4;
            }

            g.DrawLine(penSolid, margin, y, width - margin, y);
            y += 6;

            // 5. Invoice Totals
            using (var sfRight = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString("Tổng Bill :", fontBody, brush, margin + 100, y);
                g.DrawString($"{totalAmount:N0}", fontBody, brush, usableWidth + margin, y, sfRight);
                y += 18;

                g.DrawString("Giảm Giá :", fontBody, brush, margin + 100, y);
                g.DrawString($"{discount:N0}", fontBody, brush, usableWidth + margin, y, sfRight);
                y += 18;

                g.DrawString("Tổng Bill Thanh Toán:", fontBodyBold, brush, margin + 50, y);
                g.DrawString($"{totalAmount - discount:N0}", fontBodyBold, brush, usableWidth + margin, y, sfRight);
                y += 24;
            }

            g.DrawLine(penDash, margin, y, width - margin, y);
            y += 8;

            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("BABYBUS Cảm ơn quý khách và hẹn gặp lại !!!", fontBodyItalic, brush, new RectangleF(margin, y, usableWidth, 20), sfCenter);
            }
        }

        public static void PrintKitchenSlip(string tableName, List<OrderDetail> completedItems, int copies = 1)
        {
            if (completedItems == null || completedItems.Count == 0) return;
            try
            {
                using (var pd = new PrintDocument())
                {
                    string targetPrinter = SettingsHelper.Current.KitchenPrinterName;
                    if (string.IsNullOrEmpty(targetPrinter))
                    {
                        targetPrinter = SettingsHelper.Current.PrinterName;
                    }
                    if (string.IsNullOrEmpty(targetPrinter))
                    {
                        string configFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printer_config.txt");
                        if (System.IO.File.Exists(configFile))
                        {
                            targetPrinter = System.IO.File.ReadAllText(configFile).Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(targetPrinter))
                    {
                        pd.PrinterSettings.PrinterName = targetPrinter;
                    }

                    pd.PrintPage += (sender, e) =>
                    {
                        if (e.Graphics == null) return;
                        RenderKitchenSlip(e.Graphics, tableName, completedItems);
                    };

                    if (copies > 0)
                    {
                        for (int i = 0; i < copies; i++)
                        {
                            pd.Print();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi in bếp: {ex.Message}", "Lỗi in bếp", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private static void RenderKitchenSlip(Graphics g, string tableName, List<OrderDetail> completedItems)
        {
            float width = 280;
            float margin = 10;
            float usableWidth = width - (margin * 2);

            var family = new FontFamily("Segoe UI");
            var fontTitle = new Font(family, 16, FontStyle.Bold);
            var fontSubTitle = new Font(family, 12, FontStyle.Bold);
            var fontBody = new Font(family, 11, FontStyle.Bold);

            var brush = Brushes.Black;
            var penDash = new Pen(Color.Black, 1f) { DashPattern = new float[] { 3, 3 } };

            float y = 10;

            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("PHIẾU BẾP", fontTitle, brush, new RectangleF(margin, y, usableWidth, 26), sfCenter);
                y += 26;
                g.DrawString(string.IsNullOrEmpty(tableName) ? "Mang đi" : tableName, fontSubTitle, brush, new RectangleF(margin, y, usableWidth, 22), sfCenter);
                y += 24;
            }

            g.DrawLine(penDash, margin, y, width - margin, y);
            y += 8;

            foreach (var item in completedItems)
            {
                string lineText = $"{item.Quantity} x {item.ProductName}";
                if (!string.IsNullOrEmpty(item.Note))
                {
                    lineText += $" ({item.Note})";
                }

                var sizeText = g.MeasureString(lineText, fontBody, (int)usableWidth);
                g.DrawString(lineText, fontBody, brush, new RectangleF(margin, y, usableWidth, sizeText.Height));
                y += sizeText.Height + 6;
            }

            g.DrawLine(penDash, margin, y, width - margin, y);
        }
    }
}
