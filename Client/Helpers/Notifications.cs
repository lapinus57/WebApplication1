using System;
using Microsoft.Windows.AppNotifications;

namespace Client.Helpers
{
    /// <summary>
    /// Notifications Windows d'EyeChat (nouveau patient, message, serveur injoignable…).
    /// L'enregistrement auprès de Windows se fait une seule fois, au démarrage : le refaire avant chaque
    /// notification échoue, et l'erreur, interceptée, empêchait silencieusement l'affichage.
    /// L'application empaquetée (MSIX) déclare son activateur de notifications dans Package.appxmanifest.
    /// </summary>
    public static class Notifications
    {
        private static bool _registered;
        private static readonly object Gate = new();

        /// <summary>Clic sur une notification : appelé hors du fil de l'interface.</summary>
        public static event Action? Clicked;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_registered) return;
                try
                {
                    // Le gestionnaire doit être branché avant Register.
                    AppNotificationManager.Default.NotificationInvoked += (_, _) => Clicked?.Invoke();
                    AppNotificationManager.Default.Register();
                    _registered = true;
                    Logger.Log("[Notifications] Notifications Windows prêtes.");
                }
                catch (Exception ex)
                {
                    Logger.LogException("[Notifications] Enregistrement des notifications Windows impossible", ex, "CLI80");
                }
            }
        }

        /// <summary>Affiche la notification ; retourne false (et l'écrit dans le journal) si Windows la refuse.</summary>
        public static bool Show(AppNotification notification, string context)
        {
            Initialize();
            try
            {
                if (!AppNotificationManager.IsSupported())
                {
                    Logger.Log($"[Notifications] {context} : notifications non prises en charge sur ce poste.");
                    return false;
                }
                AppNotificationManager.Default.Show(notification);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException($"[Notifications] {context} : notification non affichée", ex, "CLI81");
                return false;
            }
        }

        /// <summary>Notifications désactivées par Windows pour EyeChat (Paramètres → Système → Notifications) ?</summary>
        public static bool DisabledByWindows()
        {
            try { return AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled; }
            catch { return false; }
        }
    }
}
