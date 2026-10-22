using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Collections.Generic;
using Client.SmsMail.Models;

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
        public bool AdjustReminderTimes { get; set; }
        public List<AppointmentTimeRule> ReminderTimeRules { get; set; } = new() { new(), new() { From = new TimeSpan(18, 0, 0), To = new TimeSpan(17, 50, 0) } };
        public bool UseEyeEmailFamilies { get; set; } = true;
        public string FamilyDatabasePath { get; set; } = string.Empty;
        public bool SendCalendarInvites { get; set; } = true;
        public bool SendCalendarAlarms { get; set; } = true;
        public bool CalendarIsBusy { get; set; } = true;
        public string CalendarSubject { get; set; } = "RDV Ophtalmologique";
        public List<AlertConfiguration> Alerts { get; set; } = new();
        public bool SmsOnlyWhenNoEmail { get; set; } = true;
        public string Subject { get; set; } = "Rappel de rendez-vous";
        public string SmsTemplate { get; set; } = "Bonjour [PRENOMS], rendez-vous chez [CABINET] le [DATE_LONGUE] à [HEURE]. Merci de prévenir en cas d'empêchement.";
        public const string LegacyEmailTemplate = "<html><body><p>Bonjour [PRENOMS],</p><p>Votre rendez-vous chez [CABINET] est prévu le [DATE_LONGUE] à [HEURE].</p><ul>[LISTE_PERSONNES]</ul></body></html>";
        public const string PreviousEmailTemplate = """
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
        public const string DefaultEmailTemplate = """
<div lang="fr" style="margin:0;padding:24px 12px;background-color:#f3f6f5;font-family:Arial,Helvetica,sans-serif;color:#243b36;line-height:1.6;">
<table role="presentation" style="width:100%;max-width:600px;margin:auto;border-collapse:separate;border-spacing:0;background-color:#ffffff;border:1px solid #dce8e1;border-radius:16px;overflow:hidden;"><tr><td style="padding:28px 24px;background-color:#edf7f0;border-top:5px solid #4CAF50;">
<p style="margin:0 0 8px;font-size:12px;font-weight:bold;letter-spacing:2px;color:#326849;">VOTRE CABINET MÉDICAL</p>
<h1 style="margin:0;font-size:26px;line-height:1.25;color:#245b3e;">Rappel de Rendez-vous</h1>
<p style="margin:10px 0 0;color:#426052;">[CABINET]</p>
</td></tr><tr><td style="padding:24px;">
<p style="margin:0 0 16px;">Bonjour,</p><p style="margin:0 0 20px;">Nous vous rappelons votre prochain rendez-vous au cabinet.</p>
<table role="presentation" style="width:100%;border-collapse:collapse;background-color:#f6f9f7;"><tr><td style="padding:18px;">
<p style="margin:0 0 4px;font-size:12px;color:#52665c;">DATE ET HEURE</p><p style="margin:0;font-size:19px;font-weight:bold;">[DATE_LONGUE] à [HEURE]</p>
<p style="margin:16px 0 4px;font-size:12px;color:#52665c;">ADRESSE DU CABINET</p><p style="margin:0;">[ADRESSE_CABINET]</p>
</td></tr></table>
<h2 style="margin:24px 0 8px;font-size:17px;">Personnes concernées</h2>
<ul style="margin:0;padding:0;list-style-type:none;">[LISTE_PERSONNES]</ul>
<p style="margin:24px 0 0;">À bientôt,<br><strong>Le cabinet médical</strong></p>
</td></tr></table></div>
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
            if ((settings.EmailTemplate == SmsMailSettings.LegacyEmailTemplate || settings.EmailTemplate == SmsMailSettings.PreviousEmailTemplate))
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

