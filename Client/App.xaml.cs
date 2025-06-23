using Microsoft.UI.Xaml;
using System;
using Client.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Client.Models;
using Client.Helpers;

namespace Client
{
    public partial class App : Application
    {
        public static SignalRService ChatService { get; } = new SignalRService();
        public static Window? MainWindow { get; private set; }
        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            MainWindow = m_window;

            ChatService.Dispatcher = m_window.DispatcherQueue;
            ChatService.OnMessageReceived += ChatService_OnMessageReceived;
            _ = ChatService.InitializeAsync();

            m_window.Activate();
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
