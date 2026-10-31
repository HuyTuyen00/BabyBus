using System;
using System.Collections.Generic;

namespace CafeManagement.Models
{
    public class DiningTable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = "Empty"; // "Empty" | "Occupied"
        public int SortOrder { get; set; }

        // Runtime display helpers (loaded from active order)
        public DateTime? OccupiedSince { get; set; }
        public List<string> CurrentItems { get; set; } = new();
        public decimal TotalAmount { get; set; }

        public bool IsOccupied => (Status == "Occupied" || TotalAmount > 0) && (TotalAmount > 0 || OccupiedSince != null);
        public bool IsPrinted { get; set; }

        public string ElapsedText
        {
            get
            {
                if (!IsOccupied || OccupiedSince == null) return string.Empty;
                int totalMins = (int)(DateTime.Now - OccupiedSince.Value).TotalMinutes;
                if (totalMins <= 0) return "⏱️ Vừa vào";
                if (totalMins < 60) return $"⏱️ {totalMins}p";
                int hours = totalMins / 60;
                int mins = totalMins % 60;
                return mins > 0 ? $"⏱️ {hours}h{mins}p" : $"⏱️ {hours}h";
            }
        }

        public string ItemsSummary => CurrentItems.Count > 0
            ? string.Join(", ", CurrentItems)
            : string.Empty;
    }
}
