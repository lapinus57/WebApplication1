using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace Client.SmsMail.Configuration
{
    public sealed class SmsMailSettings
    {
        public SmsSettings Sms { get; set; } = new();
        public EmailSettings Email { get; set; } = new();
        public string Cabinet { get; set; } = "Docteur René MULLER";
        public string CabinetAddress { get; set; } = "37 rue de la République, 57320 BOUZONVILLE";
        public int AppointmentDurationMinutes { get; set; } = 30;
        public string CalendarTimeZone { get; set; } = "Europe/Paris";
        public bool SmsOnlyWhenNoEmail { get; set; } = true;
        public string Subject { get; set; } = "Rappel de rendez-vous";
        public string SmsTemplate { get; set; } = "Bonjour [PRENOMS], rendez-vous chez [CABINET] le [DATE_LONGUE] à [HEURE]. Merci de prévenir en cas d'empêchement.";
        public const string LegacyEmailTemplate = "<html><body><p>Bonjour [PRENOMS],</p><p>Votre rendez-vous chez [CABINET] est prévu le [DATE_LONGUE] à [HEURE].</p><ul>[LISTE_PERSONNES]</ul></body></html>";
        public const string DefaultEmailTemplate = """
<div style="max-width: 600px; margin: auto; padding: 20px; border: 1px solid #ccc; border-radius: 10px;">
<h1 style="color: #4CAF50;">Rappel de Rendez-vous</h1>
<p>Bonjour,</p>
<p>Nous vous rappelons votre rendez-vous chez <strong>[CABINET]</strong>.</p>
<p>Voici les détails de votre rendez-vous :</p>
<ul style="list-style-type: none; padding: 0;">
<li><strong>Date :</strong> [DATE_LONGUE]</li>
<li><strong>Heure :</strong> [HEURE]</li>
<li><strong>Lieu :</strong> [ADRESSE_CABINET]</li>
</ul>
<p>Les personnes concernées par ce rendez-vous sont :</p>
<ul style="list-style-type: none; padding: 0;">[LISTE_PERSONNES]</ul>
<p>Cordialement,</p><p>Le cabinet médical</p>
</div>
""";
        public string EmailTemplate { get; set; } = DefaultEmailTemplate;

    }

    [SupportedOSPlatform("windows")]
    public static class SmsMailSettingsStore
    {
        public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "SmsMail");
        private static string FilePath => Path.Combine(DirectoryPath, "settings.dat");

        public static SmsMailSettings Load()
        {
            if (!File.Exists(FilePath)) return new();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            var settings = JsonSerializer.Deserialize<SmsMailSettings>(bytes) ?? throw new InvalidDataException("Paramètres SMS et mail invalides.");
            if (settings.EmailTemplate == SmsMailSettings.LegacyEmailTemplate)
                settings.EmailTemplate = SmsMailSettings.DefaultEmailTemplate;
            return settings;
        }

        public static void Save(SmsMailSettings settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings)), null, DataProtectionScope.CurrentUser);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, FilePath, true);
        }
    }
}
