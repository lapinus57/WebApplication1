using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.IO;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.UI;
using Microsoft.UI;
using Client.Helpers;
using Windows.Graphics;
using Windows.Foundation;

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

        private async void ChangeAccount_Click(object sender, RoutedEventArgs e)
        {
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
                XamlRoot = this.Content.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            var combo = new ComboBox { ItemsSource = users, PlaceholderText = "Utilisateur" };
            var newBox = new TextBox { PlaceholderText = "Nouvel utilisateur" };
            stack.Children.Add(combo);
            stack.Children.Add(newBox);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var name = !string.IsNullOrWhiteSpace(newBox.Text) ? newBox.Text.Trim() : combo.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await ((App)Application.Current).ChangeUserAsync(name);
                }
            }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            await ((App)Application.Current).LogoutAsync();
        }
    }
}
