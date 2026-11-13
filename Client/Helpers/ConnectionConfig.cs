using System;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public class ConnectionConfig
    {
        public string ServerAddress { get; set; } = string.Empty;

        /// <summary>Clé du poste exigée par le serveur (Administration:StationKey). Vide si le serveur n'en demande pas.</summary>
        public string StationKey { get; set; } = string.Empty;

        /// <summary>Renvoie la clé du poste, ou null s'il n'y en a pas.</summary>
        public static string? LoadStationKey()
        {
            var key = Load().StationKey;
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }

        /// <summary>Change l'adresse du serveur en gardant la clé du poste.</summary>
        public static void SaveServerAddress(string address)
        {
            var config = Load();
            config.ServerAddress = address;
            Save(config);
        }

        /// <summary>Enregistre la clé du poste en gardant l'adresse du serveur.</summary>
        public static void SaveStationKey(string key)
        {
            var config = Load();
            config.StationKey = key.Trim();
            Save(config);
        }

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
