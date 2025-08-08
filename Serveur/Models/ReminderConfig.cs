using System.Collections.Generic;

namespace ChatServeur
{
    public class ReminderConfig
    {
        public List<ReminderItem> Reminders { get; set; } = new();
        public bool IsEnabled { get; set; }
    }
}
