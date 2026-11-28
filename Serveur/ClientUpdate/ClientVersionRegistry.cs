using System.Text.Json;
using EyeChat.ClientUpdate;

namespace ChatServeur.ClientUpdate;

public sealed class ClientVersionEntry
{
    public string Machine { get; set; } = "";
    public string User { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>
/// Versions des postes EyeChat (data\client-versions.json) et dossier des mises à jour (disque réseau, ex. \\NAS\uptEye).
/// Le serveur compare les versions : dès qu'un poste plus récent se connecte, les postes plus anciens sont avertis.
/// </summary>
public sealed class ClientVersionRegistry(string folder)
{
    /// <summary>Un poste qui ne s'est pas connecté depuis ce délai ne compte plus pour « la version la plus avancée ».</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

    private readonly object _gate = new();
    private string VersionsPath => Path.Combine(folder, "client-versions.json");
    private string SettingsPath => Path.Combine(folder, "client-update.json");

    private sealed class UpdateSettings { public string UpdateFolder { get; set; } = ""; }

    public string UpdateFolder
    {
        get { lock (_gate) return Read<UpdateSettings>(SettingsPath)?.UpdateFolder ?? ""; }
        set { lock (_gate) Write(SettingsPath, new UpdateSettings { UpdateFolder = (value ?? "").Trim() }); }
    }

    public List<ClientVersionEntry> Entries()
    {
        lock (_gate) return (Read<List<ClientVersionEntry>>(VersionsPath) ?? new()).OrderByDescending(e => ClientVersions.Parse(e.Version)).ThenBy(e => e.Machine).ToList();
    }

    /// <summary>Note la version d'un poste ; renvoie la version la plus avancée d'avant (pour savoir si celle-ci est nouvelle).</summary>
    public string Record(string machine, string user, string version, DateTime nowUtc)
    {
        lock (_gate)
        {
            var entries = Read<List<ClientVersionEntry>>(VersionsPath) ?? new();
            var before = Latest(entries, nowUtc).Version;
            var key = string.IsNullOrWhiteSpace(machine) ? "(inconnu)" : machine.Trim();
            var entry = entries.FirstOrDefault(e => string.Equals(e.Machine, key, StringComparison.OrdinalIgnoreCase));
            if (entry == null) entries.Add(entry = new ClientVersionEntry { Machine = key });
            entry.User = user ?? ""; entry.Version = ClientVersions.Parse(version)?.ToString() ?? ""; entry.LastSeenUtc = nowUtc;
            Write(VersionsPath, entries);
            return before;
        }
    }

    /// <summary>La version la plus avancée : postes vus récemment, ou paquet du dossier de mise à jour si le serveur peut le lire.</summary>
    public ClientUpdateInfo Info(DateTime nowUtc)
    {
        List<ClientVersionEntry> entries;
        lock (_gate) entries = Read<List<ClientVersionEntry>>(VersionsPath) ?? new();
        var (version, machine) = Latest(entries, nowUtc);
        var update = UpdateFolder;
        if (ClientVersions.FindLatest(update) is { } package && ClientVersions.IsNewer(package.Version.ToString(), version.Length > 0 ? version : "0.0.0.0"))
            (version, machine) = (package.Version.ToString(), "");
        return new ClientUpdateInfo { LatestVersion = version, LatestMachine = machine, UpdateFolder = update };
    }

    private static (string Version, string Machine) Latest(List<ClientVersionEntry> entries, DateTime nowUtc)
    {
        var best = entries.Where(e => nowUtc - e.LastSeenUtc <= KeepFor && ClientVersions.Parse(e.Version) != null)
            .OrderByDescending(e => ClientVersions.Parse(e.Version)).FirstOrDefault();
        return best == null ? ("", "") : (best.Version, best.Machine);
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
