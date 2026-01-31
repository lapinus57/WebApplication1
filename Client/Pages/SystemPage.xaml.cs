using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.Dialogs;
using Client.Helpers;
using Client.Models;
using Newtonsoft.Json;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace Client.Pages
{
    public sealed partial class SystemPage : Page
    {
        private const string SettingsPassword = "901027";
        private readonly MachineConfig _config;
        private bool _isLoaded;
        private bool _suppressTimeToggle;
        private bool _suppressReminderToggle;
        private bool _suppressSlashToggle;

        public string RoomName { get; set; } = string.Empty;
        public bool ShowTimeModification { get; set; }
        public bool ShowReminderPage { get; set; }
        public bool ShowSlashCommands { get; set; }
        public ObservableCollection<string> Users { get; } = new();
        public string DefaultUser { get; set; } = string.Empty;
        public bool ConnectLastUser { get; set; }
        public double PickupAlertThresholdMinutes { get; set; }

        public SystemPage()
        {

            this.InitializeComponent();
            _config = MachineConfig.Load();
            ShowTimeModification = _config.ShowTimeModification;
            ShowReminderPage = _config.ShowReminderPage;
            RoomName = _config.RoomName;
            DefaultUser = _config.DefaultUser?.Trim() ?? string.Empty;
            ConnectLastUser = _config.ConnectLastUser;
            ShowSlashCommands = _config.ShowSlashCommands;
            PickupAlertThresholdMinutes = _config.PickupAlertThresholdMinutes;
            var initialUsers = LoadUsernamesFromSettingsFiles();
            UpdateUsersCollection(initialUsers);

            DataContext = this;
            PickupAlertBox.Value = PickupAlertThresholdMinutes;
            Loaded += SystemPage_Loaded;
        }

        private async void SystemPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            await RefreshLocalUserListAsync();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.RoomName = RoomName;
            _config.ShowTimeModification = ShowTimeModification;
            _config.ShowReminderPage = ShowReminderPage;
            _config.ShowSlashCommands = ShowSlashCommands;
            _config.DefaultUser = DefaultUser;
            _config.ConnectLastUser = ConnectLastUser;
            _config.PickupAlertThresholdMinutes = (int)Math.Max(0, Math.Round(PickupAlertThresholdMinutes));
            PickupAlertThresholdMinutes = _config.PickupAlertThresholdMinutes;
            PickupAlertBox.Value = PickupAlertThresholdMinutes;
            MachineConfig.Save(_config);
            App.ChatService.RoomName = RoomName;
            await App.ChatService.UpdateRoomNameAsync(RoomName);
            App.ChatService.PickupAlertThresholdMinutes = _config.PickupAlertThresholdMinutes;
        }

        private async void RenameRoom_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsurePasswordAsync(sender as FrameworkElement))
                return;

            var xamlRoot = GetXamlRoot(sender as FrameworkElement);
            if (xamlRoot is null)
                return;

            var nameBox = new TextBox
            {
                Text = RoomName,
                PlaceholderText = "Nouveau nom de la salle"
            };

            var dialog = new ContentDialog
            {
                Title = "Renommer l'ordinateur",
                PrimaryButtonText = "Valider",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Primary,
                Content = nameBox,
                XamlRoot = xamlRoot
            };

            dialog.PrimaryButtonClick += async (s, args) =>
            {
                var trimmed = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    args.Cancel = true;
                    await ShowInfoDialogAsync("Nom invalide", "Le nom de la salle ne peut pas être vide.", xamlRoot);
                    return;
                }

                RoomName = trimmed;
                _config.RoomName = RoomName;
                RoomBox.Text = RoomName;
            };

            await dialog.ShowAsync();
        }

        private async void TimeModSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _suppressTimeToggle)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.IsOn)
            {
                if (!await EnsurePasswordAsync(toggle))
                {
                    _suppressTimeToggle = true;
                    toggle.IsOn = false;
                    _suppressTimeToggle = false;
                    ShowTimeModification = false;
                    return;
                }
            }
            else
            {
                if (!await ConfirmDisableAsync("Êtes-vous sûr de désactiver l'affichage de la modification du temps ?", toggle))
                {
                    _suppressTimeToggle = true;
                    toggle.IsOn = true;
                    _suppressTimeToggle = false;
                    ShowTimeModification = true;
                    return;
                }
            }

            ShowTimeModification = toggle.IsOn;
        }

        private async void ReminderPageSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _suppressReminderToggle)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.IsOn)
            {
                if (!await EnsurePasswordAsync(toggle))
                {
                    _suppressReminderToggle = true;
                    toggle.IsOn = false;
                    _suppressReminderToggle = false;
                    ShowReminderPage = false;
                    return;
                }
            }
            else
            {
                if (!await ConfirmDisableAsync("Êtes-vous sûr de désactiver l'affichage de la page de rappel ?", toggle))
                {
                    _suppressReminderToggle = true;
                    toggle.IsOn = true;
                    _suppressReminderToggle = false;
                    ShowReminderPage = true;
                    return;
                }
            }

            ShowReminderPage = toggle.IsOn;
        }

        private async void SlashCommandsSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _suppressSlashToggle)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.IsOn)
            {
                if (!await EnsurePasswordAsync(toggle))
                {
                    _suppressSlashToggle = true;
                    toggle.IsOn = false;
                    _suppressSlashToggle = false;
                    ShowSlashCommands = false;
                    return;
                }
            }
            else
            {
                if (!await ConfirmDisableAsync("Êtes-vous sûr de masquer la liste des commandes ?", toggle))
                {
                    _suppressSlashToggle = true;
                    toggle.IsOn = true;
                    _suppressSlashToggle = false;
                    ShowSlashCommands = true;
                    return;
                }
            }

            ShowSlashCommands = toggle.IsOn;
        }

        private async void ManageUsers_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsurePasswordAsync(sender as FrameworkElement))
                return;

            var xamlRoot = GetXamlRoot(sender as FrameworkElement);
            if (xamlRoot is null)
                return;

            var dialog = new UserManagerDialog
            {
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
            await RefreshLocalUserListAsync();
        }

        private async Task RefreshLocalUserListAsync()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var names = new List<string>();

            if (App.ChatService is not null)
            {
                try
                {
                    var localUsers = await App.ChatService.LoadUsersFromDiskAsync();
                    names = localUsers
                        .Select(u => u.Username?.Trim())
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(comparer)
                        .OrderBy(u => u, comparer)
                        .ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemPage] Impossible de charger les utilisateurs via le service : {ex.Message}");
                }
            }

            if (names.Count == 0)
            {
                var fallback = LoadUsernamesFromSettingsFiles();
                if (fallback.Count == 0)
                {
                    UpdateUsersCollection(names);
                    return;
                }

                names = fallback;
            }

            UpdateUsersCollection(names);
        }

        private async Task<bool> EnsurePasswordAsync(FrameworkElement? element)
        {
            var xamlRoot = GetXamlRoot(element);
            if (xamlRoot is null)
                return false;

            while (true)
            {
                var passwordBox = new PasswordBox { PlaceholderText = "Mot de passe", Width = 300 };
                var dialog = new ContentDialog
                {
                    Title = "Mot de passe requis",
                    PrimaryButtonText = "Valider",
                    CloseButtonText = "Annuler",
                    DefaultButton = ContentDialogButton.Primary,
                    Content = passwordBox,
                    XamlRoot = xamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return false;

                if (ValidatePassword(passwordBox.Password))
                    return true;

                await ShowInfoDialogAsync("Accès refusé", "Mot de passe incorrect.", xamlRoot);
            }
        }

        private bool ValidatePassword(string? password) =>
            string.Equals(password, SettingsPassword, StringComparison.Ordinal);

        private async Task<bool> ConfirmDisableAsync(string message, FrameworkElement? element)
        {
            var xamlRoot = GetXamlRoot(element);
            if (xamlRoot is null)
                return false;

            var dialog = new ContentDialog
            {
                Title = "Confirmation",
                Content = message,
                PrimaryButtonText = "Oui",
                CloseButtonText = "Non",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task ShowInfoDialogAsync(string title, string message, XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Fermer",
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }

        private XamlRoot? GetXamlRoot(FrameworkElement? element)
        {
            if (element?.XamlRoot is XamlRoot root)
                return root;

            if (Content is FrameworkElement fe && fe.XamlRoot is not null)
                return fe.XamlRoot;

            return null;
        }

        private void UpdateUsersCollection(IEnumerable<string> names)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var finalNames = names
                .Select(name => name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(comparer)
                .ToList();

            var desiredUser = DefaultUser;

            if (!string.IsNullOrWhiteSpace(desiredUser) &&
                !finalNames.Any(name => comparer.Equals(name, desiredUser)))
            {
                finalNames.Add(desiredUser);
            }

            finalNames.Sort(comparer);

            Users.Clear();
            foreach (var user in finalNames)
                Users.Add(user);

            string? selectedName;

            if (string.IsNullOrWhiteSpace(desiredUser))
            {
                selectedName = finalNames.FirstOrDefault();
            }
            else
            {
                selectedName = finalNames.FirstOrDefault(name => comparer.Equals(name, desiredUser))
                    ?? desiredUser;
            }

            DefaultUser = selectedName ?? string.Empty;

            if (UsersComboBox is not null)
            {
                UsersComboBox.ItemsSource = Users;
                var comboSelection = Users.FirstOrDefault(name => comparer.Equals(name, DefaultUser));
                UsersComboBox.SelectedItem = string.IsNullOrEmpty(DefaultUser) ? null : comboSelection ?? DefaultUser;
            }

            _config.DefaultUser = DefaultUser;
        }

        private static List<string> LoadUsernamesFromSettingsFiles()
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                if (!Directory.Exists(folder))
                    return new List<string>();

                return Directory
                    .GetFiles(folder, "*_settings.json")
                    .Select(file => Path.GetFileName(file).Replace("_settings.json", string.Empty).Trim())
                    .Select(AppSettings.SanitizeUserNameForFile)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemPage] Lecture des utilisateurs locaux impossible : {ex.Message}");
                return new List<string>();
            }
        }

        private async void ExportUsers_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = GetXamlRoot(sender as FrameworkElement);
            if (xamlRoot is null)
                return;

            var users = await App.ChatService.LoadUsersFromDiskAsync();
            if (users.Count == 0)
            {
                await ShowInfoDialogAsync("Export impossible", "Aucun utilisateur local trouvé à exporter.", xamlRoot);
                return;
            }

            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Configuration utilisateurs EyeChat", new List<string> { ".eyechatusers" });
            picker.SuggestedFileName = $"EyeChatUsers_{DateTime.Now:yyyyMMdd_HHmm}";
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            try
            {
                var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var user in users)
                {
                    if (string.IsNullOrWhiteSpace(user.Username))
                        continue;

                    var settingsPath = GetUserSettingsFilePath(user.Username);
                    if (File.Exists(settingsPath))
                    {
                        settings[user.Username] = await File.ReadAllTextAsync(settingsPath);
                    }
                }

                var configuration = new UserExportConfiguration
                {
                    Users = users,
                    Settings = settings
                };

                var json = JsonConvert.SerializeObject(configuration, Formatting.Indented);
                CachedFileManager.DeferUpdates(file);
                await FileIO.WriteTextAsync(file, json);
                await CachedFileManager.CompleteUpdatesAsync(file);
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Erreur d'export", $"Impossible d'enregistrer la configuration : {ex.Message}", xamlRoot);
            }
        }

        private async void ImportUsers_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = GetXamlRoot(sender as FrameworkElement);
            if (xamlRoot is null)
                return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".eyechatusers");
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var configuration = JsonConvert.DeserializeObject<UserExportConfiguration>(json);
                if (configuration?.Users == null)
                {
                    await ShowInfoDialogAsync("Import invalide", "Le fichier sélectionné est vide ou invalide.", xamlRoot);
                    return;
                }

                var users = configuration.Users
                    .Where(u => !string.IsNullOrWhiteSpace(u.Username))
                    .ToList();

                if (users.Count == 0)
                {
                    await ShowInfoDialogAsync("Import invalide", "Aucun utilisateur valide trouvé dans le fichier.", xamlRoot);
                    return;
                }

                var folder = GetLocalDataFolderPath();
                Directory.CreateDirectory(folder);

                BackupFile(Path.Combine(folder, "users.json"));
                foreach (var existing in Directory.GetFiles(folder, "*_settings.json"))
                {
                    BackupFile(existing);
                }

                var usersPath = Path.Combine(folder, "users.json");
                var usersJson = JsonConvert.SerializeObject(users, Formatting.Indented);
                await File.WriteAllTextAsync(usersPath, usersJson);

                if (configuration.Settings != null)
                {
                    foreach (var kvp in configuration.Settings)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key))
                            continue;

                        var settingsPath = GetUserSettingsFilePath(kvp.Key);
                        await File.WriteAllTextAsync(settingsPath, kvp.Value ?? string.Empty);
                    }
                }

                await RefreshLocalUserListAsync();
                await ShowInfoDialogAsync("Import terminé", $"Configuration utilisateurs importée ({users.Count} utilisateur(s)).", xamlRoot);
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Erreur d'import", $"Impossible de charger la configuration : {ex.Message}", xamlRoot);
            }
        }

        private static string GetLocalDataFolderPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");

        private static string GetUserSettingsFilePath(string username)
        {
            var safeName = AppSettings.SanitizeUserNameForFile(username ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName))
                throw new ArgumentException("Le nom d'utilisateur est invalide.", nameof(username));

            return Path.Combine(GetLocalDataFolderPath(), $"{safeName}_settings.json");
        }

        private static void BackupFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", true);
            }
            catch
            {
            }
        }
    }

    internal class UserExportConfiguration
    {
        public List<UserInfo>? Users { get; set; }
        public Dictionary<string, string>? Settings { get; set; }
    }
}
