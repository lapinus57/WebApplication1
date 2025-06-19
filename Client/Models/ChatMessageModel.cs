using System;

namespace Client.Models
{
    public class ChatMessageModel
    {
        public string Avatar { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string IrcHeader { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string TimeFormatted => Timestamp.ToString("HH:mm");
    }
}
