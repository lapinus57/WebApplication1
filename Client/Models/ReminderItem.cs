using System;
using System.Collections.Generic;

namespace Client.Models
{
    public class ReminderItem
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<DayOfWeek> Days { get; set; } = new();
        public List<string> Times { get; set; } = new();
    }
}
