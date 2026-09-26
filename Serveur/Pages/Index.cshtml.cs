using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ChatServeur;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

public class IndexModel : PageModel
{
    private readonly ChatDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IHubContext<ChatHub> _hubContext;

    public IndexModel(ChatDbContext db, ILogger<IndexModel> logger, IHubContext<ChatHub> hubContext)
    {
        _db = db;
        _logger = logger;
        _hubContext = hubContext;
    }

    public ServerSnapshot Snapshot { get; private set; } = ServerSnapshot.Unavailable;
    public IReadOnlyList<ManagedUser> ManagedUsers { get; private set; } = [];
    public IReadOnlyList<ExamOption> ExamOptions { get; private set; } = [];
    public IReadOnlyList<string> Rooms { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        Snapshot = await CreateSnapshotAsync();
        ManagedUsers = await GetManagedUsersAsync();
        ExamOptions = await GetExamOptionsAsync();
        Rooms = await GetRoomsAsync();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync(UserEditInput userEdit)
    {
        if (!ModelState.IsValid)
        {
            Snapshot = await CreateSnapshotAsync();
            ManagedUsers = await GetManagedUsersAsync();
            ExamOptions = await GetExamOptionsAsync();
            Rooms = await GetRoomsAsync();
            return Page();
        }

        var user = await _db.KnownUsers.FindAsync(userEdit.Id);
        if (user is null)
            return NotFound();

        user.DisplayName = userEdit.DisplayName.Trim();
        user.Room = userEdit.Room.Trim();
        user.Note = userEdit.Note.Trim();
        user.ColorUserName = userEdit.ColorUserName.Trim();
        await _db.SaveChangesAsync();
        await BroadcastUsersAsync(user);
        SuccessMessage = $"L’utilisateur {user.DisplayName} a bien été modifié.";
        return Redirect(Url.Page("/Index") + "#users");
    }

    private async Task BroadcastUsersAsync(KnownUser? updatedUser = null)
    {
        var authoritativeUsers = updatedUser is null
            ? ChatHub.GetAuthoritativeUsers()
            : ChatHub.ApplyAdministrativeUserUpdate(updatedUser);
        if (authoritativeUsers is not null)
        {
            await _hubContext.Clients.All.SendAsync("UserListUpdated", authoritativeUsers);
            return;
        }

        var users = await _db.KnownUsers.AsNoTracking().ToListAsync();
        var clientUsers = users.Select(item => new UserInfo
        {
            ConnectionId = item.ConnectionId,
            Username = item.Username,
            Avatar = item.Avatar,
            Rooms = string.IsNullOrWhiteSpace(item.Room) ? [] : [item.Room],
            DisplayName = item.DisplayName,
            ColorUserName = item.ColorUserName,
            IsOnline = item.IsOnline,
            Note = item.Note
        }).ToList();
        await _hubContext.Clients.All.SendAsync("UserListUpdated", clientUsers);
    }

    public async Task<IActionResult> OnPostSaveExamAsync(ExamEditInput examEdit)
    {
        if (!ModelState.IsValid)
            return await ReloadPageAsync();

        var options = (await GetExamOptionsAsync()).ToList();
        var exam = options.FirstOrDefault(item => item.Id == examEdit.Id);
        if (exam is null)
            return NotFound();
        if (options.Any(item => item.Id != exam.Id && string.Equals(item.Name, examEdit.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty, "Un examen porte déjà ce nom.");
            return await ReloadPageAsync();
        }

        var previousName = exam.Name;
        ApplyExamEdit(exam, examEdit);
        await SaveExamOptionsAsync(options, previousName, exam.Id);
        SuccessMessage = $"L’examen {exam.Name} a bien été modifié.";
        return Redirect(Url.Page("/Index") + "#exams");
    }

    public async Task<IActionResult> OnPostAddExamAsync()
    {
        var options = (await GetExamOptionsAsync()).ToList();
        var exam = new ExamOption
        {
            Index = options.Count + 1,
            Name = "Nouvel examen",
            Description = "Nouvel examen",
            Color = "#246BFD",
            CodeMSG = "examen"
        };
        options.Add(exam);
        await SaveExamOptionsAsync(options);
        SuccessMessage = "Un nouvel examen a été ajouté. Vous pouvez maintenant le personnaliser.";
        return Redirect(Url.Page("/Index") + "#exams");
    }

    public async Task<IActionResult> OnPostDeleteExamAsync(string id)
    {
        var options = (await GetExamOptionsAsync()).ToList();
        var exam = options.FirstOrDefault(item => item.Id == id);
        if (exam is null)
            return NotFound();

        options.Remove(exam);
        ReindexExams(options);
        await SaveExamOptionsAsync(options);
        SuccessMessage = $"L’examen {exam.Name} a été supprimé.";
        return Redirect(Url.Page("/Index") + "#exams");
    }

    public async Task<IActionResult> OnPostMoveExamAsync(string id, int direction)
    {
        var options = (await GetExamOptionsAsync()).OrderBy(item => item.Index).ToList();
        var index = options.FindIndex(item => item.Id == id);
        var destination = index + Math.Sign(direction);
        if (index >= 0 && destination >= 0 && destination < options.Count)
            (options[index], options[destination]) = (options[destination], options[index]);
        ReindexExams(options);
        await SaveExamOptionsAsync(options);
        return Redirect(Url.Page("/Index") + "#exams");
    }

    public async Task<IActionResult> OnPostAddRoomAsync(RoomEditInput roomEdit)
    {
        if (!ModelState.IsValid)
            return await ReloadPageAsync();

        var rooms = (await GetRoomsAsync()).ToList();
        var name = roomEdit.Name.Trim();
        if (rooms.Any(room => string.Equals(room, name, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty, "Cette salle existe déjà.");
            return await ReloadPageAsync();
        }

        rooms.Add(name);
        await SaveRoomsAsync(rooms);
        SuccessMessage = $"La salle {name} a été ajoutée.";
        return Redirect(Url.Page("/Index") + "#rooms");
    }

    public async Task<IActionResult> OnPostUpdateRoomAsync(RoomEditInput roomEdit)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(roomEdit.OriginalName))
            return await ReloadPageAsync();

        var rooms = (await GetRoomsAsync()).ToList();
        var index = rooms.FindIndex(room => string.Equals(room, roomEdit.OriginalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return NotFound();

        var name = roomEdit.Name.Trim();
        if (rooms.Where((_, roomIndex) => roomIndex != index).Any(room => string.Equals(room, name, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty, "Cette salle existe déjà.");
            return await ReloadPageAsync();
        }

        var originalName = rooms[index];
        rooms[index] = name;
        await UpdateRoomReferencesAsync(originalName, name);
        await SaveRoomsAsync(rooms);
        SuccessMessage = $"La salle {originalName} a été renommée en {name}.";
        return Redirect(Url.Page("/Index") + "#rooms");
    }

    public async Task<IActionResult> OnPostDeleteRoomAsync(string name)
    {
        var rooms = (await GetRoomsAsync()).ToList();
        var existing = rooms.FirstOrDefault(room => string.Equals(room, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return NotFound();

        rooms.Remove(existing);
        await UpdateRoomReferencesAsync(existing, string.Empty);
        await SaveRoomsAsync(rooms);
        SuccessMessage = $"La salle {existing} a été supprimée.";
        return Redirect(Url.Page("/Index") + "#rooms");
    }

    public async Task<IActionResult> OnPostMoveRoomAsync(string name, int direction)
    {
        var rooms = (await GetRoomsAsync()).ToList();
        var index = rooms.FindIndex(room => string.Equals(room, name, StringComparison.OrdinalIgnoreCase));
        var destination = index + Math.Sign(direction);
        if (index >= 0 && destination >= 0 && destination < rooms.Count)
            (rooms[index], rooms[destination]) = (rooms[destination], rooms[index]);
        await SaveRoomsAsync(rooms);
        return Redirect(Url.Page("/Index") + "#rooms");
    }

    private async Task<IActionResult> ReloadPageAsync()
    {
        Snapshot = await CreateSnapshotAsync();
        ManagedUsers = await GetManagedUsersAsync();
        ExamOptions = await GetExamOptionsAsync();
        Rooms = await GetRoomsAsync();
        return Page();
    }

    private static void ApplyExamEdit(ExamOption exam, ExamEditInput examEdit)
    {
        exam.Name = examEdit.Name.Trim();
        exam.Description = examEdit.Description.Trim();
        exam.Color = examEdit.Color.Trim();
        exam.CodeMSG = examEdit.CodeMSG.Trim();
        exam.Annotation = examEdit.Annotation.Trim();
        exam.EndAnnotation = examEdit.EndAnnotation.Trim();
        exam.Floor = examEdit.Floor.Trim();
    }

    private async Task<IReadOnlyList<ExamOption>> GetExamOptionsAsync()
    {
        var config = await _db.ServerConfigs.AsNoTracking().SingleOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(config?.ExamOptionsJson))
            return [];
        try
        {
            return (JsonSerializer.Deserialize<List<ExamOption>>(config.ExamOptionsJson) ?? [])
                .OrderBy(item => item.Index).ToList();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "La configuration des examens est illisible.");
            return [];
        }
    }

    private async Task SaveExamOptionsAsync(List<ExamOption> options, string? renamedFrom = null, string? renamedExamId = null)
    {
        ReindexExams(options);
        var config = await _db.ServerConfigs.SingleOrDefaultAsync();
        if (config is null)
        {
            config = new ServerConfig();
            _db.ServerConfigs.Add(config);
        }
        config.ExamOptionsJson = JsonSerializer.Serialize(options);

        var colors = options
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Color, StringComparer.OrdinalIgnoreCase);
        var examNames = colors.Keys.ToList();
        var patients = await _db.Patients
            .Where(patient => examNames.Contains(patient.Exams) || (renamedFrom != null && patient.Exams == renamedFrom))
            .ToListAsync();
        var renamedExam = renamedExamId is null ? null : options.FirstOrDefault(item => item.Id == renamedExamId);
        foreach (var patient in patients)
        {
            if (renamedExam is not null && string.Equals(patient.Exams, renamedFrom, StringComparison.OrdinalIgnoreCase))
            {
                patient.Exams = renamedExam.Name;
                patient.Colors = renamedExam.Color;
            }
            else if (colors.TryGetValue(patient.Exams, out var color))
            {
                patient.Colors = color;
            }
        }
        await _db.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("ExamOptionsUpdated", options);
        foreach (var patient in patients)
            await _hubContext.Clients.All.SendAsync("PatientUpdated", patient);
    }

    private async Task<IReadOnlyList<string>> GetRoomsAsync()
    {
        var config = await _db.ServerConfigs.AsNoTracking().SingleOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(config?.RoomsJson))
            return [];
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(config.RoomsJson) ?? [])
                .Where(room => !string.IsNullOrWhiteSpace(room)).ToList();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "La configuration des salles est illisible.");
            return [];
        }
    }

    private async Task SaveRoomsAsync(List<string> rooms)
    {
        var config = await _db.ServerConfigs.SingleOrDefaultAsync();
        if (config is null)
        {
            config = new ServerConfig();
            _db.ServerConfigs.Add(config);
        }
        config.RoomsJson = JsonSerializer.Serialize(rooms);
        await _db.SaveChangesAsync();
        await _hubContext.Clients.All.SendAsync("RoomsUpdated", rooms);
    }

    private async Task UpdateRoomReferencesAsync(string oldName, string newName)
    {
        var options = (await GetExamOptionsAsync()).ToList();
        var changedOptions = options.Where(exam => string.Equals(exam.Floor, oldName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var exam in changedOptions)
            exam.Floor = newName;

        var users = await _db.KnownUsers.Where(user => user.Room == oldName).ToListAsync();
        foreach (var user in users)
            user.Room = newName;

        if (changedOptions.Count > 0)
        {
            var config = await _db.ServerConfigs.SingleAsync();
            config.ExamOptionsJson = JsonSerializer.Serialize(options);
            await _hubContext.Clients.All.SendAsync("ExamOptionsUpdated", options);
        }
        await _db.SaveChangesAsync();
        if (users.Count > 0)
        {
            foreach (var user in users)
                ChatHub.ApplyAdministrativeUserUpdate(user);
            await BroadcastUsersAsync();
        }
    }

    private static void ReindexExams(IList<ExamOption> options)
    {
        for (var index = 0; index < options.Count; index++)
            options[index].Index = index + 1;
    }

    private async Task<IReadOnlyList<ManagedUser>> GetManagedUsersAsync() => await _db.KnownUsers
        .AsNoTracking()
        .OrderByDescending(user => user.IsOnline)
        .ThenBy(user => user.DisplayName)
        .Select(user => new ManagedUser(user.Id, user.Username, user.DisplayName, user.Room, user.Note, user.ColorUserName, user.IsOnline))
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
    public sealed record ManagedUser(int Id, string Username, string DisplayName, string Room, string Note, string ColorUserName, bool IsOnline);
    public sealed record DailyPatientSnapshot(string Label, int Count);

    public sealed class UserEditInput
    {
        [Range(1, int.MaxValue)] public int Id { get; set; }
        [Required(ErrorMessage = "Le nom affiché est obligatoire.")]
        [StringLength(80)] public string DisplayName { get; set; } = string.Empty;
        [StringLength(80)] public string Room { get; set; } = string.Empty;
        [StringLength(200)] public string Note { get; set; } = string.Empty;
        [StringLength(30)] public string ColorUserName { get; set; } = string.Empty;
    }

    public sealed class ExamEditInput
    {
        [Required] public string Id { get; set; } = string.Empty;
        [Required(ErrorMessage = "Le nom de l’examen est obligatoire.")]
        [StringLength(80)] public string Name { get; set; } = string.Empty;
        [StringLength(160)] public string Description { get; set; } = string.Empty;
        [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "La couleur doit être au format #RRGGBB.")]
        public string Color { get; set; } = "#246BFD";
        [StringLength(80)] public string CodeMSG { get; set; } = string.Empty;
        [StringLength(200)] public string Annotation { get; set; } = string.Empty;
        [StringLength(200)] public string EndAnnotation { get; set; } = string.Empty;
        [StringLength(80)] public string Floor { get; set; } = string.Empty;
    }

    public sealed class RoomEditInput
    {
        public string OriginalName { get; set; } = string.Empty;
        [Required(ErrorMessage = "Le nom de la salle est obligatoire.")]
        [StringLength(80)] public string Name { get; set; } = string.Empty;
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
