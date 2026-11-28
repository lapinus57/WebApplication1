using System;
using System.IO;
using System.Text;

namespace Client.SmsMail.Services
{
    /// <summary>
    /// Fichier STUDIOV.INI de StudioV : à chaque ouverture d'un dossier, StudioV y écrit le numéro du patient
    /// (<c>[Echange] NumPat=…</c>). EyeChat le lit seulement, jamais il ne l'écrit.
    /// </summary>
    public static class StudioIni
    {
        public const string DefaultPath = @"C:\Studiov2000\STUDIOV.INI";

        /// <summary>Numéro du patient ouvert (<c>NumPat</c> de la section <c>[Echange]</c>), ou null.</summary>
        public static int? OpenPatient(string content) => Value(content, "NumPat");

        /// <summary>Numéro de la consultation ouverte (<c>NumConsult</c>), ou null.</summary>
        public static int? OpenConsultation(string content) => Value(content, "NumConsult");

        /// <summary>
        /// Une consultation est « créée » quand son numéro dépasse tous ceux déjà vus sur ce poste : rouvrir un ancien dossier
        /// redonne un numéro plus ancien. Renvoie aussi le plus grand numéro vu, à garder pour la fois suivante.
        /// </summary>
        public static (bool IsNew, long MaxSeen) NewConsultation(int? consultation, long maxSeen)
            => consultation is int c && c > maxSeen ? (maxSeen > 0, c) : (false, maxSeen);

        private static int? Value(string content, string key)
        {
            var inExchange = false;
            foreach (var raw in (content ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inExchange = line.Equals("[Echange]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inExchange) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0 || !line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                return int.TryParse(line[(eq + 1)..].Trim(), out var id) && id > 0 ? id : null;
            }
            return null;
        }

        /// <summary>Lit le fichier sans gêner StudioV (partage lecture/écriture), en ANSI comme StudioV l'écrit.</summary>
        public static string Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
