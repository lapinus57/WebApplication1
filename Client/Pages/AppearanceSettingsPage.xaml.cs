using Client.ViewModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using Client.Models;
using Client.Helpers;
using System.Linq;

namespace Client.Pages
{
    public sealed partial class AppearanceSettingsPage : Page
    {
        public SettingsViewModel ViewModelSettings { get; } = new();

        private AppColorSettings currentSettings = new();

        public AppearanceSettingsPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModelSettings;
            ViewModelSettings.Load();
            Debug.WriteLine($"[AppearanceSettingsPage] ViewModel instance: {ViewModelSettings.GetHashCode()}");

            currentSettings = AppSettings.GetObject<AppColorSettings>("Colors");
            RefreshColorPreview();
            if (FindName("ThemeCombo") is ComboBox theme)
            {
                theme.SelectedIndex = ViewModelSettings.AppTheme switch
                {
                    "Light" => 0,
                    "Dark" => 1,
                    _ => 1
                };
            }
        }

        private void TitleBarAccentColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.TitleBarColor = ColorUtils.ToHex(args.NewColor);
            var textColorTitleBar = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextTitleBarColor = ColorUtils.ToHex(textColorTitleBar);
            currentSettings.SystemAccentColorDark1 = ColorUtils.ToHex(args.NewColor);
            AppSettings.SetObject("Colors", currentSettings);

