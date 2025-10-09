using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Dialogs
{
    public sealed partial class UserManagerDialog : ContentDialog, INotifyPropertyChanged
    {
        private readonly SignalRService _service;

        public ObservableCollection<UserInfo> LocalUsers { get; } = new();
        public ObservableCollection<UserInfo> ServerUsers { get; } = new();

        private string _localStatus = string.Empty;
        public string LocalStatus
        {
            get => _localStatus;
            set => SetProperty(ref _localStatus, value);
        }

        private string _serverStatus = string.Empty;
        public string ServerStatus
        {
            get => _serverStatus;
            set => SetProperty(ref _serverStatus, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsIdle));
                    IsPrimaryButtonEnabled = !value;
                }
            }
        }

        public bool IsIdle => !IsBusy;

        public event PropertyChangedEventHandler? PropertyChanged;

        public UserManagerDialog()
        {
            InitializeComponent();
            _service = App.ChatService;
            DataContext = this;
            Opened += UserManagerDialog_Opened;
        }

        private async void UserManagerDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                await RefreshLocalAsync();
                await RefreshServerAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshLocalAsync()
        {
            try
            {
                var users = await _service.LoadUsersFromDiskAsync();
                LocalUsers.Clear();
                foreach (var user in users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                    LocalUsers.Add(CloneUser(user));

                LocalStatus = LocalUsers.Count == 0
                    ? "Aucun utilisateur local enregistré."
                    : $"{LocalUsers.Count} utilisateur(s) local(aux).";
            }
            catch (Exception ex)
            {
                LocalStatus = $"Erreur de chargement local : {ex.Message}";
            }
        }

        private async Task RefreshServerAsync()
        {
            try
            {
                var users = await _service.GetAllUsersAsync();
                ServerUsers.Clear();
                foreach (var user in users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                    ServerUsers.Add(CloneUser(user));

                ServerStatus = ServerUsers.Count == 0
                    ? "Aucun utilisateur connu sur le serveur."
                    : $"{ServerUsers.Count} utilisateur(s) serveur.";
            }
            catch (Exception ex)
            {
                ServerStatus = $"Erreur de chargement serveur : {ex.Message}";
            }
        }

        private static UserInfo CloneUser(UserInfo user) => user.Clone();

        private async void RenameLocal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string username)
                return;

            await RenameLocalAsync(username);
        }

        private async Task RenameLocalAsync(string username)
        {
            if (IsProtectedUser(username) || IsBusy)
                return;

            var newName = await PromptForNameAsync(username, "Renommer l'utilisateur local");
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, username, StringComparison.OrdinalIgnoreCase))
            {
                LocalStatus = "Renommage annulé.";
                return;
            }

            IsBusy = true;
            try
            {
                var success = await _service.RenameLocalUserAsync(username, newName);
                if (success)
                {
                    LocalStatus = $"Utilisateur local « {username} » renommé en « {newName} ».";
                    await RefreshLocalAsync();
                }
                else
                {
                    LocalStatus = $"Impossible de renommer « {username} ». Vérifiez que le nom est disponible.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void DeleteLocal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string username)
                return;

            await DeleteLocalAsync(username);
        }

        private async Task DeleteLocalAsync(string username)
        {
            if (IsProtectedUser(username) || IsBusy)
                return;

            if (!await ConfirmDeleteAsync(username, false))
                return;

            IsBusy = true;
            try
            {
                var success = await _service.DeleteLocalUserAsync(username);
                if (success)
                {
                    LocalStatus = $"Utilisateur local « {username} » supprimé.";
                    await RefreshLocalAsync();
                }
                else
                {
                    LocalStatus = $"Impossible de supprimer « {username} ».";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void RenameServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string username)
                return;

            await RenameServerAsync(username);
        }

        private async Task RenameServerAsync(string username)
        {
            if (IsProtectedUser(username) || IsBusy)
                return;

            var newName = await PromptForNameAsync(username, "Renommer l'utilisateur serveur");
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, username, StringComparison.OrdinalIgnoreCase))
            {
                ServerStatus = "Renommage annulé.";
                return;
            }

            IsBusy = true;
            try
            {
                var success = await _service.RenameServerUserAsync(username, newName);
                if (success)
                {
                    ServerStatus = $"Utilisateur serveur « {username} » renommé en « {newName} ».";
                    await _service.RenameLocalUserAsync(username, newName);
                    await RefreshServerAsync();
                    await RefreshLocalAsync();
                }
                else
                {
                    ServerStatus = $"Impossible de renommer « {username} » sur le serveur.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void DeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string username)
                return;

            await DeleteServerAsync(username);
        }

        private async Task DeleteServerAsync(string username)
        {
            if (IsProtectedUser(username) || IsBusy)
                return;

            if (!await ConfirmDeleteAsync(username, true))
                return;

            IsBusy = true;
            try
            {
                var success = await _service.DeleteServerUserAsync(username);
                if (success)
                {
                    ServerStatus = $"Utilisateur serveur « {username} » supprimé.";
                    await _service.DeleteLocalUserAsync(username);
                    await RefreshServerAsync();
                    await RefreshLocalAsync();
                }
                else
                {
                    ServerStatus = $"Impossible de supprimer « {username} » sur le serveur.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<string?> PromptForNameAsync(string currentName, string title)
        {
            var box = new TextBox
            {
                Text = currentName,
                PlaceholderText = "Nouveau nom"
            };

            var dialog = new ContentDialog
            {
                Title = title,
                PrimaryButtonText = "Valider",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Primary,
                Content = box,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var text = box.Text?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            return null;
        }

        private async Task<bool> ConfirmDeleteAsync(string username, bool server)
        {
            var text = server
                ? $"Supprimer « {username} » des utilisateurs serveur ?"
                : $"Supprimer « {username} » du cache local ?";

            var dialog = new ContentDialog
            {
                Title = "Confirmation",
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.WrapWholeWords },
                PrimaryButtonText = "Supprimer",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public bool CanManageUser(string username) => !IsProtectedUser(username);

        private static bool IsProtectedUser(string username)
            => string.Equals(username, "A Tous", StringComparison.OrdinalIgnoreCase)
               || string.Equals(username, "Secrétariat", StringComparison.OrdinalIgnoreCase);

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
