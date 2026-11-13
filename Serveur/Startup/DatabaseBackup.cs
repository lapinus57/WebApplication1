using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ChatServeur.Startup;

/// <summary>
/// Sauvegarde quotidienne de chat.db dans data/sauvegardes (chat-AAAA-MM-JJ.db).
/// La copie passe par l'API de sauvegarde SQLite : elle reste cohérente même pendant
/// que le serveur écrit (mode WAL). Seules les <see cref="DefaultKeep"/> dernières sont gardées.
/// </summary>
public static class DatabaseBackup
{
    public const int DefaultKeep = 14;
    private const string Prefix = "chat-";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Crée la sauvegarde du jour si elle n'existe pas encore. Renvoie son chemin, ou null si déjà faite.</summary>
    public static string? RunIfDue(string databasePath, string backupFolder, DateTime today, int keep = DefaultKeep)
    {
        Directory.CreateDirectory(backupFolder);
        var target = Path.Combine(backupFolder, $"{Prefix}{today.ToString(DateFormat, CultureInfo.InvariantCulture)}.db");
        if (File.Exists(target))
            return null;

        var temporary = target + ".tmp";
        File.Delete(temporary);
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        // Le fichier n'apparaît sous son nom final qu'une fois complet.
        File.Move(temporary, target);

        Prune(backupFolder, keep);
        return target;
    }

    /// <summary>Supprime les sauvegardes les plus anciennes au-delà de <paramref name="keep"/>.</summary>
    public static void Prune(string backupFolder, int keep)
    {
        var dated = Directory.EnumerateFiles(backupFolder, Prefix + "*.db")
            .Select(path => (path, date: ParseDate(path)))
            .Where(item => item.date != null)
            .OrderByDescending(item => item.date)
            .Skip(Math.Max(1, keep));
        foreach (var (path, _) in dated)
            File.Delete(path);
    }

    private static DateTime? ParseDate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(Prefix, StringComparison.Ordinal) &&
               DateTime.TryParseExact(name[Prefix.Length..], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
