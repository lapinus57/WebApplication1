using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Client.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Newtonsoft.Json;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.System;
using WinRT.Interop;

namespace Client.Pages
{
    public sealed partial class ConfigurationBuilderPage : Page
    {
        private readonly List<UserInfo> _users = new();
        private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<WorkstationConfiguration> _workstations = new();

        public ObservableCollection<string> UserNames { get; } = new();
        public ObservableCollection<string> WorkstationNames { get; } = new();
        public ObservableCollection<string> Rooms { get; } = new();

        public ConfigurationBuilderPage()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var username = UserNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                UserStatusText.Text = "Le nom de l'utilisateur est obligatoire.";
                return;
            }

            if (_users.Any(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                UserStatusText.Text = $"« {username} » existe déjà dans cette configuration.";
                return;
            }

            var user = new UserInfo
            {
                Username = username,
                DisplayName = username,
                IsOnline = false
            };

            _users.Add(user);
            UserNames.Add(username);
            _settings[username] = System.Text.Json.JsonSerializer.Serialize(BuildSettings(), new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            UserStatusText.Text = $"Utilisateur « {username} » ajouté ({_users.Count} au total).";
            ClearUserForm();
        }

        private Dictionary<string, string> BuildSettings() => new()
        {
            ["Initials"] = InitialsBox.Text.Trim(),
            ["ShortcutF5Refraction"] = F5RefractionBox.Text,
            ["ShortcutF5Lentilles"] = F5LentillesBox.Text,
            ["ShortcutF5Pathologies"] = F5PathologiesBox.Text,
            ["ShortcutF5Orthoptie"] = F5OrthoptieBox.Text,
            ["ShortcutF6Refraction"] = F6RefractionBox.Text,
            ["ShortcutF6Lentilles"] = F6LentillesBox.Text,
            ["ShortcutF6Pathologies"] = F6PathologiesBox.Text,
            ["ShortcutF6Orthoptie"] = F6OrthoptieBox.Text,
            ["ShortcutF7Refraction"] = F7RefractionBox.Text,
            ["ShortcutF7Lentilles"] = F7LentillesBox.Text,
            ["ShortcutF7Pathologies"] = F7PathologiesBox.Text,
            ["ShortcutF7Orthoptie"] = F7OrthoptieBox.Text,
            ["ShortcutF8Refraction"] = F8RefractionBox.Text,
            ["ShortcutF8Lentilles"] = F8LentillesBox.Text,
            ["ShortcutF8Pathologies"] = F8PathologiesBox.Text,
            ["ShortcutF8Orthoptie"] = F8OrthoptieBox.Text
        };

        private void ClearUserForm()
        {
            UserNameBox.Text = string.Empty;
            InitialsBox.Text = string.Empty;

            foreach (var box in new[]
            {
                F5RefractionBox, F5LentillesBox, F5PathologiesBox, F5OrthoptieBox,
                F6RefractionBox, F6LentillesBox, F6PathologiesBox, F6OrthoptieBox,
                F7RefractionBox, F7LentillesBox, F7PathologiesBox, F7OrthoptieBox,
                F8RefractionBox, F8LentillesBox, F8PathologiesBox, F8OrthoptieBox
            })
            {
                box.Text = string.Empty;
            }
        }

        private async void ExportUsers_Click(object sender, RoutedEventArgs e)
        {
            if (_users.Count == 0)
            {
                UserStatusText.Text = "Ajoutez au moins un utilisateur avant l'export.";
                return;
            }

            try
            {
                var file = await PickSaveFileAsync(
                    "Configuration utilisateurs EyeChat",
                    ".eyechatusers",
                    $"EyeChatUsers_{DateTime.Now:yyyyMMdd_HHmm}");
                if (file is null)
                    return;

                var payload = new UserConfigurationFile
                {
                    Users = _users,
                    Settings = _settings
                };

                await WriteFileAsync(file, JsonConvert.SerializeObject(payload, Formatting.Indented));
                UserStatusText.Text = $"Fichier utilisateurs enregistré : {file.Name}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d'export", $"Impossible d'enregistrer la configuration utilisateurs : {ex.Message}");
            }
        }

        private void AddWorkstation_Click(object sender, RoutedEventArgs e)
        {
            var name = WorkstationNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                WorkstationStatusText.Text = "Le nom du poste est obligatoire.";
                return;
            }

            if (_workstations.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                WorkstationStatusText.Text = $"« {name} » existe déjà dans cette configuration.";
                return;
            }

