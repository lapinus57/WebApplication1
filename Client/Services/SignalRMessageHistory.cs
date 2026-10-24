using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EyeChat.Messaging;
using Microsoft.AspNetCore.SignalR.Client;

namespace Client.Services;

public partial class SignalRService : ISharedMessageHistoryApi
{
    public event Action? MessageHistoryChanged;
    public Task<string> HistoryServerIdAsync() => GetRequiredConnection().InvokeAsync<string>("GetMessageHistoryServerId");
    public Task ImportHistoryAsync(HistoryTransfer entry) => GetRequiredConnection().InvokeAsync("ImportMessageHistory", entry);
    public Task<bool> ReserveHistoryAsync(HistoryTransfer entry) => GetRequiredConnection().InvokeAsync<bool>("ReserveMessageHistory", entry);
    public Task<List<string>> FindReservedHistoryAsync(List<string> keys) => GetRequiredConnection().InvokeAsync<List<string>>("FindReservedMessages", keys);
    public Task<HistoryPage> LoadHistoryPageAsync(long cursor) => GetRequiredConnection().InvokeAsync<HistoryPage>("GetMessageHistoryPage", cursor);
    public Task<HistoryRecord?> GetHistoryMessageAsync(string key) => GetRequiredConnection().InvokeAsync<HistoryRecord?>("GetMessageHistoryEntry", key);
    public Task<bool> UpdateHistoryAsync(HistoryUpdate update) => GetRequiredConnection().InvokeAsync<bool>("UpdateMessageHistory", update);
}
