using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.Helpers;

namespace Client.Pages
{
    public sealed partial class SystemPage : Page
    {
        private const string SettingsPassword = "901027";
        private readonly MachineConfig _config;
        private bool _isLoaded;
        private bool _suppressTimeToggle;
        private bool _suppressReminderToggle;

        public string RoomName { get; set; } = string.Empty;
        public bool ShowTimeModification { get; set; }
        public bool ShowReminderPage { get; set; }
        public List<string> Users { get; set; } = new();
        public string DefaultUser { get; set; } = string.Empty;
        public bool ConnectLastUser { get; set; }

        public SystemPage()
        {

            this.InitializeComponent();
            _config = MachineConfig.Load();
            ShowTimeModification = _config.ShowTimeModification;
            ShowReminderPage = _config.ShowReminderPage;
            RoomName = _config.RoomName;
            DefaultUser = _config.DefaultUser;
            ConnectLastUser = _config.ConnectLastUser;

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*_settings.json");
                Users = files.Select(f => Path.GetFileName(f).Replace("_settings.json", "")).ToList();
            }
            if (!Users.Contains(DefaultUser) && !string.IsNullOrWhiteSpace(DefaultUser))
                Users.Add(DefaultUser);

            DataContext = this;
            Loaded += SystemPage_Loaded;
        }

        private void SystemPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
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
            _config.DefaultUser = DefaultUser;
            _config.ConnectLastUser = ConnectLastUser;
            MachineConfig.Save(_config);
            App.ChatService.RoomName = RoomName;
            await App.ChatService.UpdateRoomNameAsync(RoomName);
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
    }
}