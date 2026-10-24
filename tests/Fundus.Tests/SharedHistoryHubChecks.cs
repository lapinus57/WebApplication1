using System.Security.Claims;
using ChatServeur;
using ChatServeur.Messaging;
using EyeChat.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

internal static class SharedHistoryHubChecks
{
    // Exercise the real ChatHub registration/access checks, without a Windows client or live provider.
    public static async Task RunAsync(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "eye-history-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var dbPath = Path.Combine(folder, "chat.db");
            await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseSqlite("Data Source=" + dbPath).Options);
            await db.Database.EnsureCreatedAsync();
            var store = new SharedMessageHistoryStore(dbPath);
            var clients = new FakeClients(); var groups = new Groups();
            ChatHub Hub(string id) => new(db, new Environment(folder), NullLogger<ChatHub>.Instance, store)
                { Context = new Caller(id), Clients = clients, Groups = groups };
            var a = Hub("history-test-a"); var b = Hub("history-test-b");
            var transfer = new HistoryTransfer { Entry = new HistoryRecord { Key = "hub-message", Channel = "SMS", Contact = "+33600000000", Body = "Message TEST", Workstation = "Untrusted name" }, ReservationKeys = new() { "hub-family" } };
            foreach (var action in new Func<Task>[]
            {
                async () => { await a.GetMessageHistoryServerId(); }, async () => { await a.GetMessageHistoryPage(0); },
                async () => { await a.GetMessageHistoryEntry("hub-message"); }, async () => { await a.FindReservedMessages(new() { "hub-family" }); },
                async () => { await a.ReserveMessageHistory(transfer); }, async () => { await a.ImportMessageHistory(transfer); },
                async () => { await a.UpdateMessageHistory(new()); }
            })
            {
                try { await action(); check(false, "Unregistered history request must be denied"); }
                catch (HubException) { check(true, "Unregistered history request denied"); }
            }
            await a.RegisterUser("HistoryTestA", "", "TEST", "Black", "POSTE A");
            await b.RegisterUser("HistoryTestB", "", "TEST", "Black", "POSTE B");
            check(groups.Members.Contains(("history-test-a", "A Tous")) && groups.Members.Contains(("history-test-b", "A Tous")), "Registered workstations join shared notification group");
            check(await a.ReserveMessageHistory(transfer), "Registered workstation can reserve a message");
            var read = await Hub("history-test-b").GetMessageHistoryEntry("hub-message");
            check(read?.Body == "Message TEST" && read.Workstation == "POSTE A", "Other registered workstation reads history with server-sourced origin");
            transfer.Entry.Key = "hub-other-phone";
            check(!await b.ReserveMessageHistory(transfer), "Hub prevents cross-workstation duplicate family send");
            check(await b.UpdateMessageHistory(new HistoryUpdate { OperationId = Guid.NewGuid().ToString("N"), Key = read!.Key, Status = "Reçu", ExpectedVersion = read.Version }), "Other workstation can update shared delivery status");
            check((await a.GetMessageHistoryPage(0)).Entries.Single().Status == "Reçu", "Hub list includes shared status update");
            check(clients.HistoryEvents.Count > 0 && clients.HistoryEvents.All(e => e.Group == "A Tous" && e.Arguments == 0), "History broadcasts contain only a refresh signal to registered group");
            clients.FailNotification = true;
            transfer.Entry.Key = "hub-notify-failure"; transfer.ReservationKeys.Clear();
            check(await a.ReserveMessageHistory(transfer), "Notification failure does not invalidate committed reservation");
            check(!await b.ReserveMessageHistory(transfer), "Committed reservation remains effective despite notification failure");
            await a.OnDisconnectedAsync(null); await b.OnDisconnectedAsync(null);
            try { await Hub("history-test-a").GetMessageHistoryPage(0); check(false, "Disconnected workstation must be denied"); }
            catch (HubException) { check(true, "Disconnected workstation loses shared history access"); }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }

    private sealed class Caller(string id) : HubCallerContext
    {
        public override string ConnectionId => id;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => new();
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
    private sealed class Groups : IGroupManager
    {
        public HashSet<(string, string)> Members { get; } = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Members.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Members.Remove((connectionId, groupName)); return Task.CompletedTask; }
    }
    private sealed class FakeClients : IHubCallerClients
    {
        public bool FailNotification;
        public List<(string Group, int Arguments)> HistoryEvents { get; } = new();
        private sealed class Proxy(FakeClients owner, string group) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (method == "MessageHistoryChanged")
                {
                    if (owner.FailNotification) throw new IOException("Simulated notification loss");
                    owner.HistoryEvents.Add((group, args.Length));
                }
                return Task.CompletedTask;
            }
        }
        public IClientProxy All => new Proxy(this, "all");
        public IClientProxy Caller => new Proxy(this, "caller");
        public IClientProxy Others => new Proxy(this, "others");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Client(string connectionId) => Caller;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Caller;
        public IClientProxy Group(string groupName) => new Proxy(this, groupName);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
        public IClientProxy OthersInGroup(string groupName) => Group(groupName);
        public IClientProxy User(string userId) => Caller;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Caller;
    }
    private sealed class Environment(string folder) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = folder;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = folder;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
