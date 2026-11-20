using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

// Ancien historique d'un poste envoyé à l'historique commun (simulé par une seconde base).
static class HistoryMigrationChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var day = new DateTime(2026, 10, 23);
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        PreparedMessage Sms(int i) => MessagePreparation.Prepare(new[]
        {
            new RdvInfo { Id = 950 + i, ReferenceFamilyNumber = 950 + i, Prenom = "P" + i, Nom = "Migr" + i, DateRDV = day, HeureRDV = day.AddHours(9), Tel1 = $"06223344{i:D2}" }
        }, day, settings).Single(m => m.Channel == "SMS");
        var directory = Path.Combine(Path.GetTempPath(), "migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var localPath = Path.Combine(directory, "history.db");
            var local = new MessageHistory(localPath);
            Check(await local.ReserveAsync(Sms(1)), "Local send reserved");
            await local.UpdateAsync(Sms(1).Key, "Accepté par OVH", 77);
            Check(await local.ReserveAsync(Sms(2)), "Second local send reserved");

            var shared = new MessageHistory(Path.Combine(directory, "server.db"));
            Check(await shared.ReserveAsync(Sms(3)), "Another workstation already sent on the server");

            var migrated = await LocalHistoryMigration.MigrateAsync(localPath, shared);
            Check(migrated == 2, "Both local messages added to the shared history");
            Check(!File.Exists(localPath) && File.Exists(localPath + LocalHistoryMigration.MigratedSuffix), "Local file renamed, not deleted");
            var entries = await shared.LoadAsync();
            Check(entries.Count == 3 && entries.Single(e => e.Key == Sms(1).Key) is { Status: "Accepté par OVH", ProviderId: 77 }, "Statuses and OVH ids kept");
            Check(!await shared.ReserveAsync(Sms(1)) && !await shared.ReserveAsync(Sms(2)), "Migrated sends block duplicates from every workstation");
            var reserved = await shared.LoadReservedKeysAsync();
            Check(Sms(1).ReservationKeys.All(reserved.Contains), "Family reservation keys migrated too");
            Check(await LocalHistoryMigration.MigrateAsync(localPath, shared) == null, "Migration runs only once");

            // Échec côté serveur : le fichier local reste en place pour un nouvel essai.
            var retryPath = Path.Combine(directory, "retry.db");
            Check(await new MessageHistory(retryPath).ReserveAsync(Sms(4)), "Retry source prepared");
            try { await LocalHistoryMigration.MigrateAsync(retryPath, new FailingHistory()); Check(false, "Failure must surface"); }
            catch (InvalidOperationException) { Check(File.Exists(retryPath), "Local history kept when the server is unreachable"); }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
        return count;
    }

    private sealed class FailingHistory : IMessageHistory
    {
        static Exception Down() => new InvalidOperationException("Serveur EyeChat non connecté");
        public Task<bool> ReserveAsync(PreparedMessage message) => throw Down();
        public Task UpdateAsync(string key, string status, long? providerId = null) => throw Down();
        public Task<HashSet<string>> LoadReservedKeysAsync() => throw Down();
        public Task<int> ApplyReportAsync(MailDeliveryReport report, string smtpHost) => throw Down();
        public Task<DateTimeOffset?> EarliestMailAsync(string smtpHost) => throw Down();
        public Task<int> ImportAsync(IReadOnlyList<EyeChat.SmsMail.HistoryRecord> records, IReadOnlyList<EyeChat.SmsMail.HistoryReservationKey> reservations) => throw Down();
        public Task<List<MessageHistoryEntry>> LoadAsync() => throw Down();
    }
}
