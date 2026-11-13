using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ChatServeur.Startup;

/// <summary>
/// Sauvegarde quotidienne d'une base (chat.db, eyemail.db) dans data/sauvegardes (nom-AAAA-MM-JJ.db).
/// La copie passe par l'API de sauvegarde SQLite : elle reste cohérente même pendant
/// que le serveur écrit (mode WAL). Seules les <see cref="DefaultKeep"/> dernières sont gardées.
/// </summary>
public static class DatabaseBackup
{
    public const int DefaultKeep = 14;
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Crée la sauvegarde du jour si elle n'existe pas encore. Renvoie son chemin, ou null si déjà faite.</summary>
    public static string? RunIfDue(string databasePath, string backupFolder, DateTime today, int keep = DefaultKeep)
    {
        Directory.CreateDirectory(backupFolder);
        // chat.db → chat-AAAA-MM-JJ.db, eyemail.db → eyemail-AAAA-MM-JJ.db
        var prefix = Path.GetFileNameWithoutExtension(databasePath) + "-";
        var target = Path.Combine(backupFolder, $"{prefix}{today.ToString(DateFormat, CultureInfo.InvariantCulture)}.db");
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

        Prune(backupFolder, prefix, keep);
        return target;
    }

    /// <summary>Supprime les sauvegardes les plus anciennes au-delà de <paramref name="keep"/>.</summary>
    public static void Prune(string backupFolder, string prefix, int keep)
    {
        var dated = Directory.EnumerateFiles(backupFolder, prefix + "*.db")
            .Select(path => (path, date: ParseDate(path, prefix)))
            .Where(item => item.date != null)
            .OrderByDescending(item => item.date)
            .Skip(Math.Max(1, keep));
        foreach (var (path, _) in dated)
            File.Delete(path);
    }

    private static DateTime? ParseDate(string path, string prefix)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               DateTime.TryParseExact(name[prefix.Length..], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
