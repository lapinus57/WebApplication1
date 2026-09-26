using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
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
    public IReadOnlyList<ManagedUser> ManagedUsers { get; private set; } = [];

    [BindProperty]
    public UserEditInput UserEdit { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        Snapshot = await CreateSnapshotAsync();
        ManagedUsers = await GetManagedUsersAsync();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync()
    {
        if (!ModelState.IsValid)
        {
            Snapshot = await CreateSnapshotAsync();
            ManagedUsers = await GetManagedUsersAsync();
            return Page();
        }

        var user = await _db.KnownUsers.FindAsync(UserEdit.Id);
        if (user is null)
            return NotFound();

        user.DisplayName = UserEdit.DisplayName.Trim();
        user.Room = UserEdit.Room.Trim();
        user.Note = UserEdit.Note.Trim();
        await _db.SaveChangesAsync();
        SuccessMessage = $"L’utilisateur {user.DisplayName} a bien été modifié.";
        return Redirect(Url.Page("/Index") + "#users");
    }

    private async Task<IReadOnlyList<ManagedUser>> GetManagedUsersAsync() => await _db.KnownUsers
        .AsNoTracking()
        .OrderByDescending(user => user.IsOnline)
        .ThenBy(user => user.DisplayName)
        .Select(user => new ManagedUser(user.Id, user.Username, user.DisplayName, user.Room, user.Note, user.IsOnline))
        .ToListAsync();

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

            var today = DateTime.Today;
            var weekStart = today.AddDays(-6);
            var patientCounts = await _db.Patients.AsNoTracking()
                .Where(patient => patient.HoldTime >= weekStart)
                .GroupBy(patient => patient.HoldTime.Date)
                .Select(group => new { Date = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Date, item => item.Count);
            var weeklyPatients = Enumerable.Range(0, 7)
                .Select(offset => weekStart.AddDays(offset))
                .Select(date => new DailyPatientSnapshot(date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), patientCounts.GetValueOrDefault(date)))
                .ToList();

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
                await _db.Patients.CountAsync(),
                await _db.Patients.CountAsync(patient => patient.HoldTime >= today),
                await _db.Patients.CountAsync(patient => patient.IsArchived),
                await _db.Patients.CountAsync(patient => patient.PickUpTime != null),
                weeklyPatients,
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
            0,
            0,
            0,
            0,
            [],
            []);
    }

    public sealed record ConnectedUserSnapshot(string Name, string Room, string Note);
    public sealed record ManagedUser(int Id, string Username, string DisplayName, string Room, string Note, bool IsOnline);
    public sealed record DailyPatientSnapshot(string Label, int Count);

    public sealed class UserEditInput
    {
        [Range(1, int.MaxValue)] public int Id { get; set; }
        [Required(ErrorMessage = "Le nom affiché est obligatoire.")]
        [StringLength(80)] public string DisplayName { get; set; } = string.Empty;
        [StringLength(80)] public string Room { get; set; } = string.Empty;
        [StringLength(200)] public string Note { get; set; } = string.Empty;
    }

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
        int TotalPatients,
        int PatientsToday,
        int ArchivedPatients,
        int CompletedPatients,
        IReadOnlyList<DailyPatientSnapshot> WeeklyPatients,
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
            0,
            0,
            0,
            0,
            [],
            []);
    }
}
