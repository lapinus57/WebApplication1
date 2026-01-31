using System;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages
{
    public sealed partial class AppointmentSearchSettingsPage : Page
    {
        private readonly MachineConfig _config;
        private bool _isLoaded;

        public AppointmentSearchConfig AppointmentConfig { get; private set; } = new();
        public string AccessOleDbProvider { get; set; } = string.Empty;
        public string AccessWorkgroupPath { get; set; } = string.Empty;
        public string AccessUserName { get; set; } = string.Empty;
        public string AccessPassword { get; set; } = string.Empty;

        public AppointmentSearchSettingsPage()
        {
            InitializeComponent();
            _config = MachineConfig.Load();
            AccessOleDbProvider = _config.AccessOleDbProvider;
            AccessWorkgroupPath = _config.AccessWorkgroupPath;
            AccessUserName = _config.AccessUserName;
            AccessPassword = _config.AccessPassword;
            DataContext = this;
            AccessPasswordBox.Password = AccessPassword;
            Loaded += AppointmentSearchSettingsPage_Loaded;
        }

        private async void AppointmentSearchSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            AccessPasswordBox.Password = AccessPassword;
            await LoadAppointmentConfigAsync();
        }

        private async Task LoadAppointmentConfigAsync()
        {
            try
            {
                var cfg = await App.ChatService.GetAppointmentSearchConfigAsync();
                AppointmentConfig = cfg ?? new AppointmentSearchConfig();
                this.Bindings.Update();
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppointmentSearchSettingsPage] Failed to load appointment config", ex, "CLI45");
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void AccessPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            if (sender is PasswordBox box)
            {
                AccessPassword = box.Password;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.AccessOleDbProvider = AccessOleDbProvider?.Trim() ?? string.Empty;
            _config.AccessWorkgroupPath = AccessWorkgroupPath?.Trim() ?? string.Empty;
            _config.AccessUserName = AccessUserName?.Trim() ?? string.Empty;
            _config.AccessPassword = AccessPassword ?? string.Empty;
            MachineConfig.Save(_config);
            AccessPasswordBox.Password = AccessPassword;

            try
            {
                await App.ChatService.SaveAppointmentSearchConfigAsync(AppointmentConfig);
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppointmentSearchSettingsPage] Failed to save appointment config", ex, "CLI46");
            }
        }
    }
}
