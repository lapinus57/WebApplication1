using System.Collections.Generic;

namespace Client.Helpers
{
    public static class AppSettings
    {
        private static readonly Dictionary<string, object> _values = new();
        public static string Get(string key, string defaultValue)
        {
            if (_values.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        public static void Set(string key, object value)
        {
            _values[key] = value;
        }

        public static Client.Models.UserInfo? CurrentSelectedUser { get; set; }
    }
}
