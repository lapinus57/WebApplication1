using System;
using System.Collections.Generic;
using System.IO;
using Client.Models;
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
        /// Enable agenda-based connection for this workstation.
        /// </summary>
        public bool AgendaModeEnabled { get; set; }

        /// <summary>
        /// Enable automatic user switching based on the agenda.
        /// </summary>
        public bool AutoSwitchEnabled { get; set; }

        /// <summary>
        /// Relaunch the client automatically when the window is closed.
        /// </summary>
        public bool AutoRestartOnClose { get; set; }

        /// <summary>
        /// Agenda rules used to decide which user should be connected.
        /// </summary>
        public List<AgendaSwitchEntry> AgendaSchedule { get; set; } = new();

        /// <summary>
        /// Delay in minutes before highlighting waiting patients.
        /// </summary>
        public int PickupAlertThresholdMinutes { get; set; }

        /// <summary>
        /// Exam shortcuts are workstation settings: they must remain unchanged when the
        /// connected EyeChat user changes.
        /// </summary>
        public string ShiftF9Exam { get; set; } = string.Empty;
        public string CtrlF9Exam { get; set; } = string.Empty;
        public string ShiftF10Exam { get; set; } = string.Empty;
        public string CtrlF10Exam { get; set; } = string.Empty;
        public string ShiftF11Exam { get; set; } = string.Empty;
        public string CtrlF11Exam { get; set; } = string.Empty;
        public string ShiftF12Exam { get; set; } = string.Empty;
        public string CtrlF12Exam { get; set; } = string.Empty;

        public string GetExamShortcut(string key) => key switch
        {
            nameof(ShiftF9Exam) => ShiftF9Exam,
            nameof(CtrlF9Exam) => CtrlF9Exam,
            nameof(ShiftF10Exam) => ShiftF10Exam,
            nameof(CtrlF10Exam) => CtrlF10Exam,
            nameof(ShiftF11Exam) => ShiftF11Exam,
            nameof(CtrlF11Exam) => CtrlF11Exam,
            nameof(ShiftF12Exam) => ShiftF12Exam,
            nameof(CtrlF12Exam) => CtrlF12Exam,
            _ => string.Empty
        };

        public bool SetExamShortcut(string key, string value)
        {
            value ??= string.Empty;
            switch (key)
            {
                case nameof(ShiftF9Exam): ShiftF9Exam = value; break;
                case nameof(CtrlF9Exam): CtrlF9Exam = value; break;
                case nameof(ShiftF10Exam): ShiftF10Exam = value; break;
                case nameof(CtrlF10Exam): CtrlF10Exam = value; break;
                case nameof(ShiftF11Exam): ShiftF11Exam = value; break;
                case nameof(CtrlF11Exam): CtrlF11Exam = value; break;
                case nameof(ShiftF12Exam): ShiftF12Exam = value; break;
                case nameof(CtrlF12Exam): CtrlF12Exam = value; break;
                default: return false;
            }

            return true;
        }

        /// <summary>
        /// OLE DB provider to use when connecting to the Access database.
        /// </summary>
        public string AccessOleDbProvider { get; set; } = "Microsoft.ACE.OLEDB.12.0";

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

        public string? GetAgendaUser(DateTime now)
        {
            if (!AgendaModeEnabled || !AutoSwitchEnabled)
            {
                return null;
            }

            if (AgendaSchedule == null || AgendaSchedule.Count == 0)
            {
                return null;
            }

            var time = now.TimeOfDay;
            foreach (var entry in AgendaSchedule)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.Day != now.DayOfWeek)
                {
                    continue;
                }

                var user = entry.UserName?.Trim();
                if (string.IsNullOrWhiteSpace(user))
                {
                    continue;
                }

                if (entry.IsAllDay)
                {
                    return user;
                }

                var start = entry.StartTime;
                var end = entry.EndTime;
                if (start == end)
                {
                    return user;
                }

                if (time >= start && time < end)
                {
                    return user;
                }
            }

            return null;
        }
    }
}
