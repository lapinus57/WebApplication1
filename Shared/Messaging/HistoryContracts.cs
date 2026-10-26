using System.Collections.Generic;
using System.Threading.Tasks;

namespace EyeChat.Messaging;

public sealed class HistoryRecord
{
    public string Key { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public long? ProviderId { get; set; }
    public string ProviderService { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Subject { get; set; } = "";
    public string TraceJson { get; set; } = "";
    public long Version { get; set; }
    public string Workstation { get; set; } = "";
}

public sealed class HistoryTransfer
{
    public HistoryRecord Entry { get; set; } = new();
    public List<string> ReservationKeys { get; set; } = new();
}

public sealed class HistoryUpdate
{
    public string OperationId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Status { get; set; } = "";
    public long? ProviderId { get; set; }
    public long ExpectedVersion { get; set; }
}

public sealed class HistoryPage
{
    public List<HistoryRecord> Entries { get; set; } = new();
    public long NextCursor { get; set; }
}

public interface ISharedMessageHistoryApi
{
    Task<string> HistoryServerIdAsync();
    Task ImportHistoryAsync(HistoryTransfer entry);
    Task<bool> ReserveHistoryAsync(HistoryTransfer entry);
    Task<List<string>> FindReservedHistoryAsync(List<string> keys);
    Task<HistoryPage> LoadHistoryPageAsync(long cursor);
    Task<HistoryRecord?> GetHistoryMessageAsync(string key);
    // false means a newer server status won the optimistic concurrency check.
    Task<bool> UpdateHistoryAsync(HistoryUpdate update);
}
