using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Client.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Client.Models;
using Client.Helpers;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System.IO;
using Client.Pages;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;

namespace Client
{
    public partial class App : Application
    {
        public static SignalRService ChatService { get; } = new SignalRService();
        public static Window? MainWindow { get; private set; }
        public static string UserName { get; set; } = string.Empty;
        public static UserInfo? LastUserChanged { get; set; }
        public static HotKeyService HotKeys { get; } = new HotKeyService();
        private DispatcherQueueTimer? _agendaTimer;
        private bool _agendaSwitchInProgress;
        public App()
        {
            this.InitializeComponent();

            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AppSettings.SettingsChanged += async () =>
            {
                try
                {
                    if (ChatService.Connection != null && ChatService.Connection.State == HubConnectionState.Connected)
                    {
                        await ChatService.SaveUserSettingsAsync(UserName, AppSettings.Export());
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException("[App] SettingsChanged handler failed", ex, "CLI12");
                }
            };
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            MainWindow = m_window;
            HotKeys.Start();

            var theme = AppSettings.Get("AppTheme", "Dark");
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                if (m_window.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme =
                        appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
                }
            }
            m_window.Closed += (_, __) => HotKeys.Dispose();
            ChatService.Dispatcher = m_window.DispatcherQueue;
            ChatService.OnMessageReceived += ChatService_OnMessageReceived;
            // Register handler once the window root has loaded so XamlRoot is valid
            if (m_window.Content is FrameworkElement windowRoot)
            {
                windowRoot.Loaded += MainWindow_Loaded;
            }
            // Show the window immediately
            m_window.Activate();
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement root)
                return;

            root.Loaded -= MainWindow_Loaded;
            RegisterActivityHandlers(root);

            var machine = MachineConfig.Load();

            if (string.IsNullOrWhiteSpace(machine.RoomName))
            {
                var dialog = new ContentDialog
                {
                    Title = "Nom de la salle",
                    PrimaryButtonText = "Valider",
                    XamlRoot = root.XamlRoot
                };

                var box = new TextBox();
                dialog.Content = box;
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    machine.RoomName = box.Text.Trim();
                    MachineConfig.Save(machine);
                }
            }

            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            Directory.CreateDirectory(appFolder);
            var settingsFiles = Directory.GetFiles(appFolder, "*_settings.json");
            var validSettingsFiles = new List<string>();

            foreach (var file in settingsFiles)
            {
                var rawName = Path.GetFileNameWithoutExtension(file)?.Replace("_settings", string.Empty) ?? string.Empty;
                var sanitized = AppSettings.SanitizeUserNameForFile(rawName);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException($"[App] Impossible de supprimer le fichier de paramètres invalide '{file}'", ex, "CLI24");
                    }

