namespace ChatServeur
{
    /// <summary>
    /// Avatars importés par les postes : rangés dans data\avatars, à côté des bases, et servis à l'adresse /avatars/….
    /// Avant, ils étaient écrits dans wwwroot\avatars : si wwwroot manquait au démarrage (serveur lancé depuis bin\…),
    /// le fichier était bien écrit mais le serveur web ne le servait pas (404) et l'avatar n'apparaissait jamais.
    /// Les anciens avatars de wwwroot\avatars restent proposés et servis.
    /// </summary>
    public static class AvatarStorage
    {
        public const string RequestPath = "/avatars";
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

        public static string Folder(string baseDirectory) => Path.Combine(baseDirectory, "data", "avatars");

        public static async Task<string> SaveAsync(string folder, byte[] bytes, string extension)
        {
            Directory.CreateDirectory(folder);
            var safeName = Path.ChangeExtension(Path.GetRandomFileName(), extension);
            await File.WriteAllBytesAsync(Path.Combine(folder, safeName), bytes);
            return $"{RequestPath}/{safeName}";
        }

        /// <summary>
        /// Recopie dans data\avatars les avatars importés par les anciennes versions dans &lt;exe&gt;\wwwroot\avatars
        /// (y compris quand ce dossier avait été créé sans le reste de wwwroot et n'était donc jamais servi).
        /// </summary>
        public static int ImportLegacy(string baseDirectory)
        {
            var legacy = Path.Combine(baseDirectory, "wwwroot", "avatars");
            if (!Directory.Exists(legacy)) return 0;
            var folder = Folder(baseDirectory);
            Directory.CreateDirectory(folder);
            int copied = 0;
            foreach (var file in Directory.GetFiles(legacy).Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                var target = Path.Combine(folder, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                File.Copy(file, target);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
                copied++;
            }
            return copied;
        }

        /// <summary>Avatars disponibles : ceux de data\avatars puis les anciens de wwwroot\avatars, sans doublon.</summary>
        public static List<string> List(string folder, string? webRoot)
        {
            var folders = new[] { folder, string.IsNullOrEmpty(webRoot) ? null : Path.Combine(webRoot, "avatars") };
            return folders
                .Where(f => f != null && Directory.Exists(f))
                .SelectMany(f => Directory.GetFiles(f!).OrderBy(File.GetLastWriteTimeUtc))
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => $"{RequestPath}/{Path.GetFileName(f)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
