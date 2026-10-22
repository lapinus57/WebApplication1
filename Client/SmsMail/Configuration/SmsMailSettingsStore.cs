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
        public string Cabinet { get; set; } = string.Empty;
        public bool SmsOnlyWhenNoEmail { get; set; } = true;
        public string Subject { get; set; } = "Rappel de rendez-vous";
        public string SmsTemplate { get; set; } = "Bonjour [PRENOMS], rendez-vous chez [CABINET] le [DATE_LONGUE] à [HEURE]. Merci de prévenir en cas d'empêchement.";
        public string EmailTemplate { get; set; } = "<html><body><p>Bonjour [PRENOMS],</p><p>Votre rendez-vous chez [CABINET] est prévu le [DATE_LONGUE] à [HEURE].</p><ul>[LISTE_PERSONNES]</ul></body></html>";
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
            return JsonSerializer.Deserialize<SmsMailSettings>(bytes) ?? throw new InvalidDataException("Paramètres SMS et mail invalides.");
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
