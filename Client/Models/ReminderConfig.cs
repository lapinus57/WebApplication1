using System.Collections.Generic;

namespace Client.Models
{
    public class ReminderConfig
    {
        public List<ReminderItem> Reminders { get; set; } = new();
        public bool IsEnabled { get; set; }
    }
}
