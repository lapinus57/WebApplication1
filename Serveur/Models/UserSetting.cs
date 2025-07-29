namespace ChatServeur
{
    public class UserSetting
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string SettingsJson { get; set; } = string.Empty;
    }
}
