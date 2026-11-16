using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

// Envoi en arrière-plan : progression, arrêt entre deux messages, doublons et erreurs. Aucun vrai envoi.
static class SendingChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var day = new DateTime(2026, 10, 23);
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        List<PreparedMessage> Messages() => Enumerable.Range(0, 5).SelectMany(i => MessagePreparation.Prepare(new[]
        {
            new RdvInfo { Id = 900 + i, ReferenceFamilyNumber = 900 + i, Prenom = "P" + i, Nom = "Envoi" + i, DateRDV = day, HeureRDV = day.AddHours(9), Tel1 = $"06123456{i:D2}" }
        }, day, settings)).Where(m => m.Channel == "SMS").ToList();
        var directory = Path.Combine(Path.GetTempPath(), "sending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var history = new MessageHistory(Path.Combine(directory, "history.db"));

            // 1. Tout part, la progression est signalée avant chaque message, l'historique est à jour.
            var messages = Messages();
            Check(messages.Count == 5, "Five distinct SMS prepared");
            var reported = new List<int>();
            var sent = new List<string>();
            var outcome = await new MessageSender(history, m => { sent.Add(m.Contact); return Task.FromResult(new SendResult("Accepté", 42, true)); })
                .SendAsync(messages, new SyncProgress(p => reported.Add(p.Done)), CancellationToken.None);
            Check(outcome is { Accepted: 5, Skipped: 0, Uncertain: 0, Stopped: false } && sent.Count == 5, "All messages sent once");
            Check(reported.SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "Progress reported before each message");
            Check(messages.All(m => !m.CanSend && !m.Selected && m.Status == "Accepté"), "Sent messages can no longer be selected");
            Check((await history.LoadAsync()).Count(e => e.Status == "Accepté" && e.ProviderId == 42) == 5, "History records status and OVH id");

            // 2. Renvoyer les mêmes messages : tous bloqués par l'historique, rien ne part.
            sent.Clear();
            outcome = await new MessageSender(history, m => { sent.Add(m.Contact); return Task.FromResult(new SendResult("Accepté", null, true)); })
                .SendAsync(Messages(), null, CancellationToken.None);
            Check(outcome.Skipped == 5 && sent.Count == 0, "Second batch blocked as already handled");

            // 3. Arrêt demandé pendant le 2e message : il se termine, le 3e ne part pas.
            history = new MessageHistory(Path.Combine(directory, "stop.db"));
            messages = Messages();
            using var stop = new CancellationTokenSource();
            sent.Clear();
            outcome = await new MessageSender(history, async m =>
            {
                sent.Add(m.Contact);
                if (sent.Count == 2) stop.Cancel();
                await Task.Delay(10);
                return new SendResult("Accepté", null, true);
            }).SendAsync(messages, null, stop.Token);
            Check(outcome is { Accepted: 2, NotStarted: 3, Stopped: true } && sent.Count == 2, "Stop takes effect after the message in progress");
            Check(messages.Skip(2).All(m => m.CanSend && m.Selected && m.Status == "À envoyer"), "Messages not started stay selectable");
            Check((await history.LoadReservedKeysAsync()).Count(k => messages.Skip(2).Any(m => m.Key == k)) == 0, "Messages not started are not reserved");

            // 4. Erreur réseau : statut « à vérifier », jamais de nouvel essai automatique.
            history = new MessageHistory(Path.Combine(directory, "error.db"));
            messages = Messages().Take(2).ToList();
            int calls = 0;
            outcome = await new MessageSender(history, _ => { calls++; throw new HttpRequestException("coupure"); })
                .SendAsync(messages, null, CancellationToken.None);
            Check(outcome.Uncertain == 2 && calls == 2 && messages.All(m => m.Status == MessageSender.UncertainStatus && !m.CanSend), "Network error marked as to verify, not retried");
            Check((await history.LoadAsync()).All(e => e.Status == MessageSender.UncertainStatus), "Uncertain status stored in history");

        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
        return count;
    }

    private sealed class SyncProgress(Action<SendProgress> report) : IProgress<SendProgress>
    {
        public void Report(SendProgress value) => report(value);
    }
}
