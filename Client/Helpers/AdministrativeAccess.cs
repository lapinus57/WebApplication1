using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Client.Helpers
{
    internal static class AdministrativeAccess
    {
        // Mot de passe historique : utilisé seulement si le serveur est injoignable ou trop ancien
        // pour vérifier lui-même (Administration:ApplicationPassword côté serveur).
        private const string LegacyApplicationPassword = "901027";

        private static string? _validatedPassword;

        /// <summary>Mot de passe accepté lors de la dernière saisie, transmis au serveur avec les actions d'administration.</summary>
        internal static string ApplicationPassword => _validatedPassword ?? LegacyApplicationPassword;

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

                if (await IsPasswordValidAsync(passwordBox.Password))
                {
                    _validatedPassword = passwordBox.Password;
                    return true;
                }

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

        private static async Task<bool> IsPasswordValidAsync(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            var serverAnswer = await App.ChatService.VerifyApplicationPasswordAsync(password);
            return serverAnswer ?? string.Equals(password, LegacyApplicationPassword, StringComparison.Ordinal);
        }
    }
}
