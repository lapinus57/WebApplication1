using System;
using Client;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Client.Models;

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

            var dispatcher = dialog.DispatcherQueue ?? App.MainWindow?.DispatcherQueue;

            if (dispatcher is DispatcherQueue queue && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(() => ApplyDialogTheme(dialog));
                return;
            }

            var appliedCustomColors = TryApplyCustomDialogColors(dialog);

            if (!appliedCustomColors && Application.Current?.Resources is ResourceDictionary resources)
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
                dialog.XamlRoot ??= root.XamlRoot;

                var theme = root.RequestedTheme != ElementTheme.Default
                    ? root.RequestedTheme
                    : root.ActualTheme;

                if (theme is ElementTheme.Dark or ElementTheme.Light)
                {
                    dialog.RequestedTheme = theme;
                    return;
                }
            }

            var themeSetting = AppSettings.Get("AppTheme", "Dark");
            if (Enum.TryParse<ApplicationTheme>(themeSetting, out var appTheme))
            {
                dialog.RequestedTheme = appTheme == ApplicationTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
        }

        private static bool TryApplyCustomDialogColors(ContentDialog dialog)
        {
            try
            {
                var colors = AppSettings.GetObject<AppColorSettings>("Colors");
                var applied = false;

                if (!string.IsNullOrWhiteSpace(colors.AppBackgroundColor))
                {
                    dialog.Background = new SolidColorBrush(ColorUtils.FromHex(colors.AppBackgroundColor));
                    applied = true;
                }

                if (!string.IsNullOrWhiteSpace(colors.TextAppBackgroundColor))
                {
                    dialog.Foreground = new SolidColorBrush(ColorUtils.FromHex(colors.TextAppBackgroundColor));
                    applied = true;
                }

                return applied;
            }
            catch (Exception ex)
            {
                Logger.LogException("[ThemeHelper] Applying custom dialog colors failed", ex, "CLI23");
                return false;
            }
        }
    }
}
