using System;
using System.IO;
using System.Text.Json;

namespace CafeManagement.Utilities
{
    public class AppSettings
    {
        public string PrinterName { get; set; } = "";
        public string KitchenPrinterName { get; set; } = "";
        public int TempInvoiceCopies { get; set; } = 1;
        public int PaidInvoiceCopies { get; set; } = 1;
        public int KitchenInvoiceCopies { get; set; } = 1;
    }

    public static class SettingsHelper
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static AppSettings? _current;

        public static AppSettings Current
        {
            get
            {
                if (_current == null)
                {
                    Load();
                }
                return _current!;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _current = JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch
            {
                // Ignore
            }

            if (_current == null)
            {
                _current = new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                _current = settings;
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
