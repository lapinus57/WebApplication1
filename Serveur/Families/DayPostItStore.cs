using System.Globalization;
using EyeChat.SmsMail;
using Microsoft.Data.Sqlite;

namespace ChatServeur.Families;

/// <summary>
/// Post-it EyeChat du jour, un par famille (data\day-postits.db). La famille est figée à la première saisie : un recalcul des
/// familles dans la journée ne déplace pas le post-it. Utile le jour même seulement : les post-it de plus de 30 jours sont effacés.
/// </summary>
public sealed class DayPostItStore
{
    public const int KeepDays = 30;
    private readonly string _connectionString;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public DayPostItStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS day_postit (id INTEGER PRIMARY KEY AUTOINCREMENT, day TEXT NOT NULL, text TEXT NOT NULL, color TEXT NOT NULL,
                updated_by TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS day_postit_member (postit INTEGER NOT NULL, patient_id INTEGER NOT NULL, day TEXT NOT NULL, name TEXT NOT NULL,
                PRIMARY KEY (day, patient_id));
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Le post-it du jour qui contient ce patient, ou null.</summary>
    public DayPostIt? Find(int patientId, DateTime day)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.text, p.color, p.updated_by, p.updated_utc FROM day_postit_member m JOIN day_postit p ON p.id = m.postit
            WHERE m.day = $d AND m.patient_id = $p
            """;
        cmd.Parameters.AddWithValue("$d", D(day));
        cmd.Parameters.AddWithValue("$p", patientId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var id = r.GetInt64(0);
        var note = new DayPostIt
        {
            PatientId = patientId, Day = day.Date, Text = r.GetString(1), Color = r.GetString(2), UpdatedBy = r.GetString(3),
            UpdatedAt = DateTime.TryParse(r.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var u) ? u.ToLocalTime() : null,
        };
        r.Close();
        using var m = c.CreateCommand();
        m.CommandText = "SELECT patient_id, name FROM day_postit_member WHERE postit = $id ORDER BY patient_id";
        m.Parameters.AddWithValue("$id", id);
        using var mr = m.ExecuteReader();
        while (mr.Read()) note.Members.Add(new DayPostItMember { PatientId = mr.GetInt32(0), Name = mr.GetString(1) });
        return note;
    }

    /// <summary>
    /// Retire un patient du post-it du jour (famille mal regroupée, ou séparée dans la journée) : il aura le sien, vide.
    /// Renvoie les patients qui partageaient ce post-it (pour prévenir les postes), vide s'il n'y en avait pas.
    /// </summary>
    public async Task<List<int>> DetachAsync(int patientId, DateTime day)
    {
        await Gate.WaitAsync();
        try
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            var members = new List<int>();
            using (var list = c.CreateCommand())
            {
                list.Transaction = t;
                list.CommandText = "SELECT patient_id FROM day_postit_member WHERE postit = (SELECT postit FROM day_postit_member WHERE day = $d AND patient_id = $p)";
                list.Parameters.AddWithValue("$d", D(day)); list.Parameters.AddWithValue("$p", patientId);
                using var r = list.ExecuteReader();
                while (r.Read()) members.Add(r.GetInt32(0));
            }
            using (var del = c.CreateCommand())
            {
                del.Transaction = t;
                del.CommandText = """
                    DELETE FROM day_postit_member WHERE day = $d AND patient_id = $p;
                    DELETE FROM day_postit WHERE day = $d AND id NOT IN (SELECT postit FROM day_postit_member WHERE day = $d);
                    """;
                del.Parameters.AddWithValue("$d", D(day)); del.Parameters.AddWithValue("$p", patientId);
                del.ExecuteNonQuery();
            }
            t.Commit();
            return members;
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// Enregistre le texte du post-it du jour de ce patient : met à jour celui qui le contient déjà, sinon en crée un pour
    /// <paramref name="family"/> (les membres déjà dans un autre post-it ce jour-là n'y sont pas ajoutés). Texte vide : supprimé.
    /// Renvoie les patients concernés (pour prévenir les autres postes).
    /// </summary>
    public async Task<List<int>> SaveAsync(int patientId, DateTime day, string text, string color, string author, IReadOnlyList<DayPostItMember> family)
    {
        await Gate.WaitAsync();
        try
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            long? id = null;
            using (var find = c.CreateCommand())
            {
                find.Transaction = t;
                find.CommandText = "SELECT postit FROM day_postit_member WHERE day = $d AND patient_id = $p";
                find.Parameters.AddWithValue("$d", D(day)); find.Parameters.AddWithValue("$p", patientId);
                if (find.ExecuteScalar() is long found) id = found;
            }
            var members = new List<int>();
            if (id is { } existing)
            {
                using var list = c.CreateCommand();
                list.Transaction = t;
                list.CommandText = "SELECT patient_id FROM day_postit_member WHERE postit = $id";
                list.Parameters.AddWithValue("$id", existing);
                using (var r = list.ExecuteReader()) while (r.Read()) members.Add(r.GetInt32(0));
                using var cmd = c.CreateCommand();
                cmd.Transaction = t;
                if (string.IsNullOrWhiteSpace(text))
                    cmd.CommandText = "DELETE FROM day_postit_member WHERE postit = $id; DELETE FROM day_postit WHERE id = $id;";
                else
                {
                    cmd.CommandText = "UPDATE day_postit SET text = $text, color = $color, updated_by = $by, updated_utc = $u WHERE id = $id";
                    cmd.Parameters.AddWithValue("$text", text); cmd.Parameters.AddWithValue("$color", color);
                    cmd.Parameters.AddWithValue("$by", author); cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                }
                cmd.Parameters.AddWithValue("$id", existing);
                cmd.ExecuteNonQuery();
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                using (var ins = c.CreateCommand())
                {
                    ins.Transaction = t;
                    ins.CommandText = "INSERT INTO day_postit(day, text, color, updated_by, updated_utc) VALUES($d, $text, $color, $by, $u); SELECT last_insert_rowid();";
                    ins.Parameters.AddWithValue("$d", D(day)); ins.Parameters.AddWithValue("$text", text); ins.Parameters.AddWithValue("$color", color);
                    ins.Parameters.AddWithValue("$by", author); ins.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    id = (long)ins.ExecuteScalar()!;
                }
                var people = family.Where(f => f.PatientId > 0).GroupBy(f => f.PatientId).Select(g => g.First()).ToList();
                if (people.All(p => p.PatientId != patientId)) people.Insert(0, new DayPostItMember { PatientId = patientId, Name = patientId.ToString(CultureInfo.InvariantCulture) });
                foreach (var person in people)
                {
                    using var add = c.CreateCommand();
                    add.Transaction = t;
                    add.CommandText = "INSERT OR IGNORE INTO day_postit_member(postit, patient_id, day, name) VALUES($id, $p, $d, $n)";
                    add.Parameters.AddWithValue("$id", id); add.Parameters.AddWithValue("$p", person.PatientId);
                    add.Parameters.AddWithValue("$d", D(day)); add.Parameters.AddWithValue("$n", person.Name);
                    if (add.ExecuteNonQuery() == 1) members.Add(person.PatientId);
                }
            }
            using (var purge = c.CreateCommand())
            {
                purge.Transaction = t;
                purge.CommandText = "DELETE FROM day_postit_member WHERE day < $old; DELETE FROM day_postit WHERE day < $old;";
                purge.Parameters.AddWithValue("$old", D(DateTime.Today.AddDays(-KeepDays)));
                purge.ExecuteNonQuery();
            }
            t.Commit();
            return members;
        }
        finally { Gate.Release(); }
    }
}
