using System.Collections.Generic;

namespace Client.Models
{
    public class UserInfo
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public List<string> Rooms { get; set; } = new();
        public string ColorUserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string Note { get; set; } = string.Empty;
        /// <summary>
        /// Convenience property returning <see cref="DisplayName"/> if set or
        /// <see cref="Username"/> otherwise.
        /// </summary>
        public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
        public string RoomsDisplay => string.Join(", ", Rooms);
    }
}
