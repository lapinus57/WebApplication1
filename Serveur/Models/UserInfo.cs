using System.Collections.Generic;

namespace ChatServeur
{
    public class UserInfo
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public List<string> Rooms { get; set; } = new();
        public string DisplayName { get; set; } = string.Empty;
        public string ColorUserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
