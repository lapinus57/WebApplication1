using System;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public class ConnectionConfig
    {
        public string ServerAddress { get; set; } = string.Empty;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyeChat", "connection.json");

        public static ConnectionConfig Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonConvert.DeserializeObject<ConnectionConfig>(json) ?? new ConnectionConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[ConnectionConfig] Load failed", ex, "CLI26");
            }
            return new ConnectionConfig();
        }

        public static void Save(ConnectionConfig config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogException("[ConnectionConfig] Save failed", ex, "CLI27");
            }
        }
    }
}
