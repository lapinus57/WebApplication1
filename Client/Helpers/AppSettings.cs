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
    }
}
