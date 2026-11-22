using System.Net.Mail;
using ChatServeur.Replies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RepliesModel(ReplySettingsStore store, ReplyLinkServiceFactory replies, IReplyMailer mailer, ILogger<RepliesModel> logger) : PageModel
{
    [BindProperty] public string RelayUrl { get; set; } = "";
    [BindProperty] public string? NewRelayKey { get; set; }
    [BindProperty] public string SmtpHost { get; set; } = "";
    [BindProperty] public int SmtpPort { get; set; } = 587;
    [BindProperty] public bool SmtpSsl { get; set; } = true;
    [BindProperty] public string SmtpUser { get; set; } = "";
    [BindProperty] public string? NewSmtpPassword { get; set; }
    [BindProperty] public string FromAddress { get; set; } = "";
    [BindProperty] public string FromName { get; set; } = "EyeChat";
    [BindProperty] public string TestAddress { get; set; } = "";

    public bool HasRelayKey { get; private set; }
    public bool HasSmtpPassword { get; private set; }
    public string State { get; private set; } = "";
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string TestResult { get; private set; } = "";
    public bool TestFailed { get; private set; }

    public void OnGet() => Load();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        var url = ReplySettingsStore.NormalizeUrl(RelayUrl);
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.PathAndQuery != "/"))
            Error = "L’adresse du service doit commencer par https:// et ne contenir que le nom du site (ex. https://rdv.exemple.workers.dev).";
        else if ((NewRelayKey ?? "").Trim().Length is > 0 and < 20)
            Error = "La clé secrète est trop courte (au moins 20 caractères).";
        else if (FromAddress.Trim().Length > 0 && !MailAddress.TryCreate(FromAddress.Trim(), out _))
            Error = "Adresse de l’expéditeur invalide.";
        else if (SmtpPort is < 1 or > 65535)
            Error = "Port SMTP invalide.";
        if (Error.Length > 0) { Load(keepForm: true); return Page(); }

        await store.SaveAsync(new ReplySettings
        {
            RelayUrl = url, SmtpHost = SmtpHost ?? "", SmtpPort = SmtpPort, SmtpSsl = SmtpSsl, SmtpUser = SmtpUser ?? "",
            FromAddress = FromAddress ?? "", FromName = FromName ?? ""
        }, NewRelayKey, NewSmtpPassword, HttpContext.RequestAborted);
        logger.LogInformation("Réglages « Réponses des patients » modifiés par {User}.", User.Identity?.Name);
        Notice = "Réglages enregistrés.";
        Load();
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        Load();
        var config = replies.Configuration();
        if (!config.RelayConfigured) { TestFailed = true; TestResult = "Renseignez d’abord l’adresse du service et sa clé secrète."; return Page(); }
        try
        {
            var problem = await replies.Create(config).PingAsync(HttpContext.RequestAborted);
            TestFailed = problem != null;
            TestResult = problem == null ? "Le service de réponse répond, accepte la clé du serveur et sa base est prête."
                : "Le service de réponse a répondu " + problem;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            TestFailed = true;
            TestResult = "Service injoignable : vérifiez l’adresse et la connexion Internet du serveur. (" + ex.Message + ")";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostTestMailAsync()
    {
        var to = (TestAddress ?? "").Trim();
        Load();
        TestAddress = to;
        var config = replies.Configuration();
        if (!MailAddress.TryCreate(to, out _)) { TestFailed = true; TestResult = "Adresse de test invalide."; return Page(); }
        if (!config.MailConfigured) { TestFailed = true; TestResult = "Renseignez d’abord le serveur SMTP et l’adresse de l’expéditeur."; return Page(); }
        try
        {
            await mailer.SendAsync(config, to, "[TEST] EyeChat — réponses des patients",
                "<p>Ce mail confirme que le serveur EyeChat peut prévenir le cabinet quand un patient confirme ou annule un rendez-vous.</p>",
                HttpContext.RequestAborted);
            TestResult = "Mail de test envoyé à " + to + ".";
        }
        catch (Exception ex)
        {
            TestFailed = true;
            TestResult = "Mail non envoyé : " + ex.Message;
        }
        return Page();
    }

    private void Load(bool keepForm = false)
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var saved = store.Load();
        HasRelayKey = !string.IsNullOrEmpty(saved.ProtectedRelayKey);
        HasSmtpPassword = !string.IsNullOrEmpty(saved.ProtectedSmtpPassword);
        if (!keepForm)
        {
            RelayUrl = saved.RelayUrl; SmtpHost = saved.SmtpHost; SmtpPort = saved.SmtpPort; SmtpSsl = saved.SmtpSsl;
            SmtpUser = saved.SmtpUser; FromAddress = saved.FromAddress; FromName = saved.FromName;
            NewRelayKey = null; NewSmtpPassword = null;
        }
        var config = store.Resolve();
        State = !config.RelayConfigured ? "non configuré — les postes ne peuvent pas ajouter le lien « Confirmer / Annuler »."
            : !config.MailConfigured ? "service configuré, mais aucun compte mail : les réponses s’inscrivent dans l’historique sans prévenir par mail."
            : "configuré — réponses relevées chaque minute.";
    }
}
