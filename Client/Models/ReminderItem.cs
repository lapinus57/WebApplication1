using System.Collections.Generic;

namespace Client.Models
{
    public class ReminderItem
    {
        public string Message { get; set; } = string.Empty;
        public List<string> Times { get; set; } = new();
    }
}
