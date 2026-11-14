using ChatServeur.EyeMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EyeMailModel(EyeMailDbContext db, EyeMailImport import, IConfiguration configuration, ChatServeur.Fundus.FoAccessReader access) : PageModel
{
    public const int PageSize = 100;

    [BindProperty] public string? Query { get; set; } = "";
    [BindProperty] public bool Inactive { get; set; }
    [BindProperty] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<EyeMailPatient> Patients { get; private set; } = Array.Empty<EyeMailPatient>();
    public EyeMailImportRun? LastRun { get; private set; }
    public EyeMailImportStatus ImportStatus => import.Status;
    public bool WatchImport { get; private set; }
    public int ActiveCount { get; private set; }
    public int InactiveCount { get; private set; }
    public int MatchCount { get; private set; }
    public int Pages => Math.Max(1, (MatchCount + PageSize - 1) / PageSize);
    public string Error { get; private set; } = "";
    public (string Path, string Origin, bool Exists) AccessSource { get; private set; } = ("", "", false);
    public bool UsesWorkgroup { get; private set; }
    public string Notice { get; private set; } = "";

    public string FormatUtc(DateTime? date) => date.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris"))
            .ToString("g", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : "jamais";

    public async Task OnGetAsync() => await GuardAsync(LoadAsync);

    public async Task<IActionResult> OnPostSearchAsync() { await GuardAsync(LoadAsync); return Page(); }

    public async Task<IActionResult> OnPostImportAsync()
    {
        Notice = import.Start() ? "Import démarré. Vous pouvez suivre son avancement ci-dessous." : "Un import est déjà en cours.";
        WatchImport = true;
        await GuardAsync(LoadAsync);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync() { import.Cancel(); await GuardAsync(LoadAsync); return Page(); }

    public IActionResult OnGetProgress() { NoCache(); return new JsonResult(import.Status); }

    private async Task LoadAsync()
    {
        WatchImport |= import.Status.Running;
        Query ??= "";
        if (Query.Length > 100 || PageNumber < 1 || PageNumber > 10_000)
            throw new InvalidDataException("Recherche invalide.");

        var ct = HttpContext.RequestAborted;
        try { AccessSource = access.DescribeSource(); UsesWorkgroup = access.UsesWorkgroup; } catch { AccessSource = ("", "illisible", false); }
        ActiveCount = await db.Patients.CountAsync(p => p.IsActive, ct);
        InactiveCount = await db.Patients.CountAsync(p => !p.IsActive, ct);
        LastRun = await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);

        var query = db.Patients.AsNoTracking().Where(p => p.IsActive != Inactive);
        // Chaque mot saisi doit apparaître (nom, prénom, nom de jeune fille ou identifiant Access).
        foreach (var word in EyeMailColumns.SearchText(Query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            query = query.Where(p => p.SearchName.Contains(word));
        MatchCount = await query.CountAsync(ct);
        PageNumber = Math.Min(PageNumber, Pages);
        Patients = await query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.AccessPatientId)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(ct);
    }

    private void NoCache()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private async Task GuardAsync(Func<Task> action)
    {
        NoCache();
        try { await action(); }
        catch (InvalidDataException ex) { Error = ex.Message; }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch { Error = "La lecture de la base EyeMail a échoué. Vérifiez le dossier data du serveur."; }
    }
}
