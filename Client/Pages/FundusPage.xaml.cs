using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Client.Pages;

public sealed partial class FundusPage : Page
{
    private Uri? _origin;
    private bool _closed;

    public FundusPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var server = new Uri(App.ChatService.ServerAddress);
            // The existing server exposes its authenticated administration on HTTPS :5443.
            var target = new UriBuilder(server) { Scheme = "https", Path = "/Fundus", Query = "", Fragment = "", UserName = "", Password = "" };
            if (server.Scheme != "https" || server.Port == 5000) target.Port = 5443;
            _origin = target.Uri;
            await Browser.EnsureCoreWebView2Async();
            if (_closed) return;
            Browser.CoreWebView2.CookieManager.DeleteAllCookies();
            Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !SameOrigin(uri)) args.Cancel = true;
            };
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                StatusBar.IsOpen = !args.IsSuccess;
                if (!args.IsSuccess)
                {
                    StatusBar.Severity = InfoBarSeverity.Error;
                    StatusBar.Message = "Page inaccessible. Vérifiez que le serveur est à jour, joignable en HTTPS sur le port 5443 et que son certificat est reconnu par Windows.";
                }
            };
            Browser.Source = _origin;
        }
        catch
        {
            if (_closed) return;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = "Impossible d’ouvrir Préparation FO. Vérifiez la connexion au serveur et l’installation de WebView2.";
        }
    }

    private bool SameOrigin(Uri uri) => _origin != null && uri.Scheme == _origin.Scheme && uri.Host == _origin.Host && uri.Port == _origin.Port;
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_origin != null && Browser.CoreWebView2 != null) Browser.CoreWebView2.Navigate(_origin.AbsoluteUri);
    }
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _closed = true;
        Browser.CoreWebView2?.CookieManager.DeleteAllCookies();
        Browser.Close();
    }
}
