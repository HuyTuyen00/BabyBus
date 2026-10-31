using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CafeManagement.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string? ImagePath { get; set; }
        public bool IsExcludeFromKitchen { get; set; }

        // Helper property for join query
        public string CategoryName { get; set; } = string.Empty;

        // Dynamic ImageSource for WPF binding (safely handles spaces/accents in absolute paths and avoids file locks)
        public ImageSource? ImageSource
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
                        return null;

                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(ImagePath);
                    bi.EndInit();
                    bi.Freeze(); // Crucial for thread-safe cross-thread UI binding in WPF
                    return bi;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
