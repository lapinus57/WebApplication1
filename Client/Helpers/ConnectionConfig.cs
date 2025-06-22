using System;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public class ConnectionConfig
    {
        public string ServerAddress { get; set; } = "http://localhost:5000";

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
            catch
            {
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
            catch
            {
            }
        }
    }
}
