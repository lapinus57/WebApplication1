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
        private string? _editedUserName;
        private string? _editedWorkstationName;

        public ObservableCollection<string> UserNames { get; } = new();
        public ObservableCollection<string> WorkstationNames { get; } = new();
        public ObservableCollection<string> Rooms { get; } = new();
        public ObservableCollection<ExamOption> Exams { get; } = ExamOption.Load();

        public ConfigurationBuilderPage()
        {
            InitializeComponent();
            Loaded += ConfigurationBuilderPage_Loaded;
        }

        private async void ConfigurationBuilderPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ConfigurationBuilderPage_Loaded;
            try
            {
                var serverExams = await App.ChatService.GetExamOptionsAsync();
                if (serverExams?.Any() == true)
                    ReplaceExams(serverExams);
                ExamStatusText.Text = $"{Exams.Count} examen(s) existant(s) chargé(s).";
            }
            catch (Exception ex)
            {
                ExamStatusText.Text = $"Examens locaux chargés. Serveur indisponible : {ex.Message}";
            }
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

            if (_users.Any(user => !string.Equals(user.Username, _editedUserName, StringComparison.OrdinalIgnoreCase) && string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                UserStatusText.Text = $"« {username} » existe déjà dans cette configuration.";
                return;
            }

            var existing = _users.FirstOrDefault(user => string.Equals(user.Username, _editedUserName, StringComparison.OrdinalIgnoreCase));
            var user = existing ?? new UserInfo
            {
                Username = username,
                DisplayName = username,
                IsOnline = false
            };

            if (existing is null)
            {
                _users.Add(user);
                UserNames.Add(username);
            }
            else
            {
                var listIndex = UserNames.IndexOf(_editedUserName!);
                _settings.Remove(_editedUserName!);
                existing.Username = username;
                existing.DisplayName = username;
                UserNames[listIndex] = username;
            }
            _settings[username] = System.Text.Json.JsonSerializer.Serialize(BuildSettings(), new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            UserStatusText.Text = _editedUserName is null ? $"Utilisateur « {username} » ajouté ({_users.Count} au total)." : $"Utilisateur « {username} » modifié.";
            ClearUserForm();
            EndUserEdit();
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string name }) return;
            _editedUserName = name;
            UserNameBox.Text = name;
            if (_settings.TryGetValue(name, out var json))
            {
                var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                InitialsBox.Text = GetValue(values, "Initials");
                var boxes = UserShortcutBoxes();
                foreach (var (key, box) in boxes) box.Text = GetValue(values, key);
            }
            SaveUserButton.Content = "Enregistrer les modifications";
            CancelUserEditButton.Visibility = Visibility.Visible;
        }

        private void CancelUserEdit_Click(object sender, RoutedEventArgs e) { ClearUserForm(); EndUserEdit(); }
        private void EndUserEdit() { _editedUserName = null; SaveUserButton.Content = "Ajouter cet utilisateur"; CancelUserEditButton.Visibility = Visibility.Collapsed; }
        private static string GetValue(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : string.Empty;
        private IEnumerable<(string Key, TextBox Box)> UserShortcutBoxes()
        {
            yield return ("ShortcutF5Refraction", F5RefractionBox); yield return ("ShortcutF5Lentilles", F5LentillesBox); yield return ("ShortcutF5Pathologies", F5PathologiesBox); yield return ("ShortcutF5Orthoptie", F5OrthoptieBox);
            yield return ("ShortcutF6Refraction", F6RefractionBox); yield return ("ShortcutF6Lentilles", F6LentillesBox); yield return ("ShortcutF6Pathologies", F6PathologiesBox); yield return ("ShortcutF6Orthoptie", F6OrthoptieBox);
            yield return ("ShortcutF7Refraction", F7RefractionBox); yield return ("ShortcutF7Lentilles", F7LentillesBox); yield return ("ShortcutF7Pathologies", F7PathologiesBox); yield return ("ShortcutF7Orthoptie", F7OrthoptieBox);
            yield return ("ShortcutF8Refraction", F8RefractionBox); yield return ("ShortcutF8Lentilles", F8LentillesBox); yield return ("ShortcutF8Pathologies", F8PathologiesBox); yield return ("ShortcutF8Orthoptie", F8OrthoptieBox);
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

            if (_workstations.Any(item => !string.Equals(item.Name, _editedWorkstationName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                WorkstationStatusText.Text = $"« {name} » existe déjà dans cette configuration.";
                return;
            }

            var workstation = _workstations.FirstOrDefault(item => string.Equals(item.Name, _editedWorkstationName, StringComparison.OrdinalIgnoreCase));
            var configuration = workstation ?? new WorkstationConfiguration();
            configuration.Name = name;
            configuration.ShiftF9Exam = SelectedExamId(ShiftF9Box);
            configuration.CtrlF9Exam = SelectedExamId(CtrlF9Box);
            configuration.ShiftF10Exam = SelectedExamId(ShiftF10Box);
            configuration.CtrlF10Exam = SelectedExamId(CtrlF10Box);
            configuration.ShiftF11Exam = SelectedExamId(ShiftF11Box);
            configuration.CtrlF11Exam = SelectedExamId(CtrlF11Box);
            configuration.ShiftF12Exam = SelectedExamId(ShiftF12Box);
            configuration.CtrlF12Exam = SelectedExamId(CtrlF12Box);
            if (workstation is null)
            {
                _workstations.Add(configuration); WorkstationNames.Add(name);
            }
            else WorkstationNames[WorkstationNames.IndexOf(_editedWorkstationName!)] = name;
            WorkstationStatusText.Text = _editedWorkstationName is null ? $"Poste « {name} » ajouté ({_workstations.Count} au total)." : $"Poste « {name} » modifié.";
            ClearWorkstationForm(); EndWorkstationEdit();
        }

        private static string SelectedExamId(ComboBox box) => box.SelectedValue?.ToString() ?? string.Empty;
        private void EditWorkstation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string name }) return;
            var item = _workstations.First(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            _editedWorkstationName = name; WorkstationNameBox.Text = item.Name;
            ShiftF9Box.SelectedValue = item.ShiftF9Exam; CtrlF9Box.SelectedValue = item.CtrlF9Exam; ShiftF10Box.SelectedValue = item.ShiftF10Exam; CtrlF10Box.SelectedValue = item.CtrlF10Exam;
            ShiftF11Box.SelectedValue = item.ShiftF11Exam; CtrlF11Box.SelectedValue = item.CtrlF11Exam; ShiftF12Box.SelectedValue = item.ShiftF12Exam; CtrlF12Box.SelectedValue = item.CtrlF12Exam;
            SaveWorkstationButton.Content = "Enregistrer les modifications"; CancelWorkstationEditButton.Visibility = Visibility.Visible;
        }
        private void CancelWorkstationEdit_Click(object sender, RoutedEventArgs e) { ClearWorkstationForm(); EndWorkstationEdit(); }
        private void ClearWorkstationForm() { WorkstationNameBox.Text = string.Empty; foreach (var box in WorkstationExamBoxes()) box.SelectedIndex = -1; }
        private IEnumerable<ComboBox> WorkstationExamBoxes() => new[] { ShiftF9Box, CtrlF9Box, ShiftF10Box, CtrlF10Box, ShiftF11Box, CtrlF11Box, ShiftF12Box, CtrlF12Box };
        private void EndWorkstationEdit() { _editedWorkstationName = null; SaveWorkstationButton.Content = "Ajouter ce poste"; CancelWorkstationEditButton.Visibility = Visibility.Collapsed; }

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

            if (!TryValidateExamNames(out var validationMessage))
            {
                ExamStatusText.Text = validationMessage;
                RoomStatusText.Text = validationMessage;
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

                var payload = new RoomConfigurationFile { Exams = Exams.ToList(), Rooms = Rooms.ToList() };
                await WriteFileAsync(file, JsonConvert.SerializeObject(payload, Formatting.Indented));
                RoomStatusText.Text = $"Fichier salles enregistré : {file.Name}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d'export", $"Impossible d'enregistrer la configuration des salles : {ex.Message}");
            }
        }

        private void AddExam_Click(object sender, RoutedEventArgs e)
        {
            var name = GenerateExamName("Nouvel examen");
            Exams.Add(new ExamOption
            {
                Index = Exams.Count + 1,
                Name = name,
                Description = name,
                Color = "#FF0000",
                CodeMSG = "examen"
            });
            ExamStatusText.Text = $"Examen « {name} » ajouté. Vous pouvez modifier ses propriétés dans le tableau.";
        }

        private string GenerateExamName(string baseName)
        {
            if (!Exams.Any(exam => string.Equals(exam.Name, baseName, StringComparison.OrdinalIgnoreCase)))
                return baseName;

            var suffix = 2;
            while (Exams.Any(exam => string.Equals(exam.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
                suffix++;
            return $"{baseName} {suffix}";
        }

        private void ExamColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (sender.DataContext is ExamOption exam)
                exam.Color = ColorUtils.ToHex(args.NewColor);
        }

        private void DuplicateExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ExamOption source }) return;
            var duplicate = new ExamOption
            {
                Name = GenerateExamName(source.Name),
                Description = source.Description,
                Color = source.Color,
                CodeMSG = source.CodeMSG,
                Annotation = source.Annotation,
                EndAnnotation = source.EndAnnotation,
                Floor = source.Floor
            };
            var index = Exams.IndexOf(source) + 1;
            Exams.Insert(index, duplicate);
            ReindexExams();
            ExamStatusText.Text = $"Examen « {source.DisplayLabel} » dupliqué.";
        }

        private void MoveExamUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ExamOption exam }) return;
            var index = Exams.IndexOf(exam);
            if (index <= 0) return;
            Exams.Move(index, index - 1);
            ReindexExams();
        }

        private void MoveExamDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ExamOption exam }) return;
            var index = Exams.IndexOf(exam);
            if (index < 0 || index >= Exams.Count - 1) return;
            Exams.Move(index, index + 1);
            ReindexExams();
        }

        private void DeleteExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ExamOption exam }) return;
            Exams.Remove(exam);
            var clearedShortcutCount = ClearWorkstationExamReferences(exam.Id);
            ReindexExams();
            ExamStatusText.Text = clearedShortcutCount == 0
                ? $"Examen « {exam.DisplayLabel} » supprimé."
                : $"Examen « {exam.DisplayLabel} » supprimé. {clearedShortcutCount} raccourci(s) de poste associé(s) ont été effacés.";
        }

        private int ClearWorkstationExamReferences(string examId)
        {
            var clearedShortcutCount = 0;
            foreach (var workstation in _workstations)
            {
                void ClearIfMatching(string shortcutExamId, Action clear)
                {
                    if (!string.Equals(shortcutExamId, examId, StringComparison.OrdinalIgnoreCase))
                        return;

                    clear();
                    clearedShortcutCount++;
                }

                ClearIfMatching(workstation.ShiftF9Exam, () => workstation.ShiftF9Exam = string.Empty);
                ClearIfMatching(workstation.CtrlF9Exam, () => workstation.CtrlF9Exam = string.Empty);
                ClearIfMatching(workstation.ShiftF10Exam, () => workstation.ShiftF10Exam = string.Empty);
                ClearIfMatching(workstation.CtrlF10Exam, () => workstation.CtrlF10Exam = string.Empty);
                ClearIfMatching(workstation.ShiftF11Exam, () => workstation.ShiftF11Exam = string.Empty);
                ClearIfMatching(workstation.CtrlF11Exam, () => workstation.CtrlF11Exam = string.Empty);
                ClearIfMatching(workstation.ShiftF12Exam, () => workstation.ShiftF12Exam = string.Empty);
                ClearIfMatching(workstation.CtrlF12Exam, () => workstation.CtrlF12Exam = string.Empty);
            }

            return clearedShortcutCount;
        }

        private bool TryValidateExamNames(out string message)
        {
            if (Exams.Any(exam => string.IsNullOrWhiteSpace(exam.Name)))
            {
                message = "Le nom de chaque examen est obligatoire. Corrigez le tableau avant l’export.";
                return false;
            }

            var duplicateName = Exams
                .GroupBy(exam => exam.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (duplicateName is not null)
            {
                message = $"« {duplicateName} » est utilisé par plusieurs examens. Chaque nom doit être unique avant l’export.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private void ReindexExams()
        {
            for (var index = 0; index < Exams.Count; index++)
                Exams[index].Index = index + 1;
        }

        private async void ImportExams_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".eyechatconfig");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            try
            {
                var payload = JsonConvert.DeserializeObject<RoomConfigurationFile>(await FileIO.ReadTextAsync(file));
                if (payload?.Exams is null) { ExamStatusText.Text = "Ce fichier ne contient aucun examen."; return; }
                ReplaceExams(payload.Exams);
                ExamStatusText.Text = $"{Exams.Count} examen(s) importé(s) depuis {file.Name}.";
            }
            catch (Exception ex) { await ShowMessageAsync("Erreur d’import", $"Impossible d’importer les examens : {ex.Message}"); }
        }

        private void ReplaceExams(IEnumerable<ExamOption> exams)
        {
            Exams.Clear();
            foreach (var exam in exams.Where(item => item is not null).OrderBy(item => item.Index))
            {
                exam.Normalize();
                Exams.Add(exam);
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
