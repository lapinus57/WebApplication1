using System.Text.Json;
using ChatServeur;
using ChatServeur.Registry;
using EyeChat.Fundus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientRegistryModel(ChatDbContext db, PatientRegistryStore store, IRegistryAccessReader reader, PatientRegistryImport import, IConfiguration configuration) : PageModel
{
    [BindProperty] public string? Query { get; set; } = "";
    [BindProperty] public PatientConditionState? DiabetesFilter { get; set; }
    [BindProperty] public PatientConditionState? HtoFilter { get; set; }
    [BindProperty] public bool Inactive { get; set; }
    [BindProperty] public bool ReviewOnly { get; set; }
    [BindProperty] public int PageNumber { get; set; } = 1;
    [BindProperty, ValidateNever] public FoMapping Mapping { get; set; } = new();
    public IReadOnlyList<FoField> Fields { get; private set; } = Array.Empty<FoField>();
    public IReadOnlyList<PatientRegistryEntry> Patients { get; private set; } = Array.Empty<PatientRegistryEntry>();
    public PatientRegistryEntry? Selected { get; private set; }
    public PatientRegistrySettings? Settings { get; private set; }
    public RegistryImportStatus ImportStatus => import.Status;
    public bool ShowMapping { get; private set; }
    public bool WatchImport { get; private set; }
    public string Error { get; private set; } = "";
    public string Notice { get; private set; } = "";
    public int Total { get; private set; }
    public int MatchCount { get; private set; }
    public int Pages => Math.Max(1, (MatchCount + 99) / 100);
    public string FormatUtc(DateTime? date) => date.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris"))
            .ToString("g", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : "aucun";

    public async Task OnGetAsync(bool configure = false) => await GuardAsync(async () =>
    {
        await LoadAsync();
        if (configure || Settings == null)
        {
            ShowMapping = true; Fields = reader.Inspect();
            if (Settings == null) Mapping = FoMapping.Suggest(Fields);
        }
    });

    public async Task<IActionResult> OnPostMappingAsync()
    {
        ShowMapping = true;
        await GuardAsync(async () =>
        {
            if (!ModelState.IsValid) throw new FoDataException("Vérifiez les valeurs du formulaire.");
            await store.SaveMappingAsync(Mapping, HttpContext.RequestAborted);
            Notice = "Correspondances enregistrées. Vous pouvez importer tous les patients.";
            ShowMapping = false; await LoadAsync();
        });
        if (ShowMapping) await GuardAsync(() => { Fields = reader.Inspect(); return Task.CompletedTask; });
        return Page();
    }

    public async Task<IActionResult> OnPostSearchAsync() { await GuardAsync(LoadAsync); return Page(); }
    public async Task<IActionResult> OnPostImportAsync()
    {
        await GuardAsync(async () =>
        {
            await LoadAsync();
            if (Settings == null) throw new FoDataException("Configurez les tables avant l’import.");
            Notice = import.Start() ? "Import démarré. Vous pouvez suivre son avancement ci-dessous." : "Un import est déjà en cours.";
            WatchImport = true;
        });
        return Page();
    }
    public async Task<IActionResult> OnPostCancelAsync() { import.Cancel(); await GuardAsync(LoadAsync); return Page(); }
    public IActionResult OnGetProgress()
    {
        NoCache(); return new JsonResult(import.Status);
    }

    public async Task<IActionResult> OnPostDetailsAsync(int id)
    {
        await GuardAsync(async () => { await LoadAsync(); await SelectAsync(id); }); return Page();
    }
    public async Task<IActionResult> OnPostReviewAsync(int id, string revision, PatientConditionState? diabetesManual, PatientConditionState? htoManual)
    {
        await GuardAsync(async () =>
        {
            await LoadAsync();
            if (!ModelState.IsValid) throw new FoDataException("Choisissez un état valide pour le diabète et l’HTO.");
            await store.ReviewAsync(id, revision, diabetesManual, htoManual, User.Identity!.Name!, HttpContext.RequestAborted);
            Notice = "Vérification enregistrée sur le serveur. Access reste inchangé.";
            await LoadAsync(); await SelectAsync(id);
        });
        return Page();
    }

    private async Task SelectAsync(int id)
    {
        Selected = Settings == null ? null : await db.PatientRegistry.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.SourceKey == Settings.SourceKey, HttpContext.RequestAborted);
        if (Selected == null) throw new FoDataException("Patient indisponible dans la source active.");
    }
    private async Task LoadAsync()
    {
        WatchImport |= import.Status.Running;
        Query ??= "";
        if (!ModelState.IsValid || Query.Length > 100 || PageNumber < 1 || PageNumber > 1000 ||
            (DiabetesFilter.HasValue && !Enum.IsDefined(DiabetesFilter.Value)) || (HtoFilter.HasValue && !Enum.IsDefined(HtoFilter.Value)))
            throw new FoDataException("Recherche ou filtre invalide.");
        Settings = await store.SettingsAsync(HttpContext.RequestAborted);
        if (Settings == null) return;
        Mapping = JsonSerializer.Deserialize<FoMapping>(Settings.MappingJson)!;
        var source = db.PatientRegistry.AsNoTracking().Where(p => p.SourceKey == Settings.SourceKey);
        Total = await source.CountAsync(p => p.IsActive, HttpContext.RequestAborted);
        var query = source.Where(p => p.IsActive != Inactive);
        var search = FundusAnalysis.Normalize(Query.Trim());
        if (search.Length != 0) query = query.Where(p => p.SearchName.Contains(search));
        if (DiabetesFilter.HasValue) query = query.Where(p => (p.DiabetesManual ?? p.DiabetesDetected) == DiabetesFilter);
        if (HtoFilter.HasValue) query = query.Where(p => (p.HtoManual ?? p.HtoDetected) == HtoFilter);
        if (ReviewOnly) query = query.Where(p =>
            (p.DiabetesManual.HasValue && p.DiabetesReviewedHash != p.DiabetesEvidenceHash) ||
            (p.HtoManual.HasValue && p.HtoReviewedHash != p.HtoEvidenceHash) ||
            (p.DiabetesManual ?? p.DiabetesDetected) == PatientConditionState.ToReview ||
            (p.HtoManual ?? p.HtoDetected) == PatientConditionState.ToReview);
        MatchCount = await query.CountAsync(HttpContext.RequestAborted);
        PageNumber = Math.Min(PageNumber, Pages);
        Patients = await query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.AccessPatientId)
            .Skip((PageNumber - 1) * 100).Take(100).ToListAsync(HttpContext.RequestAborted);
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
        catch (FoDataException ex) { Error = ex.Message; }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch { Error = "La lecture ou l’enregistrement a échoué. Vérifiez les droits du serveur et les réglages Access. Les données précédentes sont conservées."; }
    }
}
