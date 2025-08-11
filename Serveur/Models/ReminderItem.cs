using System;
using System.Collections.Generic;

namespace ChatServeur
{
    public class ReminderItem
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<DayOfWeek> Days { get; set; } = new();
        public List<string> Times { get; set; } = new();
    }
}
