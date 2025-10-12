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

namespace Client
{
    public partial class App : Application
    {
        public static SignalRService ChatService { get; } = new SignalRService();
        public static Window? MainWindow { get; private set; }
        public static string UserName { get; set; } = string.Empty;
        public static UserInfo? LastUserChanged { get; set; }
        public static HotKeyService HotKeys { get; } = new HotKeyService();
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
                    rootElement.RequestedTheme = appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
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

            var machine = MachineConfig.Load();

            if (string.IsNullOrWhiteSpace(machine.RoomName))
            {
                var dialog = new ContentDialog
                {
                    Title = "Nom de la salle",
                    PrimaryButtonText = "Valider",
                    XamlRoot = root.XamlRoot
                };

                ThemeHelper.ApplyDialogTheme(dialog);

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

            if (settingsFiles.Length == 0)
            {
                var userDialog = new ContentDialog
                {
                    Title = "Nom d'utilisateur",
                    PrimaryButtonText = "Valider",
                    XamlRoot = root.XamlRoot
                };

                ThemeHelper.ApplyDialogTheme(userDialog);

                var userBox = new TextBox();
                userDialog.Content = userBox;
                var userResult = await userDialog.ShowAsync();
                if (userResult == ContentDialogResult.Primary)
                {
                    var username = userBox.Text.Trim();
                    App.UserName = username;
                    AppSettings.Reload();
                    ApplySavedAppearance(root);
                    AppSettings.CurrentSelectedUser = new UserInfo
                    {
                        Username = username,
                        Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
                    };

                    if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                        machine.DefaultUser = username;
                    machine.LastUser = username;
                    machine.ConnectLastUser = false; 
                    MachineConfig.Save(machine);
                }
            }
            else
            {
                var username = machine.ConnectLastUser
                   ? machine.LastUser
                   : machine.DefaultUser;
                if (string.IsNullOrWhiteSpace(username))
                {
                    var fromFile = Path.GetFileNameWithoutExtension(settingsFiles[0])?
                        .Replace("_settings", string.Empty) ?? string.Empty;
                    username = AppSettings.SanitizeUserNameForFile(fromFile);
                }

                App.UserName = username;
                AppSettings.Reload();
                ApplySavedAppearance(root);
                AppSettings.CurrentSelectedUser = new UserInfo
                {
                    Username = username,
                    Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
                };
            }

            if (!string.IsNullOrWhiteSpace(machine.RoomName) )
            {
                ChatService.RoomName = machine.RoomName;
                await ChatService.InitializeAsync();
                await SyncUserSettingsAsync(root);
                await DownloadMissingUserSettingsAsync();
            }
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
                if (ChatService.Connection != null)
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
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                if (ChatService.Connection != null)
                    await ChatService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogException("LogoutAsync.DisconnectAsync failed", ex, "CLI09");
            }

            ChatService.ClearLocalData();
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

                        ThemeHelper.ApplyDialogTheme(dialog);

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
