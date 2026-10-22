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

    public async Task OnGetAsync(bool configure = false, int? source = null)
    {
        await GuardAsync(async () =>
        {
            await LoadAsync();
            if (configure) { Fields = access.Inspect(); ShowMapping = true; if (!Configured) Mapping = FoMapping.Suggest(Fields); }
            if (source.HasValue)
            {
                var settings = await store.SettingsAsync(HttpContext.RequestAborted);
                var alert = Alerts.SingleOrDefault(a => a.Id == source);
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
            Fields = access.Inspect();
            if (!ModelState.IsValid) throw new FoDataException("Vérifiez les champs et les dates saisis.");
            await store.SaveMappingAsync(Mapping, HttpContext.RequestAborted);
            Notice = "Correspondances enregistrées. Cliquez sur Actualiser l’analyse pour lire les consultations.";
            await LoadAsync();
            ShowMapping = false;
        });
        return Page();
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        await GuardAsync(async () =>
        {
            SetRange();
            if (!ModelState.IsValid) throw new FoDataException("Vérifiez les dates et l’état sélectionné.");
            int patients = await store.RefreshAsync(From!.Value, To!.Value.AddDays(1), HttpContext.RequestAborted);
            Notice = $"Analyse actualisée pour {patients} patient(s) ayant un rendez-vous futur dans la période.";
            await LoadAsync();
        });
        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(int id, string evidenceHash, FoStatus? review)
    {
        await GuardAsync(async () =>
        {
            if (!ModelState.IsValid) throw new FoDataException("État ou dates invalides.");
            await store.ReviewAsync(id, evidenceHash, review, User.Identity!.Name!, HttpContext.RequestAborted);
            Notice = review.HasValue ? "Validation manuelle enregistrée." : "Retour à l’analyse automatique.";
            await LoadAsync();
        });
        return Page();
    }

    private void SetRange()
    {
        From ??= Today;
        To ??= From;
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
        Total = rows.Count;
        Alerts = rows.Where(a => !Status.HasValue || a.EffectiveStatus == Status).ToList();
    }

    private async Task GuardAsync(Func<Task> action)
    {
        // Do not serialize observations, SQL parameter values or Access provider errors to logs/URLs.
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { await action(); }
        catch (FoDataException ex) { Error = ex.Message; }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch { Error = "La lecture ou l’enregistrement a échoué. Vérifiez l’accès du serveur à Access, le fournisseur ACE, les champs choisis et les droits SQLite. Aucun résultat partiel d’analyse n’est enregistré."; }
    }
}
