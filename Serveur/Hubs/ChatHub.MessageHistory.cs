using ChatServeur.Messaging;
using EyeChat.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace ChatServeur;

public partial class ChatHub
{
    private SharedMessageHistoryStore History()
    {
        if (!ConnectedUsers.ContainsKey(Context.ConnectionId))
            throw new HubException("Connectez le poste au serveur avant de consulter l’historique partagé.");
        return _messageHistory;
    }

    private string HistoryWorkstation() => ConnectedUsers.TryGetValue(Context.ConnectionId, out var user) ? user.MachineName ?? "" : "";

    private async Task HistoryChangedAsync()
    {
        // Only a refresh signal is broadcast; message contents require a registered request.
        // A failed notification must not turn a committed reservation into an apparent failure.
        try { await Clients.Group("A Tous").SendAsync("MessageHistoryChanged"); } catch { }
    }

    public Task<string> GetMessageHistoryServerId() => History().ServerIdAsync();
    public Task<HistoryPage> GetMessageHistoryPage(long cursor) => History().LoadPageAsync(cursor);
    public Task<HistoryRecord?> GetMessageHistoryEntry(string key) => History().GetAsync(key);
    public Task<List<string>> FindReservedMessages(List<string> keys) => History().FindReservedAsync(keys);
    public async Task<bool> ReserveMessageHistory(HistoryTransfer entry)
    {
        var reserved = await History().ReserveAsync(entry, HistoryWorkstation());
        if (reserved) await HistoryChangedAsync();
        return reserved;
    }
    public async Task ImportMessageHistory(HistoryTransfer entry)
    {
        if (await History().ReserveAsync(entry, HistoryWorkstation(), import: true)) await HistoryChangedAsync();
    }
    public async Task<bool> UpdateMessageHistory(HistoryUpdate update)
    {
        var applied = await History().UpdateAsync(update);
        if (applied) await HistoryChangedAsync();
        return applied;
    }
}
