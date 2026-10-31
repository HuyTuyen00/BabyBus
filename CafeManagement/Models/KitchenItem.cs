using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CafeManagement.Models
{
    public class KitchenItem : INotifyPropertyChanged
    {
        private int _completedQuantity;
        private int _printedQuantity;
        private string _status = "Pending";

        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; } // Total quantity
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }

        public int CompletedQuantity
        {
            get => _completedQuantity;
            set
            {
                if (_completedQuantity != value)
                {
                    _completedQuantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RemainingQuantity));
                    OnPropertyChanged(nameof(DisplaySlText));
                    OnPropertyChanged(nameof(DisplayTotalText));
                    OnPropertyChanged(nameof(ItemBgColor));
                    OnPropertyChanged(nameof(ItemFgColor));
                    OnPropertyChanged(nameof(ItemBorderColor));
                    OnPropertyChanged(nameof(IsDoneAny));
                }
            }
        }

        public int PrintedQuantity
        {
            get => _printedQuantity;
            set
            {
                if (_printedQuantity != value)
                {
                    _printedQuantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RemainingQuantity));
                    OnPropertyChanged(nameof(DisplaySlText));
                    OnPropertyChanged(nameof(DisplayTotalText));
                    OnPropertyChanged(nameof(ItemBgColor));
                    OnPropertyChanged(nameof(ItemFgColor));
                    OnPropertyChanged(nameof(ItemBorderColor));
                    OnPropertyChanged(nameof(IsActionEnabled));
                    OnPropertyChanged(nameof(ActionOpacity));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ItemBgColor));
                    OnPropertyChanged(nameof(ItemFgColor));
                    OnPropertyChanged(nameof(ItemBorderColor));
                    OnPropertyChanged(nameof(IsActionEnabled));
                    OnPropertyChanged(nameof(ActionOpacity));
                    OnPropertyChanged(nameof(DisplaySlText));
                }
            }
        }

        // Helper properties for UI data binding
        public string ProductName { get; set; } = string.Empty;
        public string TableNumber { get; set; } = string.Empty;
        public bool HasNote => !string.IsNullOrEmpty(Note);

        public int RemainingQuantity => Math.Max(0, Quantity - PrintedQuantity);
        public bool IsFullyServed => RemainingQuantity == 0 && Quantity > 0;

        // Display SL: [CompletedQuantity] / [RemainingQuantity] or final state
        public string DisplaySlText
        {
            get
            {
                if (IsFullyServed)
                {
                    return $"SL:{Quantity}/{Quantity}";
                }
                return $"SL:{CompletedQuantity}/{RemainingQuantity}";
            }
        }

        public string DisplayTotalText
        {
            get
            {
                if (IsFullyServed) return $"SL: {Quantity}";
                if (PrintedQuantity > 0 && RemainingQuantity > 0) return $"SL: {RemainingQuantity} (Gốc: {Quantity})";
                return $"SL: {Quantity}";
            }
        }

        public bool IsDoneAny => CompletedQuantity > 0;

        // Colors based on user specs:
        // - "Có bất kì số lượng đã làm xong thì màu nền chuyển sang xanh, sau khi in còn số lượng chưa làm thì màu nền quay lại trắng"
        // - "Sau khi làm xong toàn bộ số lượng hiển thị SL: 10/10 và nền món chuyển màu đỏ và các phím trong món đó hiển mờ không cho thao tác thêm."
        public string ItemBgColor
        {
            get
            {
                if (IsFullyServed) return "#fee2e2"; // Red/Pink completed & printed
                if (CompletedQuantity > 0) return "#dcfce7"; // Green when has done items
                return "#ffffff"; // White default
            }
        }

        public string ItemFgColor
        {
            get
            {
                if (IsFullyServed) return "#991b1b"; // Dark Red
                if (CompletedQuantity > 0) return "#15803d"; // Dark Green
                return "#0f172a"; // Dark Slate
            }
        }

        public string ItemBorderColor
        {
            get
            {
                if (IsFullyServed) return "#fca5a5"; // Light Red border
                if (CompletedQuantity > 0) return "#86efac"; // Light Green border
                return "#cbd5e1"; // Slate border
            }
        }

        public bool IsActionEnabled => !IsFullyServed && RemainingQuantity > 0;
        public double ActionOpacity => IsActionEnabled ? 1.0 : 0.35;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
