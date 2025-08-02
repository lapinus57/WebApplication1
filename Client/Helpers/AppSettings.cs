using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Client.Models;
using Client;

namespace Client.Helpers
{
    public static class AppSettings
    {
        private static Dictionary<string, JsonElement> _settings = new();

        private static string FilePath =>
              Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyeChat",
                $"{(App.UserName)}_settings.json");

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
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                                ?? new();
                }
                else
                {
                    _settings = new();
                }
            }
            catch
            {
                _settings = new();
            }
        }

        public static string Get(string key, string defaultValue = "")
        {
            return _settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? defaultValue
                : defaultValue;
        }

        public static T GetObject<T>(string key) where T : new()
        {
            try
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    return value.Deserialize<T>() ?? new T();
                }
            }
            catch { }
            return new T();
        }

        public static void Set(string key, string value)
        {
            _settings[key] = JsonDocument.Parse($"\"{value}\"").RootElement;
            Save();
        }

        public static void SetObject<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            _settings[key] = JsonDocument.Parse(json).RootElement;
            Save();
        }

        public static string Export()
        {
            try
            {
                return JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void Import(string json)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value.ValueKind == JsonValueKind.Null)
                            continue;
                        _settings[kvp.Key] = kvp.Value;
                    }
                    Save();
                }
            }
            catch
            {
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Sauvegarde échouée : {ex.Message}");
            }
        }

        public static void DeleteSettingsFile()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }
}