                    continue;
                }

                validSettingsFiles.Add(file);
            }

            settingsFiles = validSettingsFiles.ToArray();

            string? username = null;
            var scheduledUser = machine.GetAgendaUser(DateTime.Now);
            if (!string.IsNullOrWhiteSpace(scheduledUser))
            {
                username = scheduledUser;
            }
            if (settingsFiles.Length == 0 && string.IsNullOrWhiteSpace(username))
            {
                var requested = await PromptForUsernameAsync(root.XamlRoot);
                if (string.IsNullOrWhiteSpace(requested))
                    return;

                username = requested;

                if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                    machine.DefaultUser = username;
                machine.LastUser = username;
                machine.ConnectLastUser = false;
                MachineConfig.Save(machine);
            }
            else if (string.IsNullOrWhiteSpace(username))
            {
                var candidate = machine.ConnectLastUser
                    ? machine.LastUser
                    : machine.DefaultUser;

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    var fromFile = Path.GetFileNameWithoutExtension(settingsFiles[0])?
                        .Replace("_settings", string.Empty) ?? string.Empty;
                    candidate = AppSettings.SanitizeUserNameForFile(fromFile);

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                            machine.DefaultUser = candidate;
                        if (string.IsNullOrWhiteSpace(machine.LastUser))
                            machine.LastUser = candidate;
                        MachineConfig.Save(machine);
                    }
                }

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = await PromptForUsernameAsync(root.XamlRoot);
                    if (string.IsNullOrWhiteSpace(candidate))
                        return;

                    if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                        machine.DefaultUser = candidate;
                    machine.LastUser = candidate;
                    machine.ConnectLastUser = false;
                    MachineConfig.Save(machine);
                }

                username = candidate;
            }

            if (string.IsNullOrWhiteSpace(username))
                return;

            App.UserName = username;
            AppSettings.Reload();
            ApplySavedAppearance(root);
            AppSettings.CurrentSelectedUser = new UserInfo
            {
                Username = username,
                Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
            };

            if (!string.IsNullOrWhiteSpace(machine.RoomName) )
            {
                ChatService.RoomName = machine.RoomName;
                await ChatService.InitializeAsync();
                await SyncUserSettingsAsync(root);
                await DownloadMissingUserSettingsAsync();
            }

            RefreshAgendaTimer();
        }

        private void RegisterActivityHandlers(FrameworkElement root)
        {
            root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnUserActivity), true);
        }

        private void OnUserActivity(object sender, RoutedEventArgs e)
        {
            ChatService.ReportUserActivity();
        }

        public void RefreshAgendaTimer()
        {
            var machine = MachineConfig.Load();
            var dispatcher = MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (dispatcher == null)
            {
                return;
            }

            if (!machine.AgendaModeEnabled || !machine.AutoSwitchEnabled)
            {
                void StopTimer() => _agendaTimer?.Stop();
                if (dispatcher.HasThreadAccess)
                {
                    StopTimer();
                }
                else
                {
                    dispatcher.TryEnqueue(StopTimer);
                }

                return;
            }

            void StartTimer()
            {
                if (_agendaTimer == null)
                {
                    _agendaTimer = dispatcher.CreateTimer();
                    _agendaTimer.Interval = TimeSpan.FromMinutes(1);
                    _agendaTimer.Tick += AgendaTimer_Tick;
                }

                _agendaTimer.Start();
            }

            if (dispatcher.HasThreadAccess)
            {
                StartTimer();
            }
            else
            {
                dispatcher.TryEnqueue(StartTimer);
            }
        }

        private async void AgendaTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            await ApplyAgendaSwitchAsync();
        }

        private async Task ApplyAgendaSwitchAsync()
        {
            if (_agendaSwitchInProgress)
            {
                return;
            }

            var machine = MachineConfig.Load();
            if (!machine.AgendaModeEnabled || !machine.AutoSwitchEnabled)
            {
                _agendaTimer?.Stop();
                return;
            }

            var scheduledUser = machine.GetAgendaUser(DateTime.Now);
            if (string.IsNullOrWhiteSpace(scheduledUser))
            {
                return;
            }

            if (string.Equals(scheduledUser, UserName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _agendaSwitchInProgress = true;
            try
            {
                await ChangeUserAsync(scheduledUser);
            }
            finally
            {
                _agendaSwitchInProgress = false;
            }
        }

        private static async Task<string?> PromptForUsernameAsync(XamlRoot xamlRoot)
        {
            while (true)
            {
                var userDialog = new ContentDialog
                {
                    Title = "Nom d'utilisateur",
                    PrimaryButtonText = "Valider",
                    XamlRoot = xamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var userBox = new TextBox();
                userDialog.Content = userBox;
                userDialog.IsPrimaryButtonEnabled = false;

                userBox.TextChanged += (_, __) =>
                {
                    userDialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(userBox.Text);
                };

                var userResult = await userDialog.ShowAsync();
                if (userResult != ContentDialogResult.Primary)
                    return null;

                var username = userBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(username))
                    return username;
            }
        }

        public async Task<string?> PromptForAccountSelectionAsync()
        {
            if (MainWindow?.Content is not FrameworkElement root)
                return null;

            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            Directory.CreateDirectory(appFolder);
            var settingsFiles = Directory.GetFiles(appFolder, "*_settings.json");
            var users = settingsFiles
                .Select(f => Path.GetFileNameWithoutExtension(f)?.Replace("_settings", string.Empty) ?? string.Empty)
                .Select(AppSettings.SanitizeUserNameForFile)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dialog = new ContentDialog
            {
                Title = "Choisir l'utilisateur",
                PrimaryButtonText = "OK",
                CloseButtonText = "Annuler",
                XamlRoot = root.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            var combo = new ComboBox { ItemsSource = users, PlaceholderText = "Utilisateur" };
            var newBox = new TextBox { PlaceholderText = "Nouvel utilisateur" };
            stack.Children.Add(combo);
            stack.Children.Add(newBox);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return null;

            var name = !string.IsNullOrWhiteSpace(newBox.Text)
                ? newBox.Text.Trim()
                : combo.SelectedItem as string;

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        public static void ApplySavedAppearance(FrameworkElement root)
        {
            var theme = AppSettings.Get("AppTheme", "Dark");
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                root.RequestedTheme =
                    appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            }

            var colors = AppSettings.GetObject<AppColorSettings>("Colors");

            var titleBar = root.FindName("AppTitleBar") as Grid;
            var nav = root.FindName("nvSample") as NavigationView;
            var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
            if (root.FindName("PersonPic") is PersonPicture pic)
            {
                var initials = AppSettings.Get("Initials", string.Empty);
                if (string.IsNullOrWhiteSpace(initials))
                {
                    initials = string.Concat(App.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => char.ToUpperInvariant(s[0])));
                }
                pic.Initials = initials;

            }

            AppearanceSettingsPage.ApplyColors(colors, titleBar, nav, titleText);
        }

        private void ChatService_OnMessageReceived(ChatMessageModel chat)
        {
            if (MainWindow is not MainWindow mw)
                return;

            bool isForeground = WindowHelper.IsForeground(mw);
            bool isChat = mw.IsChatPageActive;

            if (!isForeground)
            {
                mw.DispatcherQueue.TryEnqueue(() =>
                {
                    mw.ShowChatPage();
                    mw.BringToForeground();
                    mw.ScrollMessagesToEnd();
                });
            }
            else if (mw.IsTopMost)
            {
                if (isChat)
                {
                    mw.DispatcherQueue.TryEnqueue(mw.ScrollMessagesToEnd);
                }
                else
                {
                    mw.DispatcherQueue.TryEnqueue(() => ShowNotification(chat));
                }
            }
            else
            {
                if (isChat)
                {
                    mw.DispatcherQueue.TryEnqueue(mw.ScrollMessagesToEnd);
                }
                else
                {
                    mw.DispatcherQueue.TryEnqueue(() => ShowNotification(chat));
                }
            }
        }

        private static void ShowNotification(ChatMessageModel chat)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText("EyeChat")
                    .AddText($"{chat.Sender}: {chat.Content}")
                    .BuildNotification();

                AppNotificationManager.Default.Register();
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] ShowNotification failed", ex, "CLI13");
            }
        }

        private async Task SyncUserSettingsAsync(FrameworkElement root)
        {
            try
            {
                var json = await ChatService.GetUserSettingsAsync(App.UserName);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    AppSettings.Import(json);
                    ApplySavedAppearance(root);
                }
                else
                {
                    var local = AppSettings.Export();
                    if (!string.IsNullOrWhiteSpace(local))
                        await ChatService.SaveUserSettingsAsync(App.UserName, local);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] SyncUserSettingsAsync failed", ex, "CLI14");
            }
        }

        private async Task DownloadMissingUserSettingsAsync()
        {
            try
            {
                var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                Directory.CreateDirectory(appFolder);
                var localUsers = Directory.GetFiles(appFolder, "*_settings.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f)?.Replace("_settings", string.Empty) ?? string.Empty)
                    .Select(AppSettings.SanitizeUserNameForFile)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
                var missing = await ChatService.GetMissingUserSettingsAsync(localUsers);
                foreach (var kvp in missing)
                {
                    var sanitized = AppSettings.SanitizeUserNameForFile(kvp.Key ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(sanitized))
                    {
                        continue;
                    }

                    var path = Path.Combine(appFolder, $"{sanitized}_settings.json");
                    File.WriteAllText(path, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] DownloadMissingUserSettingsAsync failed", ex, "CLI15");
            }
        }

        public async Task ChangeUserAsync(string username)
        {
            if (MainWindow?.Content is not FrameworkElement root)
                return;

            try
            {
                await ChatService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] ChangeUserAsync.DisconnectAsync failed", ex, "CLI16");
            }

            ChatService.ClearLocalData();

            UserName = username;
            AppSettings.Reload();
            ApplySavedAppearance(root);
            AppSettings.CurrentSelectedUser = new UserInfo
            {
                Username = username,
                Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
            };

            var machine = MachineConfig.Load();
            machine.LastUser = username;
            MachineConfig.Save(machine);

            await ChatService.InitializeAsync();
            await SyncUserSettingsAsync(root);
            await DownloadMissingUserSettingsAsync();

            if (MainWindow is MainWindow mw)
            {
                var chat = mw.ShowChatPage();
                chat?.RefreshUsername();
                mw.SetAccountState(true);
                mw.RefreshAgendaSwitchState();
            }

            RefreshAgendaTimer();
        }

        public async Task LogoutAsync()
        {
            try
            {
                await ChatService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogException("LogoutAsync.DisconnectAsync failed", ex, "CLI09");
            }

            ChatService.ClearLocalData();

            if (MainWindow is MainWindow mw)
            {
                mw.SetAccountState(false);
            }
        }

        private Window? m_window;

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            if (e.Exception is Exception ex)
            {
                Logger.LogException("Unhandled UI exception", ex, "CLI01");
            }
            else
            {
                Logger.Log("Unhandled UI exception without an Exception instance.");
            }

            e.Handled = true;

            if (MainWindow is MainWindow window && window.Content is FrameworkElement root)
            {
                window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Erreur inattendue",
                            Content = "Une erreur est survenue. Un journal a été enregistré dans les données locales.",
                            CloseButtonText = "Fermer",
                            XamlRoot = root.XamlRoot
                        };

                        await dialog.ShowAsync();
                    }
                    catch
                    {
                        // Ignore dialog errors – the dispatcher may not be available during shutdown.
                    }
                });
            }
        }

        private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.LogException("AppDomain unhandled exception", ex, "CLI02");
            }
            else
            {
                Logger.Log($"AppDomain unhandled exception object: {e.ExceptionObject}");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogException("Unobserved task exception", e.Exception, "CLI03");
            e.SetObserved();
        }
    }
}
