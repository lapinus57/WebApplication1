using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Client.Models;
using Client;
using System.Text;

namespace Client.Helpers
{
    public static class AppSettings
    {
        private static Dictionary<string, JsonNode?> _settings = new();

        public static event Action? SettingsChanged;

        private static readonly HashSet<char> InvalidFileNameCharacters = new(Path.GetInvalidFileNameChars());

        private static string? GetSettingsFilePath()
        {
            var username = App.UserName;
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var sanitized = SanitizeUserNameForFile(username);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return null;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyeChat",
                $"{sanitized}_settings.json");
        }

        internal static string SanitizeUserNameForFile(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(username.Length);
            foreach (var character in username.Trim())
            {
                if (!InvalidFileNameCharacters.Contains(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static UserInfo? _currentUser;
        public static UserInfo? CurrentSelectedUser
        {
            get => _currentUser;
            set
            {
                if (_currentUser?.Username != value?.Username)
                {
                    _currentUser = value;
                    Set("SelectedUser", value?.Username ?? string.Empty);
                }
            }
        }

        public static List<string> UserOrder
        {
            get => GetObject<List<string>>("UserOrder");
            set => SetObject("UserOrder", value);
        }

        static AppSettings()
        {
            Load();
        }
        public static void Reload()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                var filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath))
                {
                    _settings = new();
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json)
                                ?? new();
                }
                else
                {
                    _settings = new();
                }

                if (EnsureDefaultSettings())
                {
                    Save();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] Load failed", ex, "CLI17");
                _settings = new();
            }
        }

        private static bool EnsureDefaultSettings()
        {
            if (string.IsNullOrWhiteSpace(App.UserName))
                return false;

            var updated = false;

            updated |= EnsureStringSetting("SelectedUser", "A Tous");
            updated |= EnsureStringSetting("Avatar", "ms-appx:///Assets/utilisateur.png");
            updated |= EnsureStringSetting("ChatDisplayStyle", "Modern");
            updated |= EnsureStringSetting("AppTheme", "Dark");

            updated |= EnsureColorsSetting();

            return updated;
        }

        private static bool EnsureStringSetting(string key, string defaultValue)
        {
            if (_settings.TryGetValue(key, out var value) && value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }
            }

            _settings[key] = JsonValue.Create(defaultValue);
            return true;
        }

        private static bool EnsureColorsSetting()
        {
            var defaultColors = new AppColorSettings
            {
                TitleBarColor = "#FF0078D7",
                TextTitleBarColor = "#FFFFFFFF",
                NavigationViewColor = "#FFE6F1FF",
                TextNavigationViewColor = "#FF000000",
                MyMessageColor = "#FFCCE5FF",
                TextMyMessageColor = "#FF000000",
                OtherMessageColor = "#FFD9F2DC",
                TextOtherMessageColor = "#FF000000",
                AppBackgroundColor = "#FFFFFFFF",
                TextAppBackgroundColor = "#FF000000",
                SystemAccentColorDark1 = "#FF0078D7"
            };

            try
            {
                if (_settings.TryGetValue("Colors", out var node) && node is not null)
                {
                    var current = node.Deserialize<AppColorSettings>() ?? new AppColorSettings();
                    var changed = ApplyColorDefaults(current, defaultColors);
                    if (!changed)
                    {
                        return false;
                    }

                    _settings["Colors"] = JsonSerializer.SerializeToNode(current);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] EnsureColorsSetting failed", ex, "CLI21");
            }

            _settings["Colors"] = JsonSerializer.SerializeToNode(defaultColors);
            return true;
        }

        private static bool ApplyColorDefaults(AppColorSettings current, AppColorSettings defaults)
        {
            var updated = false;

            updated |= EnsureColorValue(current, defaults, c => c.TitleBarColor, (c, value) => c.TitleBarColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.TextTitleBarColor, (c, value) => c.TextTitleBarColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.NavigationViewColor, (c, value) => c.NavigationViewColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.TextNavigationViewColor, (c, value) => c.TextNavigationViewColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.MyMessageColor, (c, value) => c.MyMessageColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.TextMyMessageColor, (c, value) => c.TextMyMessageColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.OtherMessageColor, (c, value) => c.OtherMessageColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.TextOtherMessageColor, (c, value) => c.TextOtherMessageColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.AppBackgroundColor, (c, value) => c.AppBackgroundColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.TextAppBackgroundColor, (c, value) => c.TextAppBackgroundColor = value);
            updated |= EnsureColorValue(current, defaults, c => c.SystemAccentColorDark1, (c, value) => c.SystemAccentColorDark1 = value);

            return updated;
        }

        private static bool EnsureColorValue(
            AppColorSettings current,
            AppColorSettings defaults,
            Func<AppColorSettings, string?> getter,
            Action<AppColorSettings, string> setter)
        {
            var value = getter(current);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var defaultValue = getter(defaults);

            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                setter(current, defaultValue);
                return true;
            }

            return true;
        }


        public static string Get(string key, string defaultValue = "")
        {
            if (_settings.TryGetValue(key, out var value) && value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var text) && text is not null)
                {
                    return text;
                }
            }

            return defaultValue;
        }

        public static T GetObject<T>(string key) where T : new()
        {
            try
            {
                if (_settings.TryGetValue(key, out var value) && value is not null)
                {
                    return value.Deserialize<T>() ?? new T();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] GetObject failed", ex, "CLI18");
            }
            return new T();
        }

        public static void Set(string key, string value)
        {
            var safeValue = value ?? string.Empty;
            _settings[key] = JsonValue.Create(safeValue);
            Save();
            NotifySettingsChanged();
        }

        public static void SetObject<T>(string key, T value)
        {
            _settings[key] = JsonSerializer.SerializeToNode(value);
            Save();
            NotifySettingsChanged();
        }

        public static string Export()
        {
            try
            {
                return JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] Export failed", ex, "CLI19");
                return string.Empty;
            }
        }

        public static void Import(string json)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value is null)
                            continue;
                        _settings[kvp.Key] = kvp.Value;
                    }
                    Save();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] Import failed", ex, "CLI20");
            }
        }

        private static void Save()
        {
            try
            {
                var filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Sauvegarde échouée : {ex.Message}");
                Logger.LogException("[AppSettings] Save failed", ex, "CLI10");
            }
        }

        private static void NotifySettingsChanged()
        {
            var handlers = SettingsChanged;
            if (handlers is null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    (handler as Action)?.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.LogException("[AppSettings] SettingsChanged handler failed", ex, "CLI04");
                }
            }
        }

        public static void DeleteSettingsFile()
        {
            try
            {
                var filePath = GetSettingsFilePath();
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppSettings] DeleteSettingsFile failed", ex, "CLI11");
            }
        }
    }
}
