using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;
using Microsoft.UI;
using Client.Helpers;

namespace Client
{
    public sealed partial class MainWindow : Window
    {
        public bool IsTopMost { get; private set; }

        public bool IsChatPageActive => contentFrame.CurrentSourcePageType == typeof(Pages.ChatPage);

        public MainWindow()
        {
            this.InitializeComponent();
            // Use the custom AppTitleBar element as the window title bar
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // Hide default title bar button backgrounds for seamless look
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var titleBar = appWindow.TitleBar;
            nvSample.SelectedItem = nvSample.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => (string)item.Tag == "ChatPage");
            contentFrame.Navigate(typeof(Pages.ChatPage));
           
        }

        private void nvSample_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                Type pageType = tag switch
                {
                    "ChatPage" => typeof(Pages.ChatPage),
                    "HistoryPage" => typeof(Pages.HistoryPage),
                    _ => null
                };
                if (pageType != null && contentFrame.CurrentSourcePageType != pageType)
                {
                    contentFrame.Navigate(pageType);
                }
                if (args.IsSettingsSelected)
                {
                    contentFrame.Navigate(typeof(Pages.SettingsPage));
                }
            }
        }
        public Pages.ChatPage ShowChatPage()
        {
            var chatItem = nvSample.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => (string)item.Tag == "ChatPage");
            if (chatItem != null)
            {
                nvSample.SelectedItem = chatItem;
            }

            if (contentFrame.CurrentSourcePageType != typeof(Pages.ChatPage))
            {
                contentFrame.Navigate(typeof(Pages.ChatPage));
            }

            return contentFrame.Content as Pages.ChatPage;
        }

        public void SetTopMost(bool topMost, bool activate = false)
        {
            WindowHelper.SetTopMost(this, topMost, activate);
            IsTopMost = topMost;
        }

        public void ScrollMessagesToEnd()
        {
            if (contentFrame.Content is Pages.ChatPage chat)
            {
                chat.ScrollToLastMessage();
            }
        }
    }
}
