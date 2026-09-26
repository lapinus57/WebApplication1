using System.Diagnostics;
using ChatServeur;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

public class IndexModel : PageModel
{
    private readonly ChatDbContext _db;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ChatDbContext db, ILogger<IndexModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public ServerSnapshot Snapshot { get; private set; } = ServerSnapshot.Unavailable;

    public async Task OnGetAsync()
    {
        Snapshot = await CreateSnapshotAsync();
    }

    public async Task<JsonResult> OnGetSnapshotAsync()
    {
        return new JsonResult(await CreateSnapshotAsync());
    }

    private async Task<ServerSnapshot> CreateSnapshotAsync()
    {
        var process = Process.GetCurrentProcess();
        var startedAt = new DateTimeOffset(process.StartTime);

        try
        {
            var databaseAvailable = await _db.Database.CanConnectAsync();
            if (!databaseAvailable)
            {
                return CreateUnavailableSnapshot(process, startedAt);
            }

            var users = await _db.KnownUsers
                .AsNoTracking()
                .Where(user => user.IsOnline)
                .OrderBy(user => user.DisplayName)
                .Select(user => new ConnectedUserSnapshot(
                    string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                    user.Room,
                    user.Note))
                .ToListAsync();

            return new ServerSnapshot(
                true,
                "Opérationnel",
                startedAt,
                DateTimeOffset.Now,
                process.WorkingSet64,
                users.Count,
                await _db.KnownUsers.CountAsync(),
                await _db.Messages.CountAsync(),
                await _db.Patients.CountAsync(patient => !patient.IsArchived),
                users);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Impossible de récupérer l'état du serveur pour le tableau de bord.");
            return CreateUnavailableSnapshot(process, startedAt);
        }
    }

    private static ServerSnapshot CreateUnavailableSnapshot(Process process, DateTimeOffset startedAt)
    {
        return new ServerSnapshot(
            false,
            "Base de données indisponible",
            startedAt,
            DateTimeOffset.Now,
            process.WorkingSet64,
            0,
            0,
            0,
            0,
            []);
    }

    public sealed record ConnectedUserSnapshot(string Name, string Room, string Note);

    public sealed record ServerSnapshot(
        bool IsHealthy,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset CheckedAt,
        long MemoryBytes,
        int ConnectedUsers,
        int KnownUsers,
        int Messages,
        int ActivePatients,
        IReadOnlyList<ConnectedUserSnapshot> Users)
    {
        public static ServerSnapshot Unavailable { get; } = new(
            false,
            "Chargement…",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            0,
            0,
            0,
            0,
            0,
            []);
    }
}
