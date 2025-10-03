using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private bool _isClearingBackups;

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

        private static string FormatBytes(long bytes)
        {
            string[] units = new[] { "octets", "Ko", "Mo", "Go", "To" };
            double size = bytes;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{bytes} {units[unitIndex]}"
                : $"{size:0.##} {units[unitIndex]}";
        }

        public void ScrollMessagesToEnd()
        {
            if (contentFrame.Content is Pages.ChatPage chat)
            {
                chat.ScrollToLastMessage();
            }
        }

        private async void ClearBackups_Click(object sender, RoutedEventArgs e)
        {
            if (_isClearingBackups)
                return;

            _isClearingBackups = true;
            ClearBackupsButton.IsEnabled = false;

            ClearBackupsStatusText.Visibility = Visibility.Visible;
            ClearBackupsStatusText.Text = "Analyse des sauvegardes...";
            ClearBackupsProgress.Visibility = Visibility.Visible;
            ClearBackupsProgress.IsIndeterminate = true;

            try
            {
                var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Directory.Exists(appFolder))
                {
                    var patterns = new[] { "*.bak", "*.bak*", "*.backup", "*.backup*" };
                    foreach (var pattern in patterns)
                    {
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(appFolder, pattern, SearchOption.AllDirectories))
                            {
                                backups.Add(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException($"Erreur lors de l'énumération des fichiers de sauvegarde ({pattern})", ex);
                        }
                    }
                }

                if (backups.Count == 0)
                {
                    ClearBackupsProgress.IsIndeterminate = false;
                    ClearBackupsProgress.Visibility = Visibility.Collapsed;
                    ClearBackupsStatusText.Text = "Aucune sauvegarde à supprimer.";
                    return;
                }

                ClearBackupsProgress.IsIndeterminate = false;
                ClearBackupsProgress.Minimum = 0;
                ClearBackupsProgress.Maximum = backups.Count;
                ClearBackupsProgress.Value = 0;

                var dispatcher = DispatcherQueue;
                int processed = 0;
                int deleted = 0;
                int failed = 0;
                long freedBytes = 0;

                await Task.Run(() =>
                {
                    foreach (var file in backups)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            var length = info.Exists ? info.Length : 0;
                            File.Delete(file);
                            Interlocked.Add(ref freedBytes, length);
                            Interlocked.Increment(ref deleted);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            Logger.LogException($"Erreur lors de la suppression de la sauvegarde '{file}'", ex);
                        }

                        var current = Interlocked.Increment(ref processed);
                        dispatcher.TryEnqueue(() =>
                        {
                            ClearBackupsProgress.Value = current;
                            ClearBackupsStatusText.Text = $"Suppression des sauvegardes... {current}/{backups.Count}";
                        });
                    }
                });

                ClearBackupsProgress.Visibility = Visibility.Collapsed;

                var summary = $"Suppression terminée. {deleted} fichier(s) supprimé(s)";
                if (freedBytes > 0)
                {
                    summary += $" ({FormatBytes(freedBytes)} libérés)";
                }
                summary += ".";

                if (failed > 0)
                {
                    summary += $" {failed} fichier(s) n'ont pas pu être supprimés.";
                }

                ClearBackupsStatusText.Text = summary;
            }
            catch (Exception ex)
            {
                Logger.LogException("Erreur inattendue lors du nettoyage des sauvegardes", ex);
                ClearBackupsStatusText.Text = $"Erreur : {ex.Message}";
            }
            finally
            {
                ClearBackupsButton.IsEnabled = true;
                ClearBackupsProgress.IsIndeterminate = false;
                ClearBackupsProgress.Visibility = Visibility.Collapsed;
                _isClearingBackups = false;
            }
        }

        private async void ChangeAccount_Click(object sender, RoutedEventArgs e)
        {
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            Directory.CreateDirectory(appFolder);
            var settingsFiles = Directory.GetFiles(appFolder, "*_settings.json");
            var users = settingsFiles.Select(f => Path.GetFileNameWithoutExtension(f).Replace("_settings", "")).ToList();

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
