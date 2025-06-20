using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Client.Models;

namespace Client.Helpers
{
    public static class AppSettings
    {
        private static readonly string filePath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        private static Dictionary<string, JsonElement> _settings = new();
        public static UserInfo? CurrentSelectedUser { get; set; }

        static AppSettings()
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                                ?? new();
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
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Sauvegarde échouée : {ex.Message}");
            }
        }
    }
}
