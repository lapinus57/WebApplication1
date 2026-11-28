using System;
using System.IO;
using System.Text.Json;
using Client.SmsMail.Configuration;

namespace Client.SmsMail.Services
{
    /// <summary>
    /// Fichier de configuration SMS et mail (.eyeconfig) : tous les réglages, clés OVH et mot de passe SMTP compris,
    /// chiffrés en AES-256-GCM avec un mot de passe choisi à l'export (au moins 10 caractères). Sert à centraliser les
    /// réglages d'un poste sur le serveur, ou à sauvegarder ceux du serveur.
    /// </summary>
    public static class SmsMailConfigFile
    {
        public const string Format = "eyechat-sms-config";
        public const string Extension = ".eyeconfig";

        public static byte[] Export(SmsMailSettings settings, string password)
            => DiagnosticPackage.Encrypt(JsonSerializer.Serialize(settings), password, Format);

        public static SmsMailSettings Import(byte[] file, string password)
        {
            string format;
            try
            {
                using var document = JsonDocument.Parse(file);
                format = document.RootElement.GetProperty("format").GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            {
                throw new InvalidDataException("Ce fichier n'est pas une configuration SMS et mail EyeChat (.eyeconfig).");
            }
            if (format != Format) throw new InvalidDataException("Ce fichier n'est pas une configuration SMS et mail EyeChat (.eyeconfig).");
            var json = DiagnosticPackage.Decrypt(file, password);
            return JsonSerializer.Deserialize<SmsMailSettings>(json) ?? throw new InvalidDataException("Configuration SMS et mail illisible.");
        }

        /// <summary>Ce que contient le fichier, sans aucun secret, pour le montrer avant ou après l'import.</summary>
        public static string Describe(SmsMailSettings s)
            => $"cabinet « {s.Cabinet} », OVH « {(s.Sms.ServiceName.Length > 0 ? s.Sms.ServiceName : "non renseigné")} »"
               + $"{(s.Sms.ApplicationSecret.Length > 0 && s.Sms.ConsumerKey.Length > 0 ? " (clés incluses)" : " (sans clés)")}, "
               + $"SMTP « {(s.Email.SmtpHost.Length > 0 ? s.Email.SmtpHost + ":" + s.Email.SmtpPort : "non renseigné")} »"
               + $"{(s.Email.Password.Length > 0 ? " (mot de passe inclus)" : " (sans mot de passe)")}";
    }
}
