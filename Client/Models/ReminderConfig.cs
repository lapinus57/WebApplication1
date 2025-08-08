using System.Collections.Generic;

namespace Client.Models
{
    public class ReminderConfig
    {
        public string Message { get; set; } = string.Empty;
        public List<string> Times { get; set; } = new();
        public bool IsEnabled { get; set; }
    }
}
