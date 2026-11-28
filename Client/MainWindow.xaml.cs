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
        private readonly NativeWindowForceCloseMenu _forceCloseMenu;
        private bool _isUpdatingAgendaToggle;

        public bool IsChatPageActive => contentFrame.CurrentSourcePageType == typeof(Pages.ChatPage);

        public MainWindow()
        {
            this.InitializeComponent();
            App.ChatService.UpdateAvailable += ChatService_UpdateAvailable;
            // Use the custom AppTitleBar element as the window title bar
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // Hide default title bar button backgrounds for seamless look
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _forceCloseMenu = new NativeWindowForceCloseMenu(hwnd, () =>
            {
                if (Application.Current is App app)
                {
                    app.RequestForceClose();
                }
            });
            Closed += (_, _) => _forceCloseMenu.Dispose();
            RefreshAgendaSwitchState();

        }

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            if (_appWindow is not null)
            {
                // Make the entire title bar draggable, excluding interactive controls like the account button.
                // SetDragRectangles expects coordinates in physical pixels, so scale from effective pixels using the XamlRoot scale.
                SetDragRegion();
            }
        }

        private void SetDragRegion()
        {
            if (TitleBarDragRegion is null || !TitleBarDragRegion.IsLoaded || AppTitleBar?.XamlRoot is null)
            {
                return;
            }

            if (TitleBarDragRegion.ActualWidth <= 0 || TitleBarDragRegion.ActualHeight <= 0)
            {
                return;
            }

            double scale = AppTitleBar.XamlRoot.RasterizationScale;
            var transform = TitleBarDragRegion.TransformToVisual(AppTitleBar);
            var bounds = transform.TransformBounds(new Rect(0, 0, TitleBarDragRegion.ActualWidth, TitleBarDragRegion.ActualHeight));
            var dragRect = new RectInt32(
                (int)(bounds.X * scale),
                (int)(bounds.Y * scale),
                (int)(bounds.Width * scale),
                (int)(bounds.Height * scale));

            _appWindow.TitleBar.SetDragRectangles(new[] { dragRect });
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
                    "SmsMailPage" => typeof(Pages.SmsMailPage),
                    "FundusPage" => typeof(Pages.FundusPage),
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

        public void RefreshAgendaSwitchState()
        {
            var config = MachineConfig.Load();
            _isUpdatingAgendaToggle = true;
            AutoSwitchToggle.IsOn = config.AgendaModeEnabled && config.AutoSwitchEnabled;
            AutoSwitchPanel.Visibility = config.AgendaModeEnabled ? Visibility.Visible : Visibility.Collapsed;
            _isUpdatingAgendaToggle = false;
            DispatcherQueue.TryEnqueue(SetDragRegion);
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

        private void AutoSwitchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingAgendaToggle)
            {
                return;
            }

            var config = MachineConfig.Load();
            config.AutoSwitchEnabled = config.AgendaModeEnabled && AutoSwitchToggle.IsOn;
            MachineConfig.Save(config);

            if (Application.Current is App app)
            {
                app.RefreshAgendaTimer();
            }
        }
    
        private Client.Services.ClientUpdateNotice? _update;

        /// <summary>Un poste plus récent existe, ou un paquet plus récent est dans le dossier des mises à jour.</summary>
        private void ChatService_UpdateAvailable(object? sender, Client.Services.ClientUpdateNotice notice)
        {
            _update = notice;
            var where = notice.LatestMachine.Length > 0 ? $" (déjà installée sur {notice.LatestMachine})" : "";
            UpdateBar.Message = $"Version {notice.LatestVersion} disponible{where} ; ce poste a la version {notice.CurrentVersion}. "
                + (notice.InstallerPath != null ? "« Mettre à jour » ouvre l'installation ; fermez EyeChat quand l'installeur le demande."
                   : notice.UpdateFolder.Length > 0 ? $"Paquet introuvable dans {notice.UpdateFolder} : déposez-y le dossier Client_{notice.LatestVersion}_Test."
                   : "Aucun dossier de mise à jour n'est réglé sur le serveur (administration → Postes).");
            UpdateButton.Content = notice.InstallerPath != null ? "Mettre à jour" : "Ouvrir le dossier";
            UpdateButton.IsEnabled = notice.InstallerPath != null || notice.UpdateFolder.Length > 0;
            UpdateBar.IsOpen = true;
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_update == null) return;
            try
            {
                var target = _update.InstallerPath ?? _update.PackageFolder ?? _update.UpdateFolder;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UpdateBar.Severity = InfoBarSeverity.Warning;
                UpdateBar.Message = "Impossible d'ouvrir la mise à jour : " + ex.Message;
            }
        }
    }
}
