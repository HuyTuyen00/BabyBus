using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CafeManagement.Models
{
    public class OrderDetail : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalPrice));
                }
            }
        }

        private decimal _price;
        public decimal Price
        {
            get => _price;
            set
            {
                if (_price != value)
                {
                    _price = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalPrice));
                }
            }
        }

        public string? Note { get; set; }

        // Helper properties for UI data binding
        public string ProductName { get; set; } = string.Empty;
        public decimal TotalPrice => Price * Quantity;

        private bool _isCancelled = false;
        public bool IsCancelled
        {
            get => _isCancelled;
            set
            {
                if (_isCancelled != value)
                {
                    _isCancelled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayProductName));
                }
            }
        }

        public string DisplayProductName => IsCancelled ? $"{ProductName} (Đã xóa)" : ProductName;

        private string _kitchenStatus = "Draft";
        public string KitchenStatus
        {
            get => _kitchenStatus;
            set
            {
                if (_kitchenStatus != value)
                {
                    _kitchenStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
