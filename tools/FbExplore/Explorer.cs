using System.Data.Common;
using System.Globalization;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

namespace FbExplore;

public sealed class ExploreOptions
{
    public string Database { get; set; } = "";
    public string? Server { get; set; }
    public int Port { get; set; } = 3050;
    public string User { get; set; } = "SYSDBA";
    public string Password { get; set; } = "masterkey";
    /// <summary>Chemin de fbclient.dll (Firebird 3+) ou fbembed.dll (2.5) : ouverture sans serveur.</summary>
    public string? ClientLibrary { get; set; }
    public string Charset { get; set; } = "NONE";
    public string? Output { get; set; }

    public static ExploreOptions Parse(string[] args)
    {
        var o = new ExploreOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Valeur manquante après {args[i]}.");
            switch (args[i].ToLowerInvariant())
            {
                case "--serveur": o.Server = Next(); break;
                case "--port": o.Port = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--utilisateur": o.User = Next(); break;
                case "--motdepasse": o.Password = Next(); break;
                case "--client": o.ClientLibrary = Next(); break;
                case "--charset": o.Charset = Next(); break;
                case "--sortie": o.Output = Next(); break;
                default:
                    if (args[i].StartsWith("--")) throw new ArgumentException($"Option inconnue : {args[i]}");
                    o.Database = args[i];
                    break;
            }
        }
        return o;
    }

    public string ConnectionString()
    {
        var b = new FbConnectionStringBuilder
        {
            Database = Database, UserID = User, Password = Password, Charset = Charset, Pooling = false,
            ServerType = ClientLibrary != null ? FbServerType.Embedded : FbServerType.Default
        };
        if (ClientLibrary != null) b.ClientLibrary = ClientLibrary;
        else { b.DataSource = Server ?? "localhost"; b.Port = Port; }
        return b.ToString();
    }
}

public sealed record ColumnInfo(string Name, string Type, bool Nullable);
public sealed record TableInfo(string Name, long Rows, IReadOnlyList<ColumnInfo> Columns, bool IsView);

public static class Explorer
{
    public static (int Ods, string Firebird)? LastDiskVersion { get; set; }

