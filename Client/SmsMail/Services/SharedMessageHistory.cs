using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EyeChat.Messaging;

namespace Client.SmsMail.Services;

public sealed class SharedMessageHistory
{
    private readonly MessageHistory local;
    private readonly LocalHistorySync sync;
    private readonly ISharedMessageHistoryApi api;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, long> versions = new(StringComparer.Ordinal);
    private string serverId = "";
    public string Warning { get; private set; } = "";

    public SharedMessageHistory(string localPath, ISharedMessageHistoryApi api)
    { local = new(localPath); sync = new(localPath); this.api = api; }

    public async Task SynchronizeAsync()
    {
        await gate.WaitAsync();
        try
        {
            var current = await api.HistoryServerIdAsync();
            if (string.IsNullOrWhiteSpace(current)) throw new InvalidOperationException("Historique partagé indisponible : mettez à jour le serveur.");
            if (serverId != current) versions.Clear();
            serverId = current;
            await local.InitializeAsync();
            while (true)
            {
                var entries = await sync.UnimportedAsync(serverId);
                if (entries.Count == 0) break;
                foreach (var entry in entries)
                {
                    await api.ImportHistoryAsync(entry);
                    await sync.MarkImportedAsync(serverId, entry.Entry.Key);
                }
            }
            await FlushAsync();
        }
        finally { gate.Release(); }
    }

    private async Task FlushAsync()
    {
        foreach (var update in await sync.PendingAsync(serverId))
        {
            bool applied = await api.UpdateHistoryAsync(update);
            if (applied) versions[update.Key] = update.ExpectedVersion + 1;
            else Warning = "Un statut plus récent a été conservé sur le serveur. Vérifiez le message concerné dans l’historique.";
            await sync.AcknowledgeAsync(update.OperationId);
        }
    }

    public async Task<bool> ReserveAsync(PreparedMessage message)
    {
        await SynchronizeAsync();
        var transfer = new HistoryTransfer
        {
            Entry = new HistoryRecord { Key = message.Key, Channel = message.Channel, Contact = message.Contact, Body = message.Body, ProviderService = message.ProviderService,
                Reference = message.Reference, Subject = message.Subject, TraceJson = message.TraceJson },
            ReservationKeys = message.ReservationKeys.ToList()
        };
        // The server is the only authority. No offline/local fallback can authorize a send.
        if (!await api.ReserveHistoryAsync(transfer)) return false;
        versions[message.Key] = 1;
        // The mail cannot be dispatched unless its reference and frozen reasons
        // can actually be retrieved from this server (also detects old servers).
        if (message.Reference.Length > 0)
        {
            var saved = await api.GetHistoryMessageAsync(message.Key);
            if (saved?.Reference != message.Reference || saved.TraceJson != message.TraceJson || saved.Body != message.Body || saved.Subject != message.Subject)
                throw new InvalidOperationException("La traçabilité du mail n’a pas pu être vérifiée sur le serveur. Aucun mail envoyé. Mettez à jour le serveur et vérifiez l’historique ; la réservation est conservée.");
        }
        await local.ReserveAsync(message);
        await sync.MarkImportedAsync(serverId, message.Key);
        return true;
    }

    public async Task UpdateAsync(string key, string status, long? providerId = null)
    {
        if (serverId.Length == 0 || !versions.TryGetValue(key, out var version))
            throw new InvalidOperationException("Actualisez l’historique partagé avant de modifier un statut.");
        var update = new HistoryUpdate { OperationId = Guid.NewGuid().ToString("N"), Key = key, Status = status, ProviderId = providerId, ExpectedVersion = version };
        // Persist the provider result before any network call, even if the connection just dropped.
        await sync.EnqueueAsync(serverId, update);
        await local.UpdateAsync(key, status, providerId);
        try
        {
            if (await api.HistoryServerIdAsync() != serverId) throw new InvalidOperationException();
            await FlushAsync();
        }
        catch
        {
            throw new InvalidOperationException("Le résultat est conservé sur ce poste et sera synchronisé à la reconnexion. Ne renvoyez pas le message : sa réservation est déjà enregistrée sur le serveur.");
        }
    }

    public async Task<HashSet<string>> LoadReservedKeysAsync(IEnumerable<string> candidates)
    {
        await SynchronizeAsync(); var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in candidates.Distinct(StringComparer.Ordinal).Chunk(500))
            found.UnionWith(await api.FindReservedHistoryAsync(batch.ToList()));
        return found;
    }

    private async Task<List<HistoryRecord>> LoadRecordsAsync()
    {
        await SynchronizeAsync(); var records = new List<HistoryRecord>(); long cursor = 0;
        do
        {
            var page = await api.LoadHistoryPageAsync(cursor);
            records.AddRange(page.Entries);
            if (page.NextCursor != 0 && page.NextCursor <= cursor) throw new InvalidOperationException("Page d’historique partagée invalide.");
            cursor = page.NextCursor;
        } while (cursor != 0);
        foreach (var entry in records) versions[entry.Key] = entry.Version;
        return records;
    }

    public async Task<List<MessageHistoryEntry>> LoadAsync() => (await LoadRecordsAsync())
        .OrderByDescending(e => DateTimeOffset.Parse(e.CreatedAt, CultureInfo.InvariantCulture)).ThenBy(e => e.Key, StringComparer.Ordinal)
        .Select(e => new MessageHistoryEntry { Key = e.Key, Channel = e.Channel, Contact = e.Contact, Body = e.Body,
            Status = e.Status, CreatedAt = DateTimeOffset.Parse(e.CreatedAt, CultureInfo.InvariantCulture).ToLocalTime().ToString("g"),
            ProviderId = e.ProviderId, ProviderService = e.ProviderService, Workstation = e.Workstation, Reference = e.Reference }).ToList();

    public async Task<HistoryRecord> ReadDetailsAsync(string key) => await api.GetHistoryMessageAsync(key)
        ?? throw new InvalidOperationException("Message introuvable dans l’historique partagé.");

    public async Task<string> ReadBodyAsync(string key) => (await api.GetHistoryMessageAsync(key))?.Body
        ?? throw new InvalidOperationException("Message introuvable dans l’historique partagé.");

    public async Task<DateTimeOffset?> EarliestMailAsync(string host)
    {
        var dates = (await LoadRecordsAsync()).Where(e => e.Channel == "Mail" && e.ProviderService == host)
            .Select(e => DateTimeOffset.Parse(e.CreatedAt, CultureInfo.InvariantCulture)).ToList();
        return dates.Count == 0 ? null : dates.Min();
    }

    public async Task<int> ApplyReportAsync(MailDeliveryReport report, string host)
    {
        await SynchronizeAsync(); var entry = await api.GetHistoryMessageAsync(report.MessageKey);
        if (entry == null || entry.Channel != "Mail" || entry.ProviderService != host || !string.Equals(entry.Contact, report.Recipient, StringComparison.OrdinalIgnoreCase)) return 0;
        versions[entry.Key] = entry.Version;
        await UpdateAsync(entry.Key, report.Status); return 1;
    }

    public async Task<int> ImportEyeEmailAsync(string path)
    {
        var count = await local.ImportEyeEmailAsync(path);
        await SynchronizeAsync(); return count;
    }
}
