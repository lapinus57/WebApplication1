using System.Globalization;
using System.Text;
using ChatServeur.Families;
using EyeChat.Families;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(200_000_000)]
public sealed class FamiliesModel(FamilyStore store, FamilyRebuild rebuild, IConfiguration configuration) : PageModel
{
    public const int PageSize = 30;

    [BindProperty(SupportsGet = true)] public int? Patient { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public FamilyRebuildStatus RebuildStatus => rebuild.Status;
    public bool WatchRebuild { get; private set; }
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public int FamilyCount { get; private set; }
    public int ToReviewCount { get; private set; }
    public int DecisionCount { get; private set; }
    public int VitaleCount { get; private set; }
    public string VitaleFile { get; private set; } = "";
    public string VitaleImported { get; private set; } = "";
    public string LastRun { get; private set; } = "";
    public string LastRunMessage { get; private set; } = "";
    public string LastReport { get; private set; } = "";
    public IReadOnlyList<PaymentBatch> Batches { get; private set; } = Array.Empty<PaymentBatch>();
    public IReadOnlyList<StoredFamily> Shown { get; private set; } = Array.Empty<StoredFamily>();
    public Dictionary<int, List<StoredKinship>> Kin { get; } = new();
    public int Pages => Math.Max(1, (ToReviewCount + PageSize - 1) / PageSize);
    public string NightlyHour => configuration["Families:NightlyHour"] is { Length: > 0 } h ? h : "3";

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public string Local(string? utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
        ? TimeZoneInfo.ConvertTimeFromUtc(d.ToUniversalTime(), TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris")).ToString("g", Fr)
        : "jamais";

    public void OnGet() => Guard(Load);

    public IActionResult OnGetProgress()
    {
        NoCache();
        return new JsonResult(rebuild.Status);
    }

    public IActionResult OnPostRebuild()
    {
        Notice = rebuild.Start() ? "Calcul démarré : il relit tout l'agenda (quelques minutes). Les familles actuelles restent en service jusqu'à la fin." : "Un calcul est déjà en cours.";
        WatchRebuild = true;
        Guard(Load);
        return Page();
    }

    public IActionResult OnPostCancel() { rebuild.Cancel(); Guard(Load); return Page(); }

    public async Task<IActionResult> OnPostVitaleAsync(IFormFile? file)
    {
        await GuardAsync(async () =>
        {
            if (file is not { Length: > 0 }) throw new InvalidDataException("Choisissez le fichier export-vitale.csv.");
            List<VitaleRow> rows;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true)) rows = VitaleLinks.ReadCsv(reader);
            await store.ReplaceVitaleAsync(rows, Path.GetFileName(file.FileName));
            Notice = $"Photo de la carte Vitale importée : {rows.Count:N0} fiches. Elle sera utilisée au prochain calcul.";
        });
        return Page();
    }

    public async Task<IActionResult> OnPostPaymentsAsync(IFormFile? file)
    {
        await GuardAsync(async () =>
        {
            if (file is not { Length: > 0 }) throw new InvalidDataException("Choisissez l'export « suivi des paiements » (CSV).");
            var path = Path.GetTempFileName();
            try
            {
                await using (var target = System.IO.File.Create(path)) await file.CopyToAsync(target);
                List<PaymentRow> rows;
                using (var reader = PaymentLinks.Open(path)) rows = PaymentLinks.ReadCsv(reader);
                var batch = await store.AddPaymentsAsync(rows, Path.GetFileName(file.FileName));
                Notice = $"Export des paiements ajouté : {batch.Lines:N0} factures du {batch.First:dd/MM/yyyy} au {batch.Last:dd/MM/yyyy}. "
                    + "Le NIR n'est pas conservé. L'export sera utilisé au prochain calcul.";
            }
            finally { System.IO.File.Delete(path); }
        });
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteBatchAsync(int id)
    {
        await GuardAsync(async () => { await store.DeletePaymentBatchAsync(id); Notice = "Export supprimé. Pris en compte au prochain calcul."; });
        return Page();
    }

    public async Task<IActionResult> OnPostValidateAsync(string members)
    {
        await GuardAsync(async () =>
        {
            var ids = ParseIds(members);
            if (ids.Count < 2) throw new InvalidDataException("Famille invalide.");
            await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Validate, 0, 0, FamilyDecision.Signature(ids)), Author);
            Notice = "Famille validée : elle ne sera plus « à vérifier » au prochain calcul (tant que ses membres ne changent pas).";
        });
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int patient, string members)
    {
        await GuardAsync(async () =>
        {
            var others = ParseIds(members).Where(id => id != patient).ToList();
            if (patient <= 0 || others.Count == 0) throw new InvalidDataException("Patient invalide.");
            foreach (var other in others) await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Separate, patient, other), Author);
            Notice = $"Le patient {patient} sera retiré de ce foyer au prochain calcul.";
        });
        return Page();
    }

    public async Task<IActionResult> OnPostJoinAsync(int first, int second)
    {
        await GuardAsync(async () =>
        {
            if (first <= 0 || second <= 0 || first == second) throw new InvalidDataException("Indiquez deux codes patient différents.");
            await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Join, first, second), Author);
            Notice = $"Les patients {first} et {second} seront regroupés au prochain calcul.";
        });
        return Page();
    }

    private string Author => User.Identity?.Name ?? "";

    private static List<int> ParseIds(string? text) => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    private void Load()
    {
        WatchRebuild |= rebuild.Status.Running;
        if (PageNumber < 1 || PageNumber > 10_000) PageNumber = 1;
        FamilyCount = store.FamilyCount();
        ToReviewCount = store.FamilyCount(toReviewOnly: true);
        DecisionCount = store.DecisionCount();
        VitaleCount = store.VitaleCount();
        VitaleFile = store.Meta("vitale_file") ?? "";
        VitaleImported = Local(store.Meta("vitale_imported_utc"));
        LastRun = Local(store.Meta("last_run_utc"));
        LastRunMessage = store.Meta("last_run_message") ?? "";
        LastReport = store.Meta("last_report") ?? "";
        Batches = store.PaymentBatches();
        Shown = Patient is > 0
            ? store.Families(patientId: Patient)
            : store.Families(toReviewOnly: true, limit: 100_000).Skip((Math.Min(PageNumber, Pages) - 1) * PageSize).Take(PageSize).ToList();
        foreach (var m in Shown.SelectMany(f => f.Members)) Kin[m.PatientId] = store.Kinship(m.PatientId);
        if (Patient is > 0 && Shown.Count == 0)
            Notice = $"Le patient {Patient} n'est dans aucune famille.";
    }

    private void NoCache()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private void Guard(Action action)
    {
        NoCache();
        try { action(); }
        catch (InvalidDataException ex) { Error = ex.Message; }
        catch (Exception) { Error = "La lecture de la base des familles a échoué. Vérifiez le dossier data du serveur."; }
    }

    private async Task GuardAsync(Func<Task> action)
    {
        NoCache();
        try { await action(); }
        catch (InvalidDataException ex) { Error = ex.Message; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = "Opération échouée : " + ex.Message; }
        Guard(Load);
    }
}
