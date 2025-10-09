using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Microsoft.UI.Dispatching;
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
                var dispatcher = DispatcherQueue
                    ?? (XamlRoot?.Content as FrameworkElement)?.DispatcherQueue
                    ?? App.MainWindow?.DispatcherQueue;

                if (dispatcher is not null && !dispatcher.HasThreadAccess)
                {
                    if (!dispatcher.TryEnqueue(() => IsBusy = value))
                    {
                        Debug.WriteLine("[UserManagerDialog] Unable to marshal IsBusy change to dispatcher.");
                    }
                    return;
                }

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
            Debug.WriteLine("[UserManagerDialog] Constructor start");
            InitializeComponent();
            _service = App.ChatService;
            DataContext = this;
            Opened += UserManagerDialog_Opened;
            Debug.WriteLine("[UserManagerDialog] Constructor end");
        }

        private async void UserManagerDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            Debug.WriteLine("[UserManagerDialog] Dialog opened");
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            Debug.WriteLine($"[UserManagerDialog] LoadAsync invoked. IsBusy={IsBusy}");
            if (IsBusy)
            {
                Debug.WriteLine("[UserManagerDialog] LoadAsync aborted because IsBusy");
                return;
            }

            IsBusy = true;
            try
            {
                Debug.WriteLine("[UserManagerDialog] Refreshing local users");
                await RefreshLocalAsync();
                Debug.WriteLine("[UserManagerDialog] Refreshing server users");
                await RefreshServerAsync();
            }
            finally
            {
                Debug.WriteLine("[UserManagerDialog] LoadAsync completed");
                IsBusy = false;
            }
        }

        private async Task RefreshLocalAsync()
        {
            Debug.WriteLine("[UserManagerDialog] RefreshLocalAsync start");
            try
            {
                var users = await _service.LoadUsersFromDiskAsync();
                Debug.WriteLine($"[UserManagerDialog] Local users loaded: {users.Count()}");
                LocalUsers.Clear();
                foreach (var user in users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var clone = CloneUser(user);
                    clone.CanRenameLocalUser = CanManageUser(clone.Username);
                    LocalUsers.Add(clone);
                }

                LocalStatus = LocalUsers.Count == 0
                    ? "Aucun utilisateur local enregistré."
                    : $"{LocalUsers.Count} utilisateur(s) local(aux).";
                Debug.WriteLine($"[UserManagerDialog] Local status: {LocalStatus}");
            }
            catch (Exception ex)
            {
                LocalStatus = $"Erreur de chargement local : {ex.Message}";
                Debug.WriteLine($"[UserManagerDialog] Error loading local users: {ex}");
            }
            Debug.WriteLine("[UserManagerDialog] RefreshLocalAsync end");
        }

        private async Task RefreshServerAsync()
        {
            Debug.WriteLine("[UserManagerDialog] RefreshServerAsync start");
            try
            {
                var users = await _service.GetAllUsersAsync();
                Debug.WriteLine($"[UserManagerDialog] Server users loaded: {users.Count()}");
                ServerUsers.Clear();
                foreach (var user in users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var clone = CloneUser(user);
                    clone.CanRenameLocalUser = CanManageUser(clone.Username);
                    ServerUsers.Add(clone);
                }

                ServerStatus = ServerUsers.Count == 0
                    ? "Aucun utilisateur connu sur le serveur."
                    : $"{ServerUsers.Count} utilisateur(s) serveur.";
                Debug.WriteLine($"[UserManagerDialog] Server status: {ServerStatus}");
            }
            catch (Exception ex)
            {
                ServerStatus = $"Erreur de chargement serveur : {ex.Message}";
                Debug.WriteLine($"[UserManagerDialog] Error loading server users: {ex}");
            }
            Debug.WriteLine("[UserManagerDialog] RefreshServerAsync end");
        }

        private static UserInfo CloneUser(UserInfo user) => user.Clone();

        private async void RenameLocal_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[UserManagerDialog] RenameLocal_Click invoked");
            if (sender is not Button button || button.Tag is not string username)
                return;

            Debug.WriteLine($"[UserManagerDialog] RenameLocal_Click for {username}");
            await RenameLocalAsync(username);
        }

        private async Task RenameLocalAsync(string username)
        {
            Debug.WriteLine($"[UserManagerDialog] RenameLocalAsync start for {username}. IsBusy={IsBusy}");
            if (IsProtectedUser(username) || IsBusy)
            {
                Debug.WriteLine($"[UserManagerDialog] RenameLocalAsync aborted for {username}. Protected={IsProtectedUser(username)} IsBusy={IsBusy}");
                return;
            }

            var newName = await PromptForNameAsync(username, "Renommer l'utilisateur local");
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, username, StringComparison.OrdinalIgnoreCase))
            {
                LocalStatus = "Renommage annulé.";
                Debug.WriteLine($"[UserManagerDialog] RenameLocalAsync cancelled for {username}");
                return;
            }

            IsBusy = true;
            try
            {
                Debug.WriteLine($"[UserManagerDialog] Attempting to rename local user {username} to {newName}");
                var success = await _service.RenameLocalUserAsync(username, newName);
                if (success)
                {
                    LocalStatus = $"Utilisateur local « {username} » renommé en « {newName} ».";
                    Debug.WriteLine($"[UserManagerDialog] RenameLocalAsync success for {username} -> {newName}");
                    await RefreshLocalAsync();
                }
                else
                {
                    LocalStatus = $"Impossible de renommer « {username} ». Vérifiez que le nom est disponible.";
                    Debug.WriteLine($"[UserManagerDialog] RenameLocalAsync failed for {username}");
                }
            }
            finally
            {
                Debug.WriteLine("[UserManagerDialog] RenameLocalAsync end");
                IsBusy = false;
            }
        }

        private async void DeleteLocal_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[UserManagerDialog] DeleteLocal_Click invoked");
            if (sender is not Button button || button.Tag is not string username)
                return;

            Debug.WriteLine($"[UserManagerDialog] DeleteLocal_Click for {username}");
            await DeleteLocalAsync(username);
        }

        private async Task DeleteLocalAsync(string username)
        {
            Debug.WriteLine($"[UserManagerDialog] DeleteLocalAsync start for {username}. IsBusy={IsBusy}");
            if (IsProtectedUser(username) || IsBusy)
            {
                Debug.WriteLine($"[UserManagerDialog] DeleteLocalAsync aborted for {username}. Protected={IsProtectedUser(username)} IsBusy={IsBusy}");
                return;
            }

            if (!await ConfirmDeleteAsync(username, false))
            {
                Debug.WriteLine($"[UserManagerDialog] DeleteLocalAsync cancelled for {username}");
                return;
            }

            IsBusy = true;
            try
            {
                Debug.WriteLine($"[UserManagerDialog] Attempting to delete local user {username}");
                var success = await _service.DeleteLocalUserAsync(username);
                if (success)
                {
                    LocalStatus = $"Utilisateur local « {username} » supprimé.";
                    Debug.WriteLine($"[UserManagerDialog] DeleteLocalAsync success for {username}");
                    await RefreshLocalAsync();
                }
                else
                {
                    LocalStatus = $"Impossible de supprimer « {username} ».";
                    Debug.WriteLine($"[UserManagerDialog] DeleteLocalAsync failed for {username}");
                }
            }
            finally
            {
                Debug.WriteLine("[UserManagerDialog] DeleteLocalAsync end");
                IsBusy = false;
            }
        }

        private async void RenameServer_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[UserManagerDialog] RenameServer_Click invoked");
            if (sender is not Button button || button.Tag is not string username)
                return;

            Debug.WriteLine($"[UserManagerDialog] RenameServer_Click for {username}");
            await RenameServerAsync(username);
        }

        private async Task RenameServerAsync(string username)
        {
            Debug.WriteLine($"[UserManagerDialog] RenameServerAsync start for {username}. IsBusy={IsBusy}");
            if (IsProtectedUser(username) || IsBusy)
            {
                Debug.WriteLine($"[UserManagerDialog] RenameServerAsync aborted for {username}. Protected={IsProtectedUser(username)} IsBusy={IsBusy}");
                return;
            }

            var newName = await PromptForNameAsync(username, "Renommer l'utilisateur serveur");
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, username, StringComparison.OrdinalIgnoreCase))
            {
                ServerStatus = "Renommage annulé.";
                Debug.WriteLine($"[UserManagerDialog] RenameServerAsync cancelled for {username}");
                return;
            }

            IsBusy = true;
            try
            {
                Debug.WriteLine($"[UserManagerDialog] Attempting to rename server user {username} to {newName}");
                var success = await _service.RenameServerUserAsync(username, newName);
                if (success)
                {
                    ServerStatus = $"Utilisateur serveur « {username} » renommé en « {newName} ».";
                    Debug.WriteLine($"[UserManagerDialog] RenameServerAsync success for {username} -> {newName}");
                    await _service.RenameLocalUserAsync(username, newName);
                    await RefreshServerAsync();
                    await RefreshLocalAsync();
                }
                else
                {
                    ServerStatus = $"Impossible de renommer « {username} » sur le serveur.";
                    Debug.WriteLine($"[UserManagerDialog] RenameServerAsync failed for {username}");
                }
            }
            finally
            {
                Debug.WriteLine("[UserManagerDialog] RenameServerAsync end");
                IsBusy = false;
            }
        }

        private async void DeleteServer_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[UserManagerDialog] DeleteServer_Click invoked");
            if (sender is not Button button || button.Tag is not string username)
                return;

            Debug.WriteLine($"[UserManagerDialog] DeleteServer_Click for {username}");
            await DeleteServerAsync(username);
        }

        private async Task DeleteServerAsync(string username)
        {
            Debug.WriteLine($"[UserManagerDialog] DeleteServerAsync start for {username}. IsBusy={IsBusy}");
            if (IsProtectedUser(username) || IsBusy)
            {
                Debug.WriteLine($"[UserManagerDialog] DeleteServerAsync aborted for {username}. Protected={IsProtectedUser(username)} IsBusy={IsBusy}");
                return;
            }

            if (!await ConfirmDeleteAsync(username, true))
            {
                Debug.WriteLine($"[UserManagerDialog] DeleteServerAsync cancelled for {username}");
                return;
            }

            IsBusy = true;
            try
            {
                Debug.WriteLine($"[UserManagerDialog] Attempting to delete server user {username}");
                var success = await _service.DeleteServerUserAsync(username);
                if (success)
                {
                    ServerStatus = $"Utilisateur serveur « {username} » supprimé.";
                    Debug.WriteLine($"[UserManagerDialog] DeleteServerAsync success for {username}");
                    await _service.DeleteLocalUserAsync(username);
                    await RefreshServerAsync();
                    await RefreshLocalAsync();
                }
                else
                {
                    ServerStatus = $"Impossible de supprimer « {username} » sur le serveur.";
                    Debug.WriteLine($"[UserManagerDialog] DeleteServerAsync failed for {username}");
                }
            }
            finally
            {
                Debug.WriteLine("[UserManagerDialog] DeleteServerAsync end");
                IsBusy = false;
            }
        }

        private async Task<string?> PromptForNameAsync(string currentName, string title)
        {
            Debug.WriteLine($"[UserManagerDialog] PromptForNameAsync for {currentName} with title '{title}'");
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
            Debug.WriteLine($"[UserManagerDialog] PromptForNameAsync result: {result}");
            if (result == ContentDialogResult.Primary)
            {
                var text = box.Text?.Trim();
                Debug.WriteLine($"[UserManagerDialog] PromptForNameAsync text: '{text}'");
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            return null;
        }

        private async Task<bool> ConfirmDeleteAsync(string username, bool server)
        {
            Debug.WriteLine($"[UserManagerDialog] ConfirmDeleteAsync for {username}. Server={server}");
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
            Debug.WriteLine($"[UserManagerDialog] ConfirmDeleteAsync result: {result}");
            return result == ContentDialogResult.Primary;
        }

        public bool CanManageUser(string username) => !IsProtectedUser(username);

        private static bool IsProtectedUser(string username)
            => string.Equals(username, "A Tous", StringComparison.OrdinalIgnoreCase)
               || string.Equals(username, "Secrétariat", StringComparison.OrdinalIgnoreCase);

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            Debug.WriteLine($"[UserManagerDialog] SetProperty called for {propertyName}.");
            if (Equals(storage, value))
            {
                Debug.WriteLine($"[UserManagerDialog] SetProperty no change for {propertyName}.");
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            Debug.WriteLine($"[UserManagerDialog] SetProperty updated {propertyName} to {value}.");
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            Debug.WriteLine($"[UserManagerDialog] PropertyChanged invoked for {propertyName}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
