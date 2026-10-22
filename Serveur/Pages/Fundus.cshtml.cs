using System.Text.Json;
using ChatServeur;
using ChatServeur.Fundus;
using EyeChat.Fundus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FundusModel(ChatDbContext db, IFoAccessReader access, FoStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public FoStatus? Status { get; set; }
    [BindProperty, Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] public FoMapping Mapping { get; set; } = new();
    public IReadOnlyList<FoField> Fields { get; private set; } = Array.Empty<FoField>();
    public IReadOnlyList<FoAlert> Alerts { get; private set; } = Array.Empty<FoAlert>();
    public FoConsultation? Consultation { get; private set; }
    public string Error { get; private set; } = "";
    public string Notice { get; private set; } = "";
    public bool Configured { get; private set; }
    public bool ShowMapping { get; private set; }
    public DateTime Today => access.Now.Date;
    public int Total { get; private set; }
    public int PlannedCount { get; private set; }
    public int VerifyCount { get; private set; }
    public int CompletedCount { get; private set; }
    public bool AutoSearch { get; private set; }
    public bool SearchCompleted { get; private set; }
    public int PatientsAnalyzed { get; private set; }
    public DateTime? SearchTime { get; private set; }
    private IReadOnlyList<FoAlert> _periodAlerts = Array.Empty<FoAlert>();

    public async Task OnGetAsync(bool configure = false, int? source = null)
    {
        await GuardAsync(async () =>
        {
            await LoadAsync();
            if (configure || !Configured)
            {
                ShowMapping = true;
                Fields = access.Inspect();
                if (!Configured) Mapping = FoMapping.Suggest(Fields);
            }
            AutoSearch = Configured && !configure && !source.HasValue;
            if (source.HasValue)
            {
                var settings = await store.SettingsAsync(HttpContext.RequestAborted);
                var alert = _periodAlerts.SingleOrDefault(a => a.Id == source);
                if (alert == null || settings == null) throw new FoDataException("Alerte indisponible dans cette période.");
                if (access.SourceKey(Mapping) != settings.SourceKey) throw new FoDataException("La source Access a changé. Vérifiez les champs et actualisez.");
                Consultation = access.ReadConsultation(Mapping, alert.PatientId, alert.SourceConsultationId);
            }
        });
    }

    public async Task<IActionResult> OnPostMappingAsync()
    {
        ShowMapping = true;
        await GuardAsync(async () =>
        {
            SetRange();
            Fields = access.Inspect();
            if (!ModelState.IsValid) throw new FoDataException("Vérifiez les champs et les dates saisis.");
            await store.SaveMappingAsync(Mapping, HttpContext.RequestAborted);
            Notice = "Correspondances enregistrées. La recherche des patients va démarrer.";
            await LoadAsync();
            ShowMapping = false;
            AutoSearch = true;
        });
        return Page();
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        await GuardAsync(async () =>
        {
            await LoadAsync(); // Keep the previous list available if Access cannot be read.
            PatientsAnalyzed = await store.RefreshAsync(From!.Value, To!.Value.AddDays(1), HttpContext.RequestAborted);
            SearchCompleted = true;
            SearchTime = access.Now;
            Notice = $"Analyse actualisée : consultations de {PatientsAnalyzed} patient(s) relues dans Access.";
            await LoadAsync();
        });
        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(int id, string evidenceHash, FoStatus? review)
    {
        await GuardAsync(async () =>
        {
            await LoadAsync();
            await store.ReviewAsync(id, evidenceHash, review, User.Identity!.Name!, HttpContext.RequestAborted);
            Notice = review.HasValue ? "Validation manuelle enregistrée." : "Retour à l’analyse automatique.";
            await LoadAsync();
        });
        return Page();
    }

    private void SetRange()
    {
        if (!ModelState.IsValid) throw new FoDataException("Vérifiez les dates et l’état sélectionné.");
        bool defaultRange = !From.HasValue && !To.HasValue;
        From ??= Today;
        To ??= defaultRange ? From.Value.AddDays(6) : From;
        if (To == DateTime.MaxValue.Date) throw new FoDataException("Date de fin invalide.");
        FoStore.ValidateRange(From.Value, To.Value.AddDays(1), Today);
        if (Status.HasValue && !Enum.IsDefined(Status.Value)) throw new FoDataException("État inconnu.");
    }

    private async Task LoadAsync()
    {
        SetRange();
        var settings = await store.SettingsAsync(HttpContext.RequestAborted);
        Configured = settings != null;
        if (settings == null) return;
        Mapping = JsonSerializer.Deserialize<FoMapping>(settings.MappingJson)!;
        var until = To!.Value.AddDays(1);
        var now = access.Now;
        var rows = await db.FoAlerts.AsNoTracking().Where(a => a.SourceKey == settings.SourceKey && a.NextAppointment >= From && a.NextAppointment < until && a.NextAppointment >= now)
            .OrderBy(a => a.NextAppointment).ThenBy(a => a.PatientName).ToListAsync(HttpContext.RequestAborted);
        _periodAlerts = rows;
        Total = rows.Count;
        PlannedCount = rows.Count(a => a.EffectiveStatus == FoStatus.Planned);
        VerifyCount = rows.Count(a => a.EffectiveStatus == FoStatus.Verify);
        CompletedCount = rows.Count(a => a.EffectiveStatus == FoStatus.ProbablyPerformed);
        Alerts = rows.Where(a => Status.HasValue ? a.EffectiveStatus == Status : a.EffectiveStatus != FoStatus.ProbablyPerformed).ToList();
    }

    private async Task GuardAsync(Func<Task> action)
    {
        // Do not serialize observations, SQL parameter values or Access provider errors to logs/URLs.
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { await action(); }
        catch (FoDataException ex) { AutoSearch = false; Error = ex.Message; }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch { AutoSearch = false; Error = "La lecture ou l’enregistrement a échoué. Vérifiez l’accès du serveur à Access, le fournisseur ACE, les champs choisis et les droits SQLite. Aucun résultat partiel d’analyse n’est enregistré."; }
    }
}
