using Microsoft.UI.Xaml;
using System;
using Client.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Client.Models;
using Client.Helpers;
using Microsoft.UI.Xaml.Controls;
using System.IO;

namespace Client
{
    public partial class App : Application
    {
        public static SignalRService ChatService { get; } = new SignalRService();
        public static Window? MainWindow { get; private set; }
        public static string UserName { get; set; } = string.Empty;
        public App()
        {
            this.InitializeComponent();
        }

        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            MainWindow = m_window;

            var theme = AppSettings.Get("AppTheme", "Dark");
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                if (m_window.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
                }
            }

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

            if (settingsFiles.Length == 0)
            {
                var userDialog = new ContentDialog
                {
                    Title = "Nom d'utilisateur",
                    PrimaryButtonText = "Valider",
                    XamlRoot = root.XamlRoot
                };

                var userBox = new TextBox();
                userDialog.Content = userBox;
                var userResult = await userDialog.ShowAsync();
                if (userResult == ContentDialogResult.Primary)
                {
                    var username = userBox.Text.Trim();
                    App.UserName = username;
                    AppSettings.CurrentSelectedUser = new UserInfo { Username = username };

                    if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                        machine.DefaultUser = username;
                    machine.LastUser = username;
                    MachineConfig.Save(machine);
                }
            }
            else
            {
                var username = !string.IsNullOrWhiteSpace(machine.LastUser)
                    ? machine.LastUser
                    : Path.GetFileNameWithoutExtension(settingsFiles[0]).Replace("_settings", "");
                App.UserName = username;
                AppSettings.CurrentSelectedUser = new UserInfo { Username = username };
            }

            if (!string.IsNullOrWhiteSpace(machine.RoomName))
            {
                ChatService.RoomName = machine.RoomName;
                _ = ChatService.InitializeAsync();
            }
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
                    mw.SetTopMost(true, false);
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
                System.Diagnostics.Debug.WriteLine($"❌ Toast error: {ex.Message}");
            }
        }

        private Window? m_window;
    }
}
