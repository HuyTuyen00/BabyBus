using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CafeManagement.Models
{
    public class Order : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public DateTime OrderDate { get; set; }
        public int? TableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Paid";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
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
