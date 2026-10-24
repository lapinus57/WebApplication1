using Client.SmsMail.Services;
using ChatServeur.Messaging;
using EyeChat.Messaging;
using Microsoft.Data.Sqlite;

internal static class SharedHistoryChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
        var folder = Path.Combine(Path.GetTempPath(), "eye-shared-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var serverPath = Path.Combine(folder, "chat.db");
            var store = new SharedMessageHistoryStore(serverPath);
            var apiA = new Api(store, "POSTE A"); var apiB = new Api(new SharedMessageHistoryStore(serverPath), "POSTE B");
            var pathA = Path.Combine(folder, "a.db"); var pathB = Path.Combine(folder, "b.db");
            var a = new SharedMessageHistory(pathA, apiA); var b = new SharedMessageHistory(pathB, apiB);
            PreparedMessage Message(string key, string family = "") => new() { Key = key, Channel = "SMS", Contact = "+33600000000", Body = "Rappel TEST", ProviderService = "test-service", ReservationKeys = family.Length == 0 ? new() : new() { family } };

            var old = new MessageHistory(pathA);
            await old.ReserveAsync(Message("legacy", "old-family")); await old.UpdateAsync("legacy", "Reçu", 123);
            await using (var fixture = new SqliteConnection("Data Source=" + pathA))
            {
                await fixture.OpenAsync(); using var command = fixture.CreateCommand();
                command.CommandText = "UPDATE MessageHistory SET CreatedAt='2021-01-15T08:30:00+01:00' WHERE MessageKey='legacy'";
                await command.ExecuteNonQueryAsync();
            }
            apiA.LoseImportAcknowledgement = true;
            try { await a.SynchronizeAsync(); Check(false, "Interrupted archive import must be retried"); } catch (IOException) { count++; }
            a = new SharedMessageHistory(pathA, apiA);
            Check((await a.LoadAsync()).Single().Status == "Reçu", "Local history is migrated with delivery status");
            Check(DateTimeOffset.Parse((await store.GetAsync("legacy"))!.CreatedAt) == DateTimeOffset.Parse("2021-01-15T07:30:00Z"), "Migration preserves original instant including timezone offset");
            Check((await b.LoadAsync()).Single().ProviderId == 123, "Another workstation sees the migrated provider id");
            Check(await b.ReadBodyAsync("legacy") == "Rappel TEST", "Other workstation can read message body on demand");
            Check((await b.LoadAsync()).Single().Workstation == "POSTE A", "Origin workstation is shared");
            Check(!(await b.ReserveAsync(Message("new-phone", "old-family"))), "Migrated family guards block sends from another workstation");
            await a.SynchronizeAsync(); await b.SynchronizeAsync();
            Check((await a.LoadAsync()).Count == 1, "Repeated migration is idempotent");
            Check((await old.LoadAsync()).Count == 1, "Local archive is retained");

            var winners = await Task.WhenAll(Task.Run(() => a.ReserveAsync(Message("race-a", "same-family"))), Task.Run(() => b.ReserveAsync(Message("race-b", "same-family"))));
            Check(winners.Count(x => x) == 1, "Concurrent workstations reserve a family only once");
            var winner = winners[0] ? a : b; var winningKey = winners[0] ? "race-a" : "race-b";
            await winner.UpdateAsync(winningKey, "Accepté par OVH", 456);
            Check((await a.LoadAsync()).Single(e => e.Key == winningKey).Status == "Accepté par OVH", "Updated status is visible on A");
            Check((await b.LoadAsync()).Single(e => e.Key == winningKey).ProviderId == 456, "Updated status is visible on B");
            Check((await b.LoadReservedKeysAsync(new[] { "same-family", winningKey, "unused" })).SetEquals(new[] { "same-family", winningKey }), "Reservation lookup includes family and message, not unused key");
            Check(await a.ReserveAsync(Message("after-rollback", winners[0] ? "race-b" : "race-a")), "Failed reservation leaves no partial guards");

            apiA.Offline = true;
            try { await a.ReserveAsync(Message("offline")); Check(false, "Offline send must fail closed"); } catch (IOException) { count++; }
            apiA.Offline = false;
            Check(await store.GetAsync("offline") == null, "Offline failure did not create or authorize a send");

            Check(await a.ReserveAsync(Message("lost-link")), "Reserve before simulated provider acceptance");
            apiA.Offline = true;
            try { await a.UpdateAsync("lost-link", "Accepté par OVH", 789); Check(false, "Disconnected update must report pending sync"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("conservé"), "Disconnected result explains durable local storage"); }
            Check((await store.GetAsync("lost-link"))?.Status.Contains("vérifier") == true, "Server keeps uncertain reservation during outage");
            apiA.Offline = false;
            a = new SharedMessageHistory(pathA, apiA);
            await a.SynchronizeAsync();
            Check((await b.LoadAsync()).Single(e => e.Key == "lost-link").ProviderId == 789, "Restart retries status without resending the message");
            Check(!await b.ReserveAsync(Message("lost-link")), "Recovered send remains reserved across all workstations");

            Check(await a.ReserveAsync(Message("lost-ack")), "Lost acknowledgement fixture reserved");
            apiA.LoseUpdateAcknowledgement = true;
            try { await a.UpdateAsync("lost-ack", "Accepté", 900); Check(false, "Lost acknowledgement must be queued"); } catch (InvalidOperationException) { count++; }
            var committedVersion = (await store.GetAsync("lost-ack"))!.Version;
            await new SharedMessageHistory(pathA, apiA).SynchronizeAsync();
            Check((await store.GetAsync("lost-ack"))!.Version == committedVersion, "Status retry is idempotent after lost acknowledgement");

            await a.LoadAsync(); await b.LoadAsync();
            await b.UpdateAsync("lost-link", "Reçu — confirmation OVH");
            await a.UpdateAsync("lost-link", "Ancien statut en attente");
            Check((await store.GetAsync("lost-link"))!.Status.StartsWith("Reçu"), "Stale workstation cannot overwrite a newer delivery status");
            Check(a.Warning.Contains("plus récent"), "Concurrency conflict is visible to the user");
            var archived = new HistoryTransfer { Entry = new HistoryRecord { Key = "lost-link", Channel = "SMS", Contact = "+33600000000", Body = "Ancienne copie", Status = "Ancien statut", CreatedAt = DateTimeOffset.UtcNow.AddDays(-5).ToString("O") } };
            await apiA.ImportHistoryAsync(archived);
            Check((await store.GetAsync("lost-link"))!.Status.StartsWith("Reçu"), "Re-import never overwrites current delivery status");

            var identity = await store.ServerIdAsync();
            Check(await new SharedMessageHistoryStore(serverPath).ServerIdAsync() == identity, "Server identity survives a restart");
            Check((await new SharedMessageHistoryStore(serverPath).GetAsync("legacy"))?.ProviderId == 123, "Shared data survives a server restart");

            var mail = Message("mail"); mail.Channel = "Mail"; mail.Contact = "test@example.com"; mail.ProviderService = "smtp.example.com"; mail.Body = "<p>Bonjour TEST</p>";
            Check(await a.ReserveAsync(mail), "Mail reservation is also shared"); await a.UpdateAsync("mail", "Accepté SMTP");
            Check(await b.EarliestMailAsync("smtp.example.com") != null, "Another workstation can find the mailbox history start");
            var report = new MailDeliveryReport("mail", "test@example.com", "Échec de livraison");
            Check(await b.ApplyReportAsync(report, "other.example.com") == 0, "Shared DSN cannot update another SMTP account");
            Check(await b.ApplyReportAsync(report with { Recipient = "wrong@example.com" }, "smtp.example.com") == 0, "Shared DSN cannot update another recipient");
            Check(await b.ApplyReportAsync(report, "smtp.example.com") == 1, "Mail delivery report can be applied from another workstation");
            Check((await a.LoadAsync()).Single(e => e.Key == "mail").Status == report.Status, "Mail delivery status is shared back to sender workstation");
            Check(await b.ReadBodyAsync("mail") == mail.Body, "HTML message is retained exactly for readable preview");

            for (int i = 0; i < 105; i++) await apiA.ImportHistoryAsync(new HistoryTransfer { Entry = new HistoryRecord { Key = "archive-" + i, Channel = "Mail", Contact = "test@example.com", Body = "Archive TEST", Status = "Archive", CreatedAt = DateTimeOffset.UtcNow.AddYears(-1).AddMinutes(i).ToString("O") } });
            var all = await b.LoadAsync();
            Check(all.Count(e => e.Key.StartsWith("archive-")) == 105, "Pagination exposes all older records without truncation");
            Check((await store.LoadPageAsync(0)).Entries.All(e => e.Body == ""), "List pages omit potentially large message bodies");
            Check(await b.ReadBodyAsync("archive-104") == "Archive TEST", "Paged record body remains accessible");
            var many = Enumerable.Range(0, 1200).Select(i => "candidate-" + i).Append("old-family");
            Check((await a.LoadReservedKeysAsync(many)).SetEquals(new[] { "old-family" }), "Large preparation batches use bounded reservation queries");

            var newPath = Path.Combine(folder, "new-server.db"); var otherApi = new Api(new SharedMessageHistoryStore(newPath), "POSTE A");
            await otherApi.ImportHistoryAsync(new HistoryTransfer { Entry = new HistoryRecord { Key = "mail", Channel = "Mail", Contact = "test@example.com", Status = "Autre serveur", CreatedAt = DateTimeOffset.UtcNow.ToString("O") } });
            var originalVersion = (await store.GetAsync("mail"))!.Version;
            for (long version = 1; version < originalVersion; version++)
                await otherApi.UpdateHistoryAsync(new HistoryUpdate { OperationId = Guid.NewGuid().ToString("N"), Key = "mail", Status = "Autre serveur", ExpectedVersion = version });
            apiA.Offline = true;
            try { await a.UpdateAsync("mail", "Pending only on original server"); } catch (InvalidOperationException) { }
            await new SharedMessageHistory(pathA, otherApi).SynchronizeAsync();
            Check((await otherApi.GetHistoryMessageAsync("mail"))?.Status == "Autre serveur", "Outbox is not replayed to another server identity");
            apiA.Offline = false;
            await a.SynchronizeAsync();

            try { await store.ReserveAsync(new HistoryTransfer { Entry = new HistoryRecord { Key = "bad", Channel = "Other", Contact = "test" } }, "TEST"); Check(false, "Unknown channel rejected"); } catch (ArgumentException) { count++; }
            try { await store.FindReservedAsync(Enumerable.Repeat("key", 501).ToList()); Check(false, "Oversized lookup rejected"); } catch (ArgumentException) { count++; }
            Check(await store.GetAsync("bad") == null, "Invalid input leaves no shared history entry");

            var row = (await store.GetAsync("legacy"))!;
            var update = new HistoryUpdate { OperationId = Guid.NewGuid().ToString("N"), Key = row.Key, ExpectedVersion = row.Version, Status = "Reçu confirmé" };
            Check(await store.UpdateAsync(update) && await store.UpdateAsync(update), "Direct operation retry returns its original result");
            update.Status = "Altered payload";
            try { await store.UpdateAsync(update); Check(false, "Operation identifier cannot be reused with another payload"); } catch (ArgumentException) { count++; }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
        return count;
    }

    private sealed class Api(SharedMessageHistoryStore store, string workstation) : ISharedMessageHistoryApi
    {
        public bool Offline;
        public bool LoseUpdateAcknowledgement;
        public bool LoseImportAcknowledgement;
        private void Connected() { if (Offline) throw new IOException("Simulated disconnection"); }
        public Task<string> HistoryServerIdAsync() { Connected(); return store.ServerIdAsync(); }
        public async Task ImportHistoryAsync(HistoryTransfer entry)
        {
            Connected(); await store.ReserveAsync(entry, workstation, true);
            if (LoseImportAcknowledgement) { LoseImportAcknowledgement = false; throw new IOException("Simulated lost import acknowledgement"); }
        }
        public Task<bool> ReserveHistoryAsync(HistoryTransfer entry) { Connected(); return store.ReserveAsync(entry, workstation); }
        public Task<List<string>> FindReservedHistoryAsync(List<string> keys) { Connected(); return store.FindReservedAsync(keys); }
        public Task<HistoryPage> LoadHistoryPageAsync(long cursor) { Connected(); return store.LoadPageAsync(cursor); }
        public Task<HistoryRecord?> GetHistoryMessageAsync(string key) { Connected(); return store.GetAsync(key); }
        public async Task<bool> UpdateHistoryAsync(HistoryUpdate update)
        {
            Connected(); var applied = await store.UpdateAsync(update);
            if (LoseUpdateAcknowledgement) { LoseUpdateAcknowledgement = false; throw new IOException("Simulated lost acknowledgement"); }
            return applied;
        }
    }
}
