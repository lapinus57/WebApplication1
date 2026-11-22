using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using EyeChat.SmsMail;

namespace ChatServeur.Replies;

public interface IReplyMailer
{
    Task SendAsync(ReplyConfiguration config, string to, string subject, string htmlBody, CancellationToken ct);
}

public sealed class SmtpReplyMailer : IReplyMailer
{
    public async Task SendAsync(ReplyConfiguration config, string to, string subject, string htmlBody, CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(config.FromAddress, string.IsNullOrWhiteSpace(config.FromName) ? null : config.FromName),
            Subject = subject, SubjectEncoding = Encoding.UTF8, Body = htmlBody, BodyEncoding = Encoding.UTF8, IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to));
        using var smtp = new SmtpClient(config.SmtpHost, config.SmtpPort)
        {
            EnableSsl = config.SmtpSsl, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 30000,
            Credentials = string.IsNullOrEmpty(config.SmtpUser) ? null : new NetworkCredential(config.SmtpUser, config.SmtpPassword)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await smtp.SendMailAsync(message, timeout.Token);
    }
}

/// <summary>
/// Liens « Confirmer / Annuler » : création auprès du service externe avant l'envoi d'un rappel, puis relevé
/// des réponses, mise à jour de l'historique commun et mail au cabinet. Le service externe ne reçoit jamais
/// de nom, téléphone ni adresse mail : uniquement le jeton, le cabinet et la date du rendez-vous.
/// </summary>
public sealed class ReplyLinkService(ReplyConfiguration config, SmsMailHistoryStore store, HttpClient http, IReplyMailer mailer, ILogger logger)
{
    public const string Confirmed = "Confirme";
    public const string Cancelled = "Annule";
    private const string LastResponseState = "LastRelayResponseId";
    private const string TokenAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Le lien reste valable jusqu'à 3 heures après l'heure du rendez-vous.</summary>
    public static readonly TimeSpan ValidAfterAppointment = TimeSpan.FromHours(3);

    public static TimeZoneInfo CabinetTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }

    public static string NewToken() => RandomNumberGenerator.GetString(TokenAlphabet, 8);

    /// <summary>« lundi 23 novembre à 9h30 »</summary>
    public static string AppointmentText(DateTime start)
        => start.ToString("dddd d MMMM", French) + " à " + start.ToString("H'h'mm", French);

    public async Task<string> CreateAsync(ReplyLinkRequest request, CancellationToken ct = default)
    {
        if (!config.RelayConfigured)
            throw new InvalidOperationException("Le service de réponse n'est pas configuré sur le serveur (Administration → Réponses des patients).");
        if (!MailAddress.TryCreate(request.ReplyEmail?.Trim(), out _))
            throw new InvalidOperationException("Adresse mail qui reçoit les réponses invalide.");
        if (request.AppointmentStart == default)
            throw new InvalidOperationException("Heure du rendez-vous manquante.");

        var start = DateTime.SpecifyKind(request.AppointmentStart, DateTimeKind.Unspecified);
        var expires = TimeZoneInfo.ConvertTimeToUtc(start, CabinetTimeZone()) + ValidAfterAppointment;
        if (expires <= DateTime.UtcNow)
            throw new InvalidOperationException("Le rendez-vous est déjà passé : pas de lien de réponse.");

        var token = NewToken();
        var body = new { links = new[] { new
        {
            token, cabinet = request.Cabinet.Trim(), address = request.CabinetAddress.Trim(), appointment = AppointmentText(start),
            expiresAt = new DateTimeOffset(expires, TimeSpan.Zero).ToUnixTimeMilliseconds()
        } } };
        using var message = new HttpRequestMessage(HttpMethod.Post, config.RelayUrl + "/api/links") { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.RelayKey);
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Le service de réponse a refusé le lien ({(int)response.StatusCode}) : {await RelayErrorAsync(response, ct)}");

        await store.AddReplyLinkAsync(new ReplyLinkRecord
        {
            Token = token, MessageKey = request.MessageKey, Channel = request.Channel, Contact = request.Contact, People = request.People.Trim(),
            AppointmentStart = start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), ReplyEmail = request.ReplyEmail.Trim()
        });
        return config.RelayUrl + "/" + token;
    }

    /// <summary>Vérifie l'adresse, la clé et la base du service. Retourne null si tout va bien, sinon la cause.</summary>
    public async Task<string?> PingAsync(CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, config.RelayUrl + "/api/ping");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.RelayKey);
        using var response = await http.SendAsync(message, ct);
        return response.IsSuccessStatusCode ? null : $"{(int)response.StatusCode} : {await RelayErrorAsync(response, ct)}";
    }

    /// <summary>Message d'erreur renvoyé par le Worker ({"error": "..."}), ou explication selon le code HTTP.</summary>
    public static async Task<string> RelayErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? detail = null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("error", out var error)) detail = error.GetString();
        }
        catch (System.Text.Json.JsonException) { }
        return detail ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "clé refusée : la clé saisie dans l'administration doit être identique au secret RELAY_KEY du Worker.",
            HttpStatusCode.NotFound => "adresse inconnue : vérifiez l'adresse du service et que le code du Worker a bien été déployé.",
            _ => "erreur du service ; vérifiez dans Cloudflare que la base D1 est liée (DB) et que schema.sql a été exécuté."
        };
    }

    private sealed record RelayResponse(long Id, string Token, string Choice, long AnsweredAt);
    private sealed record RelayResponses(List<RelayResponse> Responses);

    /// <summary>Relève les nouvelles réponses puis envoie les mails en attente. Retourne le nombre de réponses reçues.</summary>
    public async Task<int> PollAsync(CancellationToken ct = default)
    {
        if (!config.RelayConfigured) return 0;
        int received = 0;
        while (true)
        {
            var last = long.TryParse(await store.GetStateAsync(LastResponseState), out var id) ? id : 0;
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{config.RelayUrl}/api/responses?after={last}");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.RelayKey);
            using var response = await http.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<RelayResponses>(cancellationToken: ct) ?? new RelayResponses(new());
            foreach (var reply in page.Responses.OrderBy(r => r.Id))
            {
                if (reply.Choice is Confirmed or Cancelled)
                {
                    var at = DateTimeOffset.FromUnixTimeMilliseconds(reply.AnsweredAt);
                    if (await store.RecordReplyAsync(reply.Token, reply.Choice, at, HistoryStatus(reply.Choice, at))) received++;
                    else logger.LogWarning("Réponse de patient pour un lien inconnu ignorée.");
                }
                await store.SetStateAsync(LastResponseState, reply.Id.ToString(CultureInfo.InvariantCulture));
            }
            if (page.Responses.Count < 500) break;
        }
        await SendPendingMailsAsync(ct);
        return received;
    }

    public static string HistoryStatus(string choice, DateTimeOffset answeredAt)
    {
        var local = TimeZoneInfo.ConvertTime(answeredAt, CabinetTimeZone());
        return (choice == Cancelled ? "❌ Annulé par le patient" : "✅ Confirmé par le patient") + $" le {local:dd/MM} à {local:HH:mm}";
    }

    private async Task SendPendingMailsAsync(CancellationToken ct)
    {
        var pending = await store.PendingReplyMailsAsync();
        if (pending.Count == 0) return;
        if (!config.MailConfigured)
        {
            logger.LogWarning("SER31: {Count} réponse(s) de patient sans mail envoyé : compte mail non configuré (Administration → Réponses des patients).", pending.Count);
            return;
        }
        foreach (var link in pending)
        {
            var (subject, html) = ReplyMail.Build(link);
            try
            {
                await mailer.SendAsync(config, link.ReplyEmail, subject, html, ct);
                await store.MarkReplyMailSentAsync(link.Token, link.AnsweredAtUtc);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Le mail sera retenté au prochain relevé.
                logger.LogWarning(ex, "SER32: mail de réponse de patient non envoyé à {Address} ; nouvel essai au prochain relevé.", link.ReplyEmail);
            }
        }
    }
}

