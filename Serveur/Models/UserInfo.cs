namespace ChatServeur
{
    public class UserInfo
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ColorUserName { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}
