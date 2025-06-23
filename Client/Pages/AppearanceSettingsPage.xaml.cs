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

            if (FindName("TitleBarPicker") is ColorPicker tb)
                tb.Color = ColorUtils.FromHex(currentSettings.TitleBarColor);
            if (FindName("NavPicker") is ColorPicker np)
                np.Color = ColorUtils.FromHex(currentSettings.NavigationViewColor);
            if (FindName("MyBubblePicker") is ColorPicker mb)
                mb.Color = ColorUtils.FromHex(currentSettings.MyMessageColor);
            if (FindName("OtherBubblePicker") is ColorPicker ob)
                ob.Color = ColorUtils.FromHex(currentSettings.OtherMessageColor);
        }

        private void TitleBarColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.TitleBarColor = ColorUtils.ToHex(args.NewColor);
            var textColorTitleBar = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextTitleBarColor = ColorUtils.ToHex(textColorTitleBar);
            AppSettings.SetObject("Colors", currentSettings);
            UpdateResourceBrush("TextTitleBarColor", textColorTitleBar);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = (Grid)root.FindName("AppTitleBar");
                var nav = (NavigationView)root.FindName("nvSample");
                var titleText = (TextBlock)root.FindName("TitleBarTextBlock");
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
                var titleBar = (Grid)root.FindName("AppTitleBar");
                var nav = (NavigationView)root.FindName("nvSample");
                var titleText = (TextBlock)root.FindName("TitleBarTextBlock");
                NavZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.NavigationViewColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void MyMessageColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.MyMessageColor = ColorUtils.ToHex(args.NewColor);
            var textColor = ColorUtils.GetContrastingTextColor(args.NewColor);
            currentSettings.TextMyMessageColor = ColorUtils.ToHex(textColor);
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = (Grid)root.FindName("AppTitleBar");
                var nav = (NavigationView)root.FindName("nvSample");
                var titleText = (TextBlock)root.FindName("TitleBarTextBlock");
                MyMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.MyMessageColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void OtherMessageColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
        {
            currentSettings.OtherMessageColor = ColorUtils.ToHex(args.NewColor);
            var othertextColor = ColorUtils.FromHex(currentSettings.OtherMessageColor);
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = (Grid)root.FindName("AppTitleBar");
                var nav = (NavigationView)root.FindName("nvSample");
                var titleText = (TextBlock)root.FindName("TitleBarTextBlock");
                OtherMessageZone.Background = new SolidColorBrush(ColorUtils.FromHex(currentSettings.OtherMessageColor));
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.SetObject("Colors", currentSettings);
            if (App.MainWindow.Content is FrameworkElement root)
            {
                var titleBar = (Grid)root.FindName("AppTitleBar");
                var nav = (NavigationView)root.FindName("nvSample");
                var titleText = (TextBlock)root.FindName("TitleBarTextBlock");
                ApplyColors(currentSettings, titleBar, nav, titleText);
            }
        }

        public static void ApplyColors(AppColorSettings colors, Grid? titleBar = null, NavigationView? nav = null, TextBlock? titleTextBlock = null)
        {
            var titleColor = ColorUtils.FromHex(colors.TitleBarColor);
            var textColorTitleBar = ColorUtils.FromHex(colors.TextTitleBarColor);
            var navColor = ColorUtils.FromHex(colors.NavigationViewColor);
            var textNavColor = ColorUtils.FromHex(colors.TextNavigationViewColor);
            var pointerNavColor = ColorUtils.GetContrastingTextColor(textNavColor);
            var myColor = ColorUtils.FromHex(colors.MyMessageColor);
            var textMyColor = ColorUtils.FromHex(colors.TextMyMessageColor);
            var otherColor = ColorUtils.FromHex(colors.OtherMessageColor);

            UpdateResourceBrush("TitleBarColor", titleColor);
            UpdateResourceBrush("TextTitleBarColor", textColorTitleBar);
            UpdateResourceBrush("NavigationViewColor", navColor);
            UpdateResourceBrush("SystemAccentColor", titleColor);
            UpdateResourceBrush("SystemAccentColorLight3", navColor);
            UpdateResourceBrush("MyMessageColor", myColor);
            UpdateResourceBrush("TextMyMessageColor", textMyColor);
            UpdateResourceBrush("NavigationViewItemForeground", textNavColor);
            UpdateResourceBrush("NavigationViewItemForegroundSelected", textNavColor);
            UpdateResourceBrush("NavigationViewItemIconForeground", textNavColor);
            UpdateResourceBrush("NavigationViewItemIconForegroundSelected", textNavColor);
            UpdateResourceBrush("NavigationViewItemForegroundPointerOver", pointerNavColor);
            UpdateResourceBrush("NavigationViewItemIconForegroundPointerOver", pointerNavColor);
            UpdateResourceBrush("OtherMessageColor", otherColor);

            titleBar?.DispatcherQueue.TryEnqueue(() => titleBar.Background = new SolidColorBrush(titleColor));
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

            titleTextBlock?.DispatcherQueue.TryEnqueue(() => titleTextBlock.Foreground = new SolidColorBrush(textColorTitleBar));
        }

        private static void UpdateResourceBrush(string key, Windows.UI.Color color)
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
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