public static class ReplyMail
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static (string Subject, string Html) Build(ReplyLinkRecord link)
    {
        var start = DateTime.ParseExact(link.AppointmentStart, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var answered = TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(link.AnsweredAtUtc, CultureInfo.InvariantCulture), ReplyLinkService.CabinetTimeZone());
        var cancelled = link.Choice == ReplyLinkService.Cancelled;
        var people = string.IsNullOrWhiteSpace(link.People) ? "Patient" : link.People;
        var when = start.ToString("ddd dd/MM", French) + " " + start.ToString("HH'h'mm", French);
        var subject = (cancelled ? "❌ ANNULATION — " : "✅ Confirmation — ") + people + " — " + when;
        string E(string value) => WebUtility.HtmlEncode(value);
        var html = $"""
            <p><strong>{E(people)}</strong> {(cancelled ? "a <strong>annulé</strong>" : "a <strong>confirmé</strong>")} le rendez-vous du
            <strong>{E(start.ToString("dddd d MMMM yyyy", French))} à {E(start.ToString("HH'h'mm", French))}</strong>.</p>
            <p>Réponse donnée le {answered:dd/MM/yyyy} à {answered:HH:mm}, depuis le lien du rappel envoyé par {E(link.Channel == "Mail" ? "mail" : "SMS")} à {E(link.Contact)}.</p>
            {(cancelled ? "<p>Pensez à libérer le créneau dans l'agenda.</p>" : "")}
            <p style="color:#69778d;font-size:12px">Message automatique d'EyeChat. La réponse figure aussi dans l'historique SMS et mail de tous les postes.</p>
            """;
        return (subject, html);
    }
}

/// <summary>Relève les réponses des patients chaque minute, si le service de réponse est configuré.</summary>
public sealed class ReplyPollingService(IServiceScopeFactory scopes, IHttpClientFactory httpFactory, SmsMailHistoryStore store,
    IReplyMailer mailer, ILogger<ReplyPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        DateTime lastError = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ReplyConfiguration config;
                using (var scope = scopes.CreateScope())
                    config = scope.ServiceProvider.GetRequiredService<ReplySettingsStore>().Resolve();
                if (config.RelayConfigured)
                {
                    var service = new ReplyLinkService(config, store, httpFactory.CreateClient("replies"), mailer, logger);
                    var received = await service.PollAsync(stoppingToken);
                    if (received > 0) logger.LogInformation("{Count} réponse(s) de patient reçue(s).", received);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Une erreur par heure au plus dans le journal (service externe injoignable, etc.).
                if (DateTime.UtcNow - lastError > TimeSpan.FromHours(1))
                {
                    logger.LogWarning(ex, "SER30: relevé des réponses des patients impossible ; nouvel essai chaque minute.");
                    lastError = DateTime.UtcNow;
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

/// <summary>Crée un ReplyLinkService avec les réglages du moment (lus seulement quand on en a besoin).</summary>
public sealed class ReplyLinkServiceFactory(ReplySettingsStore settings, SmsMailHistoryStore store, IHttpClientFactory httpFactory,
    IReplyMailer mailer, ILogger<ReplyLinkService> logger)
{
    public ReplyConfiguration Configuration() => settings.Resolve();
    public ReplyLinkService Create() => Create(settings.Resolve());
    public ReplyLinkService Create(ReplyConfiguration config) => new(config, store, httpFactory.CreateClient("replies"), mailer, logger);
}
