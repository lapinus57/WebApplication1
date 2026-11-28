using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.ClientUpdate
{
    /// <summary>Ce que le serveur répond à un poste qui annonce sa version.</summary>
    public sealed class ClientUpdateInfo
    {
        /// <summary>Version la plus avancée connue (postes vus, ou paquet trouvé dans le dossier de mise à jour).</summary>
        public string LatestVersion { get; set; } = "";
        /// <summary>Poste qui a cette version (vide si elle vient seulement du dossier).</summary>
        public string LatestMachine { get; set; } = "";
        /// <summary>Dossier des mises à jour (disque réseau, ex. \\NAS\uptEye), réglé sur le serveur.</summary>
        public string UpdateFolder { get; set; } = "";
    }

    /// <summary>Paquet d'installation trouvé dans le dossier de mise à jour.</summary>
    public sealed record ClientPackage(Version Version, string Folder, string? InstallerPath);

    public static class ClientVersions
    {
        private static readonly Regex VersionInName = new(@"_(\d+\.\d+\.\d+\.\d+)(?:_|\.|$)", RegexOptions.CultureInvariant);

        public static Version? Parse(string? text)
            => Version.TryParse((text ?? "").Trim(), out var v) ? Normalize(v) : null;

        private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        /// <summary>Vrai si <paramref name="candidate"/> est plus récente que <paramref name="current"/>.</summary>
        public static bool IsNewer(string? candidate, string? current)
            => Parse(candidate) is { } c && Parse(current) is { } m && c > m;

        public static string Max(IEnumerable<string> versions)
            => versions.Select(v => (Text: v, Version: Parse(v))).Where(x => x.Version != null).OrderByDescending(x => x.Version).Select(x => x.Version!.ToString()).FirstOrDefault() ?? "";

        /// <summary>
        /// Le paquet le plus récent du dossier de mise à jour : sous-dossiers « Client_0.1.38.0_Test » (sortie de Visual Studio)
        /// ou fichiers « Client_0.1.38.0_x64.msixbundle » posés directement. Null si le dossier est vide ou inaccessible.
        /// </summary>
        public static ClientPackage? FindLatest(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            try
            {
                if (!Directory.Exists(folder)) return null;
                var candidates = new List<ClientPackage>();
                foreach (var dir in Directory.GetDirectories(folder))
                    if (VersionFrom(Path.GetFileName(dir)) is { } v)
                        candidates.Add(new ClientPackage(v, dir, Installer(dir)));
                foreach (var file in Directory.GetFiles(folder).Where(IsInstaller))
                    if (VersionFrom(Path.GetFileName(file)) is { } v)
                        candidates.Add(new ClientPackage(v, folder, file));
                return candidates.OrderByDescending(c => c.Version).ThenByDescending(c => c.InstallerPath != null).FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
        }

        private static Version? VersionFrom(string name)
            => VersionInName.Match(name) is { Success: true } m && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;

        private static bool IsInstaller(string path)
            => path.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".appx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Le paquet à ouvrir dans un dossier de version : le .msixbundle d'abord, sinon un .msix.</summary>
        private static string? Installer(string dir)
        {
            var files = Directory.GetFiles(dir).Where(IsInstaller).ToList();
            return files.OrderBy(f => f.EndsWith("bundle", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
    }
}
