using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;
using Microsoft.UI;
using Windows.Graphics;
using Windows.Foundation;
using Client.Helpers;

namespace Client
{
    public sealed partial class MainWindow : Window
    {
        public bool IsTopMost { get; private set; }
        private readonly AppWindow _appWindow;

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
            _appWindow = AppWindow.GetFromWindowId(windowId);
            var titleBar = _appWindow.TitleBar;
            nvSample.SelectedItem = nvSample.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => (string)item.Tag == "ChatPage");
            contentFrame.Navigate(typeof(Pages.ChatPage));

        }

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            if (_appWindow is not null)
            {
                // Make the entire title bar draggable, excluding interactive controls like the account button.
                // SetDragRectangles expects coordinates in physical pixels, so scale from effective pixels using the XamlRoot scale.
                double scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1.0;
                var dragRect = new RectInt32(
                    0,
                    0,
                    (int)(AppTitleBar.ActualWidth * scale),
                    (int)(AppTitleBar.ActualHeight * scale));

                _appWindow.TitleBar.SetDragRectangles(new[] { dragRect });
            }
        }

        private void nvSample_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                Type? pageType = tag switch
                {
                    "ChatPage" => typeof(Pages.ChatPage),
                    "HistoryPage" => typeof(Pages.HistoryPage),
                    "AppointmentSearchPage" => typeof(Pages.AppointmentSearchPage),
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
        public Pages.ChatPage? ShowChatPage()
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

        public void BringToForeground()
        {
            if (IsTopMost)
            {
                SetTopMost(true, true);
            }
            else
            {
                SetTopMost(true, true);
                SetTopMost(false, false);
            }
        }

        public void ScrollMessagesToEnd()
        {
            if (contentFrame.Content is Pages.ChatPage chat)
            {
                chat.ScrollToLastMessage();
            }
        }

        public void SetAccountState(bool isConnected)
        {
            ChangeAccountMenuItem.Text = isConnected ? "Changer de compte" : "Choisir un compte";
            LogoutMenuItem.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;

            if (!isConnected)
            {
                PersonPic.Initials = string.Empty;
            }
        }

        private async void ChangeAccount_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app)
                return;

            var name = await app.PromptForAccountSelectionAsync();
            if (!string.IsNullOrWhiteSpace(name))
            {
                await app.ChangeUserAsync(name);
            }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            await ((App)Application.Current).LogoutAsync();
        }
    }
}
