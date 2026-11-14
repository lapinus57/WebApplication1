using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Startup;

/// <summary>
/// Réglages Access du serveur, saisis dans l'administration (page « Réglages Access »).
/// Ils priment sur les réglages FoAccess:* du fichier de configuration. Le mot de passe est chiffré.
/// </summary>
public sealed class ServerAccessSettings
{
    public string DatabasePath { get; set; } = "";
    public string Provider { get; set; } = "";
    public string WorkgroupPath { get; set; } = "";
    public string UserName { get; set; } = "";
    /// <summary>Mot de passe Access chiffré (jamais en clair dans la base).</summary>
    public string ProtectedPassword { get; set; } = "";
}

public sealed class ServerAccessSettingsStore(ChatDbContext db, IDataProtectionProvider protection)
{
    private IDataProtector Protector => protection.CreateProtector("EyeChat.ServerAccessSettings.Password.v1");

    public ServerAccessSettings Load()
    {
        var json = db.ServerConfigs.AsNoTracking().Select(c => c.AccessSettingsJson).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(json)) return new ServerAccessSettings();
        try { return JsonSerializer.Deserialize<ServerAccessSettings>(json) ?? new ServerAccessSettings(); }
        catch (JsonException) { return new ServerAccessSettings(); }
    }

    /// <summary>Mot de passe en clair, ou null s'il n'y en a pas ou s'il ne peut plus être déchiffré.</summary>
    public string? Password(ServerAccessSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedPassword)) return null;
        try { return Protector.Unprotect(settings.ProtectedPassword); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <param name="newPassword">Nouveau mot de passe ; vide = garder l'actuel.</param>
    public async Task SaveAsync(string databasePath, string provider, string workgroupPath, string userName,
        string? newPassword, bool clearPassword, CancellationToken ct)
    {
        var settings = Load();
        settings.DatabasePath = databasePath.Trim();
        settings.Provider = provider.Trim();
        settings.WorkgroupPath = workgroupPath.Trim();
        settings.UserName = userName.Trim();
        if (clearPassword) settings.ProtectedPassword = "";
        else if (!string.IsNullOrEmpty(newPassword)) settings.ProtectedPassword = Protector.Protect(newPassword);

        var config = await db.ServerConfigs.FirstOrDefaultAsync(ct);
        if (config == null) { config = new ServerConfig(); db.ServerConfigs.Add(config); }
        config.AccessSettingsJson = JsonSerializer.Serialize(settings);
        await db.SaveChangesAsync(ct);
    }
}