    /// <summary>
    /// Version de Firebird qui a créé le fichier, lue dans l'en-tête (structure sur disque, « ODS »), sans l'ouvrir :
    /// 11 = Firebird 2.x, 12 = Firebird 3, 13 = Firebird 4 ou 5. Il faut le moteur de la même famille pour l'ouvrir.
    /// </summary>
    public static (int Ods, string Firebird)? DiskVersion(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var header = new byte[0x14];
            if (file.Read(header, 0, header.Length) < header.Length || header[0] != 1) return null; // page 0 = page d'en-tête
            var ods = BitConverter.ToUInt16(header, 0x12) & 0x7FFF;
            return (ods, ods switch { 10 => "Firebird 1.x", 11 => "Firebird 2.x", 12 => "Firebird 3", 13 => "Firebird 4 ou 5", _ => "inconnue" });
        }
        catch (IOException) { return null; }
    }

    // Mots qui signalent une colonne utile pour les familles ou les coordonnées (affichés en tête du rapport).
    public static readonly string[] Interesting =
    {
        "AYANT", "ASSURE", "ASSURÉ", "QUALITE", "QUALITÉ", "RANG", "BENEF", "NIR", "NOSS", "MATRICULE", "IMMAT", "CARTE", "VITALE",
        "NAISS", "NAIS", "TEL", "PORT", "MOBILE", "MAIL", "ADRESSE", "ADR", "VILLE", "CP", "FAMIL", "PARENT", "CONJOINT", "ENFANT",
        "PATIENT", "CODE", "NOM", "PRENOM", "PRÉNOM", "LIEN"
    };

    public static List<TableInfo> Read(FbConnection connection)
    {
        // Lecture seule : uniquement les tables système (RDB$…) et des COUNT(*). Aucune ligne de données n'est lue.
        using var transaction = connection.BeginTransaction(new FbTransactionOptions { TransactionBehavior = FbTransactionBehavior.Read | FbTransactionBehavior.Concurrency });
        var tables = new List<(string Name, bool View)>();
        using (var cmd = new FbCommand("SELECT TRIM(RDB$RELATION_NAME), RDB$VIEW_BLR FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY 1", connection, transaction))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) tables.Add((r.GetString(0), !r.IsDBNull(1)));

        var result = new List<TableInfo>();
        foreach (var (name, view) in tables)
        {
            var columns = new List<ColumnInfo>();
            using (var cmd = new FbCommand(
                "SELECT TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_SCALE, rf.RDB$NULL_FLAG " +
                "FROM RDB$RELATION_FIELDS rf JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
                "WHERE rf.RDB$RELATION_NAME = @t ORDER BY rf.RDB$FIELD_POSITION", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@t", name);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    columns.Add(new ColumnInfo(r.GetString(0), TypeName(Int(r, 1), Int(r, 2), Int(r, 3), Int(r, 4), Int(r, 5)), r.IsDBNull(6)));
            }
            long rows = -1;
            if (!view)
            {
                using var count = new FbCommand($"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\"", connection, transaction);
                try { rows = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture); } catch (FbException) { rows = -1; }
            }
            result.Add(new TableInfo(name, rows, columns, view));
        }
        transaction.Commit();
        return result;
    }

    private static int Int(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    public static string TypeName(int type, int subType, int length, int charLength, int scale) => type switch
    {
        7 => scale < 0 ? $"NUMERIC(4,{-scale})" : "SMALLINT",
        8 => scale < 0 ? $"NUMERIC(9,{-scale})" : "INTEGER",
        16 => scale < 0 ? $"NUMERIC(18,{-scale})" : "BIGINT",
        10 => "FLOAT", 27 => "DOUBLE", 12 => "DATE", 13 => "TIME", 35 => "TIMESTAMP", 23 => "BOOLEAN",
        14 => $"CHAR({(charLength > 0 ? charLength : length)})",
        37 => $"VARCHAR({(charLength > 0 ? charLength : length)})",
        261 => subType == 1 ? "BLOB TEXTE" : "BLOB",
        _ => $"TYPE {type}"
    };

    private static string Rows(long rows) => rows < 0 ? "lignes non comptées" : rows.ToString("N0", CultureInfo.InvariantCulture) + (rows > 1 ? " lignes" : " ligne");

    public static bool IsInteresting(string column)
    {
        var upper = column.ToUpperInvariant();
        return Interesting.Any(upper.Contains);
    }

    /// <summary>Rapport de structure : noms de tables et de colonnes, types, nombres de lignes. Aucune donnée.</summary>
    public static string Report(IReadOnlyList<TableInfo> tables, string databaseName, string serverVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Structure de la base Firebird « {databaseName} » (aucune donnée patient)");
        sb.AppendLine($"Version du moteur : {serverVersion}");
        var real = tables.Where(t => !t.IsView).ToList();
        sb.AppendLine($"{real.Count} tables, {tables.Count - real.Count} vues, {real.Where(t => t.Rows > 0).Sum(t => t.Rows):N0} lignes au total.");
        sb.AppendLine();
        sb.AppendLine("Colonnes probablement utiles (familles, ayants droit, coordonnées) :");
        foreach (var t in tables.Where(t => t.Columns.Any(c => IsInteresting(c.Name))).OrderByDescending(t => t.Rows))
            sb.AppendLine($"  {t.Name} ({(t.IsView ? "vue" : Rows(t.Rows))}) : "
                + string.Join(", ", t.Columns.Where(c => IsInteresting(c.Name)).Select(c => c.Name)));
        sb.AppendLine();
        sb.AppendLine("Détail de toutes les tables :");
        foreach (var t in tables.OrderByDescending(t => t.Rows).ThenBy(t => t.Name))
        {
            sb.AppendLine($"[{t.Name}] {(t.IsView ? "vue" : Rows(t.Rows))}");
            foreach (var c in t.Columns) sb.AppendLine($"    {c.Name,-32} {c.Type}{(c.Nullable ? "" : " NOT NULL")}");
        }
        return sb.ToString();
    }
}
