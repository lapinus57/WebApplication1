using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Helpers;
using Client.Services;
using System;
using System.Threading;

namespace Client.Pages
{
    public sealed partial class ConnectionPage : Page
    {
        private CancellationTokenSource? _searchCancellation;
        public string ServerAddress { get; set; } = string.Empty;

        public ConnectionPage()
        {
            this.InitializeComponent();
            var cfg = ConnectionConfig.Load();
            App.ChatService.ServerAddress = cfg.ServerAddress;
            ServerAddress = App.ChatService.ServerAddress;
            DataContext = this;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            App.ChatService.ServerAddress = ServerAddress;
            ConnectionConfig.Save(new ConnectionConfig { ServerAddress = App.ChatService.ServerAddress });
            await App.ChatService.InitializeAsync();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();

            SearchButton.IsEnabled = false;
            CancelSearchButton.Visibility = Visibility.Visible;
            SearchStatusPanel.Visibility = Visibility.Visible;
            SearchProgress.IsActive = true;
            SearchStatusText.Text = "Préparation de la recherche…";

            var progress = new Progress<NetworkScanProgress>(value =>
                SearchStatusText.Text = $"Recherche sur le réseau… {value.Tested}/{value.Total}");

            try
            {
                var address = await NetworkScanner.FindServerAsync(cancellationToken: _searchCancellation.Token, progress: progress);
                if (!string.IsNullOrEmpty(address))
                {
                    ServerAddress = address;
                    AddressBox.Text = ServerAddress;
                    SearchStatusText.Text = $"Serveur trouvé : {address}";
                }
                else
                {
                    SearchStatusText.Text = "Aucun serveur EyeChat trouvé sur le réseau local.";
                }
            }
            catch (OperationCanceledException)
            {
                SearchStatusText.Text = "Recherche annulée.";
            }
            finally
            {
                SearchProgress.IsActive = false;
                SearchButton.IsEnabled = true;
                CancelSearchButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelSearch_Click(object sender, RoutedEventArgs e) => _searchCancellation?.Cancel();
    }
}
