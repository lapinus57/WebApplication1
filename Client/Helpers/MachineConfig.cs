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
        /// <summary>
        /// Show the patient time modification menu in the chat page.
        /// </summary>
        public bool ShowTimeModification { get; set; }

        /// <summary>
        /// Show the reminder page in settings.
        /// </summary>
        public bool ShowReminderPage { get; set; }

        /// <summary>
        /// Show the slash command helper list in the chat input.
        /// </summary>
        public bool ShowSlashCommands { get; set; } = true;

        /// <summary>
        /// Delay in minutes before highlighting waiting patients.
        /// </summary>
        public int PickupAlertThresholdMinutes { get; set; }

        /// <summary>
        /// Local path to the Access system database (system.mdw).
        /// </summary>
        public string AccessWorkgroupPath { get; set; } = string.Empty;

        /// <summary>
        /// Optional user name for the Access workgroup.
        /// </summary>
        public string AccessUserName { get; set; } = string.Empty;

        /// <summary>
        /// Optional password for the Access workgroup.
        /// </summary>
        public string AccessPassword { get; set; } = string.Empty;

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
            catch (Exception ex)
            {
                Logger.LogException("[MachineConfig] Load failed", ex, "CLI21");
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
            catch (Exception ex)
            {
                Logger.LogException("[MachineConfig] Save failed", ex, "CLI22");
            }
        }
    }
}