            _workstations.Add(new WorkstationConfiguration
            {
                Name = name,
                ShiftF9Exam = ShiftF9Box.Text.Trim(),
                CtrlF9Exam = CtrlF9Box.Text.Trim(),
                ShiftF10Exam = ShiftF10Box.Text.Trim(),
                CtrlF10Exam = CtrlF10Box.Text.Trim(),
                ShiftF11Exam = ShiftF11Box.Text.Trim(),
                CtrlF11Exam = CtrlF11Box.Text.Trim(),
                ShiftF12Exam = ShiftF12Box.Text.Trim(),
                CtrlF12Exam = CtrlF12Box.Text.Trim()
            });
            WorkstationNames.Add(name);
            WorkstationStatusText.Text = $"Poste « {name} » ajouté ({_workstations.Count} au total).";
            WorkstationNameBox.Text = string.Empty;
            foreach (var box in new[] { ShiftF9Box, CtrlF9Box, ShiftF10Box, CtrlF10Box, ShiftF11Box, CtrlF11Box, ShiftF12Box, CtrlF12Box })
                box.Text = string.Empty;
        }

        private async void ExportWorkstations_Click(object sender, RoutedEventArgs e)
        {
            if (_workstations.Count == 0)
            {
                WorkstationStatusText.Text = "Ajoutez au moins un poste avant l’export.";
                return;
            }

            try
            {
                var file = await PickSaveFileAsync(
                    "Configuration postes EyeChat",
                    ".eyechatpostes",
                    $"EyeChatPostes_{DateTime.Now:yyyyMMdd_HHmm}");
                if (file is null)
                    return;

                var payload = new WorkstationConfigurationFile { Workstations = _workstations };
                await WriteFileAsync(file, JsonConvert.SerializeObject(payload, Formatting.Indented));
                WorkstationStatusText.Text = $"Fichier postes enregistré : {file.Name}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d’export", $"Impossible d’enregistrer la configuration des postes : {ex.Message}");
            }
        }

        private void AddRoom_Click(object sender, RoutedEventArgs e) => AddRoom();

        private void RoomNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                AddRoom();
                e.Handled = true;
            }
        }

        private void AddRoom()
        {
            var room = RoomNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(room))
            {
                RoomStatusText.Text = "Le nom de la salle est obligatoire.";
                return;
            }

            if (Rooms.Any(existing => string.Equals(existing, room, StringComparison.OrdinalIgnoreCase)))
            {
                RoomStatusText.Text = $"« {room} » existe déjà dans cette configuration.";
                return;
            }

            Rooms.Add(room);
            RoomNameBox.Text = string.Empty;
            RoomStatusText.Text = $"Salle « {room} » ajoutée ({Rooms.Count} au total).";
        }

        private void DeleteRoom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string room })
            {
                Rooms.Remove(room);
                RoomStatusText.Text = $"Salle « {room} » supprimée.";
            }
        }

        private async void ExportRooms_Click(object sender, RoutedEventArgs e)
        {
            if (Rooms.Count == 0)
            {
                RoomStatusText.Text = "Ajoutez au moins une salle avant l'export.";
                return;
            }

            try
            {
                var file = await PickSaveFileAsync(
                    "Configuration EyeChat",
                    ".eyechatconfig",
                    $"EyeChatRooms_{DateTime.Now:yyyyMMdd_HHmm}");
                if (file is null)
                    return;

                var payload = new RoomConfigurationFile { Rooms = Rooms.ToList() };
                await WriteFileAsync(file, JsonConvert.SerializeObject(payload, Formatting.Indented));
                RoomStatusText.Text = $"Fichier salles enregistré : {file.Name}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d'export", $"Impossible d'enregistrer la configuration des salles : {ex.Message}");
            }
        }

        private static async Task<StorageFile?> PickSaveFileAsync(string label, string extension, string suggestedName)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add(label, new List<string> { extension });
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            return await picker.PickSaveFileAsync();
        }

        private static async Task WriteFileAsync(StorageFile file, string contents)
        {
            CachedFileManager.DeferUpdates(file);
            await FileIO.WriteTextAsync(file, contents);
            await CachedFileManager.CompleteUpdatesAsync(file);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }
    }

    internal sealed class UserConfigurationFile
    {
        public List<UserInfo> Users { get; set; } = new();
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    internal sealed class WorkstationConfigurationFile
    {
        public List<WorkstationConfiguration> Workstations { get; set; } = new();
    }

    internal sealed class WorkstationConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string ShiftF9Exam { get; set; } = string.Empty;
        public string CtrlF9Exam { get; set; } = string.Empty;
        public string ShiftF10Exam { get; set; } = string.Empty;
        public string CtrlF10Exam { get; set; } = string.Empty;
        public string ShiftF11Exam { get; set; } = string.Empty;
        public string CtrlF11Exam { get; set; } = string.Empty;
        public string ShiftF12Exam { get; set; } = string.Empty;
        public string CtrlF12Exam { get; set; } = string.Empty;
    }

    internal sealed class RoomConfigurationFile
    {
        public List<ExamOption>? Exams { get; set; }
        public List<string> Rooms { get; set; } = new();
    }
}
