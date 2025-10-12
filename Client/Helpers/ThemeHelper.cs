using Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Client.Helpers
{
    public static class ThemeHelper
    {
        public static void ApplyDialogTheme(ContentDialog dialog)
        {
            if (dialog is null)
            {
                return;
            }

            if (Application.Current?.Resources is ResourceDictionary resources)
            {
                if (resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var background) && background is SolidColorBrush backgroundBrush)
                {
                    dialog.Background = backgroundBrush;
                }

                if (resources.TryGetValue("TextAppBackgroundColor", out var foreground) && foreground is SolidColorBrush foregroundBrush)
                {
                    dialog.Foreground = foregroundBrush;
                }
            }

            if (App.MainWindow?.Content is FrameworkElement root)
            {
                dialog.RequestedTheme = root.ActualTheme switch
                {
                    ElementTheme.Dark => ElementTheme.Dark,
                    ElementTheme.Light => ElementTheme.Light,
                    _ => dialog.RequestedTheme
                };
            }
        }
    }
}
