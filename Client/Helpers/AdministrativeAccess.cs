using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Client.Helpers
{
    internal static class AdministrativeAccess
    {
        internal const string ApplicationPassword = "901027";

        public static async Task<bool> RequestPasswordAsync(XamlRoot xamlRoot)
        {
            while (true)
            {
                var passwordBox = new PasswordBox
                {
                    PlaceholderText = "Mot de passe",
                    Width = 300
                };
                var dialog = new ContentDialog
                {
                    Title = "Mot de passe requis",
                    PrimaryButtonText = "Valider",
                    CloseButtonText = "Annuler",
                    DefaultButton = ContentDialogButton.Primary,
                    Content = passwordBox,
                    XamlRoot = xamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return false;

                if (IsPasswordValid(passwordBox.Password))
                    return true;

                var errorDialog = new ContentDialog
                {
                    Title = "Accès refusé",
                    Content = "Mot de passe incorrect.",
                    CloseButtonText = "Fermer",
                    XamlRoot = xamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private static bool IsPasswordValid(string? password) =>
            string.Equals(password, ApplicationPassword, StringComparison.Ordinal);
    }
}
