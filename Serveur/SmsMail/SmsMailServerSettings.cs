using System.Text;
using System.Text.Json;
using Client.SmsMail.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace ChatServeur.SmsMail;

/// <summary>Quels secrets sont enregistrés sur le serveur (leur valeur n'est jamais renvoyée aux postes).</summary>
public sealed class SmsMailSecretsState
{
    public bool SmsApplicationSecret { get; set; }
    public bool SmsConsumerKey { get; set; }
    public bool MailPassword { get; set; }
}

/// <summary>Réglages SMS et mail tels qu'un poste les reçoit : sans mots de passe ni clés secrètes.</summary>
public sealed class SmsMailStationSettings
{
    /// <summary>Faux tant qu'aucun poste n'a enregistré de réglages sur le serveur.</summary>
    public bool Exists { get; set; }
    public SmsMailSettings Settings { get; set; } = new();
    public SmsMailSecretsState Secrets { get; set; } = new();
}

/// <summary>
/// Réglages SMS et mail communs à tous les postes, gardés par le serveur (data\sms-mail-settings.dat, chiffré avec les clés
/// du serveur). C'est le serveur qui lit l'agenda et envoie : les clés OVH et le mot de passe SMTP ne quittent jamais le serveur.
/// </summary>
public sealed class SmsMailServerSettings(IDataProtectionProvider protection, string path)
{
    private readonly IDataProtector _protector = protection.CreateProtector("EyeChat.SmsMail.Settings.v1");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public SmsMailSettings? Load()
    {
        if (!File.Exists(path)) return null;
        var json = _protector.Unprotect(File.ReadAllBytes(path));
        var settings = JsonSerializer.Deserialize<SmsMailSettings>(json) ?? throw new InvalidDataException("Réglages SMS et mail du serveur illisibles.");
        if (settings.EmailTemplate == SmsMailSettings.LegacyEmailTemplate || settings.EmailTemplate == SmsMailSettings.PreviousEmailTemplate)
            settings.EmailTemplate = SmsMailSettings.DefaultEmailTemplate;
        return settings;
    }

    /// <summary>Réglages pour un poste : mêmes valeurs, mais les secrets sont remplacés par des champs vides.</summary>
    public SmsMailStationSettings ForStation()
    {
        var current = Load();
        if (current == null) return new SmsMailStationSettings { Exists = false };
        var copy = Clone(current);
        var state = new SmsMailSecretsState
        {
            SmsApplicationSecret = copy.Sms.ApplicationSecret.Length > 0,
            SmsConsumerKey = copy.Sms.ConsumerKey.Length > 0,
            MailPassword = copy.Email.Password.Length > 0,
        };
        copy.Sms.ApplicationSecret = ""; copy.Sms.ConsumerKey = ""; copy.Email.Password = "";
        return new SmsMailStationSettings { Exists = true, Settings = copy, Secrets = state };
    }

    /// <summary>Enregistre les réglages d'un poste. Un secret laissé vide garde la valeur déjà enregistrée.</summary>
    public async Task SaveAsync(SmsMailSettings incoming)
    {
        await Gate.WaitAsync();
        try
        {
            var current = Load();
            var next = Clone(incoming);
            if (current != null)
            {
                if (next.Sms.ApplicationSecret.Length == 0) next.Sms.ApplicationSecret = current.Sms.ApplicationSecret;
                if (next.Sms.ConsumerKey.Length == 0) next.Sms.ConsumerKey = current.Sms.ConsumerKey;
                if (next.Email.Password.Length == 0) next.Email.Password = current.Email.Password;
            }
            Write(next);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Premier poste mis à jour : ses réglages locaux (secrets compris) deviennent ceux du serveur, s'il n'en a pas encore.</summary>
    public async Task<bool> ImportIfEmptyAsync(SmsMailSettings local)
    {
        await Gate.WaitAsync();
        try
        {
            if (File.Exists(path)) return false;
            Write(Clone(local));
            return true;
        }
        finally { Gate.Release(); }
    }

    private void Write(SmsMailSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var bytes = _protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings)));
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    private static SmsMailSettings Clone(SmsMailSettings settings)
        => JsonSerializer.Deserialize<SmsMailSettings>(JsonSerializer.Serialize(settings)) ?? new SmsMailSettings();
}
