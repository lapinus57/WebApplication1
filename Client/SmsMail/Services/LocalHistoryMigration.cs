using System.IO;
using System.Threading.Tasks;
using EyeChat.SmsMail;
using Microsoft.Data.Sqlite;

namespace Client.SmsMail.Services
{
    /// <summary>
    /// L'historique était autrefois gardé sur chaque poste (history.db). Au premier passage, il est envoyé au
    /// serveur (lignes et réservations anti-doublon, sans écraser ce qui y est déjà), puis le fichier local est
    /// renommé pour ne plus être relu. En cas d'échec, le fichier reste en place et l'envoi sera retenté.
    /// </summary>
    public static class LocalHistoryMigration
    {
        public const string MigratedSuffix = ".envoye-au-serveur";

        public static async Task<int?> MigrateAsync(string localPath, IMessageHistory server)
        {
            if (!File.Exists(localPath)) return null;
            var local = new SmsMailHistoryStore(localPath);
            var records = await local.LoadAsync();
            var reservations = await local.LoadReservationsAsync();
            var imported = await server.ImportAsync(records, reservations);
            SqliteConnection.ClearAllPools();
            File.Move(localPath, localPath + MigratedSuffix, overwrite: true);
            return imported;
        }
    }
}
