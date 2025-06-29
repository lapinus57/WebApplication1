using System;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public class MachineConfig
    {
        public string RoomName { get; set; } = string.Empty;
        public string DefaultUser { get; set; } = string.Empty;
        public string LastUser { get; set; } = string.Empty;
        public bool ConnectLastUser { get; set; }

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyeChat", "machine.json");

        public static MachineConfig Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonConvert.DeserializeObject<MachineConfig>(json) ?? new MachineConfig();
                }
            }
            catch
            {
            }
            return new MachineConfig();
        }

        public static void Save(MachineConfig config)
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