            UpdateResourceBrush("TextTitleBarColor", textColorTitleBar);
            UpdateResourceBrush("SystemAccentColorDark1", args.NewColor);
            UpdateResourceBrush("AccentColor", args.NewColor);
            UpdateResourceBrush("SystemControlHighlightAccentBrush", args.NewColor);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = root.FindName("AppTitleBar") as Grid;
                var nav = root.FindName("nvSample") as NavigationView;
                var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
                TitleBarZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.TitleBarColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void NavColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.NavigationViewColor = ColorUtils.ToHex(args.NewColor);
            var textNavColor = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextNavigationViewColor = ColorUtils.ToHex(textNavColor);
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var nav = root.FindName("nvSample") as NavigationView;
                NavZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.NavigationViewColor));
                ApplyNavigationColors(currentSettings, nav);
            }
        }

        private static void ApplyNavigationColors(AppColorSettings colors, NavigationView? nav = null)
        {
            var navColor = ColorUtils.FromHex(colors.NavigationViewColor);
            var textNavColor = ColorUtils.FromHex(colors.TextNavigationViewColor);
            var pointerNavColor = ColorUtils.GetContrastingTextColor(textNavColor);

            UpdateResourceBrush("NavigationViewColor", navColor);
            UpdateResourceBrush("NavigationViewItemForeground", textNavColor);
            UpdateResourceBrush("NavigationViewItemForegroundSelected", textNavColor);
            UpdateResourceBrush("NavigationViewItemIconForeground", textNavColor);
            UpdateResourceBrush("NavigationViewItemIconForegroundSelected", textNavColor);
            UpdateResourceBrush("NavigationViewItemForegroundPointerOver", pointerNavColor);
            UpdateResourceBrush("NavigationViewItemIconForegroundPointerOver", pointerNavColor);
            UpdateResourceBrush("PivotHeaderForeground", textNavColor);

            nav?.DispatcherQueue.TryEnqueue(() =>
            {
                nav.Background = new SolidColorBrush(navColor);
                foreach (var item in nav.MenuItems.OfType<NavigationViewItem>())
                {
                    item.Foreground = new SolidColorBrush(textNavColor);
                }
                if (nav.SettingsItem is NavigationViewItem settingsItem)
                {
                    settingsItem.Foreground = new SolidColorBrush(textNavColor);
                }
            });
        }

        private void MyMessageColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.MyMessageColor = ColorUtils.ToHex(args.NewColor);
            var textColor = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextMyMessageColor = ColorUtils.ToHex(textColor);
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = root.FindName("AppTitleBar") as Grid;
                var nav = root.FindName("nvSample") as NavigationView;
                var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
                MyMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.MyMessageColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void OtherMessageColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.OtherMessageColor = ColorUtils.ToHex(args.NewColor);
            var textColor = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextOtherMessageColor = ColorUtils.ToHex(textColor);
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = root.FindName("AppTitleBar") as Grid;
                var nav = root.FindName("nvSample") as NavigationView;
                var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
                OtherMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.OtherMessageColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            {
                ViewModelSettings.AppTheme = theme;
                currentSettings = AppSettings.GetObject<AppColorSettings>("Colors");
                RefreshColorPreview();
            }
        }

        private void RefreshColorPreview()
        {
            if (FindName("TitleBarAccentPicker") is ColorPicker tb)
                tb.Color = ColorUtils.FromHex(currentSettings.TitleBarColor);
            if (FindName("NavPicker") is ColorPicker np)
                np.Color = ColorUtils.FromHex(currentSettings.NavigationViewColor);
            if (FindName("MyBubblePicker") is ColorPicker mb)
                mb.Color = ColorUtils.FromHex(currentSettings.MyMessageColor);
            if (FindName("OtherBubblePicker") is ColorPicker ob)
                ob.Color = ColorUtils.FromHex(currentSettings.OtherMessageColor);

            TitleBarZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.TitleBarColor));
            NavZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.NavigationViewColor));
            MyMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.MyMessageColor));
            OtherMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.OtherMessageColor));
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = root.FindName("AppTitleBar") as Grid;
                var nav = root.FindName("nvSample") as NavigationView;
                var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        public static void ApplyColors(
            AppColorSettings colors,
            Grid? titleBar = null,
            NavigationView? nav = null,
            TextBlock? titleTextBlock = null)
        {
            var titleColor = ColorUtils.FromHex(colors.TitleBarColor);
            var textColorTitleBar = ColorUtils.FromHex(colors.TextTitleBarColor);
            var navColor = ColorUtils.FromHex(colors.NavigationViewColor);
            var textNavColor = ColorUtils.FromHex(colors.TextNavigationViewColor);
            var myColor = ColorUtils.FromHex(colors.MyMessageColor);
            var textMyColor = ColorUtils.FromHex(colors.TextMyMessageColor);
            var otherColor = ColorUtils.FromHex(colors.OtherMessageColor);
            var textOtherColor = ColorUtils.FromHex(colors.TextOtherMessageColor);
            var accentDark1 = ColorUtils.FromHex(colors.SystemAccentColorDark1);
            UpdateResourceBrush("TitleBarColor", titleColor);
            UpdateResourceBrush("TextTitleBarColor", textColorTitleBar);
            UpdateResourceBrush("SystemAccentColor", titleColor);
            UpdateResourceBrush("AccentColor", titleColor);
            UpdateResourceBrush("SystemAccentColorLight3", navColor);
            UpdateResourceBrush("SystemControlHighlightAccentBrush", titleColor);
            UpdateResourceBrush("MyMessageColor", myColor);
            UpdateResourceBrush("TextMyMessageColor", textMyColor);
            UpdateResourceBrush("OtherMessageColor", otherColor);
            UpdateResourceBrush("TextOtherMessageColor", textOtherColor);
            UpdateResourceBrush("SystemAccentColorDark1", accentDark1);

            ApplyNavigationColors(colors, nav);

            titleBar?.DispatcherQueue.TryEnqueue(() => titleBar.Background = new SolidColorBrush(titleColor));

            titleTextBlock?.DispatcherQueue.TryEnqueue(() => titleTextBlock.Foreground = new SolidColorBrush(textColorTitleBar));
        }

        private static void UpdateResourceBrush(string key, Windows.UI.Color color)
        {
            var resources = Application.Current.Resources;
            if (resources[key] is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else if (resources[key] is Windows.UI.Color)
            {
                resources[key] = color;
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }
}

