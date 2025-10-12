using System;

namespace Client.Models
{
    public static class AppColorPalettes
    {
        public static AppColorSettings CreateLightPalette()
        {
            return new AppColorSettings
            {
                TitleBarColor = "#FF0078D7",
                TextTitleBarColor = "#FFFFFFFF",
                NavigationViewColor = "#FFE6F1FF",
                TextNavigationViewColor = "#FF000000",
                MyMessageColor = "#FFCCE5FF",
                TextMyMessageColor = "#FF000000",
                OtherMessageColor = "#FFD9F2DC",
                TextOtherMessageColor = "#FF000000",
                AppBackgroundColor = "#FFFFFFFF",
                TextAppBackgroundColor = "#FF000000",
                SystemAccentColorDark1 = "#FF0078D7"
            };
        }

        public static AppColorSettings CreateDarkPalette()
        {
            return new AppColorSettings
            {
                TitleBarColor = "#FF202020",
                TextTitleBarColor = "#FFFFFFFF",
                NavigationViewColor = "#FF1E1E1E",
                TextNavigationViewColor = "#FFFFFFFF",
                MyMessageColor = "#FF2F5D8C",
                TextMyMessageColor = "#FFFFFFFF",
                OtherMessageColor = "#FF2F6F4A",
                TextOtherMessageColor = "#FFFFFFFF",
                AppBackgroundColor = "#FF0F0F0F",
                TextAppBackgroundColor = "#FFFFFFFF",
                SystemAccentColorDark1 = "#FF0078D7"
            };
        }

        public static bool AreEquivalent(AppColorSettings? first, AppColorSettings? second)
        {
            if (first is null || second is null)
            {
                return false;
            }

            return string.Equals(first.TitleBarColor, second.TitleBarColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.TextTitleBarColor, second.TextTitleBarColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.NavigationViewColor, second.NavigationViewColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.TextNavigationViewColor, second.TextNavigationViewColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.MyMessageColor, second.MyMessageColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.TextMyMessageColor, second.TextMyMessageColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.OtherMessageColor, second.OtherMessageColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.TextOtherMessageColor, second.TextOtherMessageColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.AppBackgroundColor, second.AppBackgroundColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.TextAppBackgroundColor, second.TextAppBackgroundColor, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.SystemAccentColorDark1, second.SystemAccentColorDark1, StringComparison.OrdinalIgnoreCase);
        }
    }
}
