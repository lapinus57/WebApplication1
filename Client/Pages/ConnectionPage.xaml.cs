using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Helpers;
using Client.Services;

namespace Client.Pages
{
    public sealed partial class ConnectionPage : Page
    {
        public string ServerAddress { get; set; } = string.Empty;

        public ConnectionPage()
        {
            this.InitializeComponent();
            var cfg = ConnectionConfig.Load();
            ServerAddress = cfg.ServerAddress;
            DataContext = this;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            ConnectionConfig.Save(new ConnectionConfig { ServerAddress = ServerAddress });
            App.ChatService.ServerAddress = ServerAddress;
            await App.ChatService.InitializeAsync();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            AddressBox.Text = "Recherche...";
            var address = await NetworkScanner.FindServerAsync();
            if (!string.IsNullOrEmpty(address))
            {
                ServerAddress = address;
                AddressBox.Text = ServerAddress;
            }
            else
            {
                ServerAddress = string.Empty;
                AddressBox.Text = "Serveur introuvable";
            }
        }
    }
}
