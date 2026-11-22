using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Replies;

/// <summary>
/// Réglages « Réponses des patients » saisis dans l'administration : service externe (Cloudflare Worker)
/// et compte mail qui prévient le cabinet. La clé du service et le mot de passe mail sont chiffrés.
/// </summary>
public sealed class ReplySettings
{
    public string RelayUrl { get; set; } = "";
    public string ProtectedRelayKey { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string SmtpUser { get; set; } = "";
    public string ProtectedSmtpPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "EyeChat";
}

/// <summary>Réglages déchiffrés, prêts à l'emploi.</summary>
public sealed record ReplyConfiguration(string RelayUrl, string RelayKey, string SmtpHost, int SmtpPort, bool SmtpSsl,
    string SmtpUser, string SmtpPassword, string FromAddress, string FromName)
{
    public bool RelayConfigured => RelayUrl.Length > 0 && RelayKey.Length > 0;
    public bool MailConfigured => SmtpHost.Length > 0 && FromAddress.Length > 0;
}

public sealed class ReplySettingsStore(ChatDbContext db, IDataProtectionProvider protection)
{
    private IDataProtector Protector => protection.CreateProtector("EyeChat.ReplySettings.Secrets.v1");

    public ReplySettings Load()
    {
        var json = db.ServerConfigs.AsNoTracking().Select(c => c.ReplySettingsJson).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(json)) return new ReplySettings();
        try { return JsonSerializer.Deserialize<ReplySettings>(json) ?? new ReplySettings(); }
        catch (JsonException) { return new ReplySettings(); }
    }

    public ReplyConfiguration Resolve()
    {
        var s = Load();
        return new ReplyConfiguration(NormalizeUrl(s.RelayUrl), Unprotect(s.ProtectedRelayKey), s.SmtpHost.Trim(), s.SmtpPort, s.SmtpSsl,
            s.SmtpUser.Trim(), Unprotect(s.ProtectedSmtpPassword), s.FromAddress.Trim(), s.FromName.Trim());
    }

    public static string NormalizeUrl(string url) => (url ?? "").Trim().TrimEnd('/');

    private string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Protector.Unprotect(value); }
        catch (System.Security.Cryptography.CryptographicException) { return ""; }
    }

    /// <param name="relayKey">Nouvelle clé ; vide = garder l'actuelle.</param>
    /// <param name="smtpPassword">Nouveau mot de passe ; vide = garder l'actuel.</param>
    public async Task SaveAsync(ReplySettings values, string? relayKey, string? smtpPassword, CancellationToken ct)
    {
        var settings = Load();
        settings.RelayUrl = NormalizeUrl(values.RelayUrl);
        settings.SmtpHost = values.SmtpHost.Trim();
        settings.SmtpPort = values.SmtpPort;
        settings.SmtpSsl = values.SmtpSsl;
        settings.SmtpUser = values.SmtpUser.Trim();
        settings.FromAddress = values.FromAddress.Trim();
        settings.FromName = values.FromName.Trim();
        if (!string.IsNullOrWhiteSpace(relayKey)) settings.ProtectedRelayKey = Protector.Protect(relayKey.Trim());
        if (!string.IsNullOrEmpty(smtpPassword)) settings.ProtectedSmtpPassword = Protector.Protect(smtpPassword);

        var config = await db.ServerConfigs.FirstOrDefaultAsync(ct);
        if (config == null) { config = new ServerConfig(); db.ServerConfigs.Add(config); }
        config.ReplySettingsJson = JsonSerializer.Serialize(settings);
        await db.SaveChangesAsync(ct);
    }
}
