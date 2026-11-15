using System.Globalization;
using System.Security.Cryptography;

namespace MdbCompare;

public enum TableStatus { Identique, SeulementDans1, SeulementDans2, Differente }
public enum RowChange { SeulementDans1, SeulementDans2, Modifiee }

public sealed record RowDifference(RowChange Kind, string Key, string Detail);

public sealed class TableResult
{
    public required string Name { get; init; }
    public TableStatus Status { get; set; } = TableStatus.Identique;
    public List<string> SchemaNotes { get; } = new();
    public int Rows1 { get; set; }
    public int Rows2 { get; set; }
    public int OnlyIn1 { get; set; }
    public int OnlyIn2 { get; set; }
    public int Modified { get; set; }
    /// <summary>Détail des lignes différentes, limité à <see cref="CompareOptions.MaxDetailsPerTable"/>.</summary>
    public List<RowDifference> Details { get; } = new();
    public bool DetailsTruncated { get; set; }
    public string ComparedBy { get; set; } = "";
    public int DifferenceCount => OnlyIn1 + OnlyIn2 + Modified;
}

public sealed record CompareOptions(bool CompareData = true, bool IgnoreCaseAndTrailingSpaces = false, int MaxDetailsPerTable = 1000);

/// <summary>Compare deux bases déjà lues : tables, colonnes, puis lignes (par clé primaire si possible).</summary>
public static class MdbComparer
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static List<TableResult> Compare(DatabaseSnapshot db1, DatabaseSnapshot db2, CompareOptions options)
    {
        var results = new List<TableResult>();
        var tables2 = db2.Tables.ToDictionary(t => t.Name, Names);
        foreach (var t1 in db1.Tables)
        {
            if (!tables2.TryGetValue(t1.Name, out var t2))
            {
                results.Add(new TableResult { Name = t1.Name, Status = TableStatus.SeulementDans1, Rows1 = t1.Rows.Count });
                continue;
            }
            results.Add(CompareTable(t1, t2, options));
        }
        var names1 = db1.Tables.Select(t => t.Name).ToHashSet(Names);
        foreach (var t2 in db2.Tables.Where(t => !names1.Contains(t.Name)))
            results.Add(new TableResult { Name = t2.Name, Status = TableStatus.SeulementDans2, Rows2 = t2.Rows.Count });
        return results.OrderBy(r => r.Name, Names).ToList();
    }

    public static TableResult CompareTable(TableData t1, TableData t2, CompareOptions options)
    {
        var result = new TableResult { Name = t1.Name, Rows1 = t1.Rows.Count, Rows2 = t2.Rows.Count };

        // 1. Structure
        var cols2 = t2.Columns.ToDictionary(c => c.Name, Names);
        var cols1 = t1.Columns.ToDictionary(c => c.Name, Names);
        foreach (var c in t1.Columns.Where(c => !cols2.ContainsKey(c.Name)))
            result.SchemaNotes.Add($"Colonne « {c.Name} » seulement dans la base 1 ({c.Type})");
        foreach (var c in t2.Columns.Where(c => !cols1.ContainsKey(c.Name)))
            result.SchemaNotes.Add($"Colonne « {c.Name} » seulement dans la base 2 ({c.Type})");
        foreach (var c in t1.Columns.Where(c => cols2.TryGetValue(c.Name, out var o) && o.Type != c.Type))
            result.SchemaNotes.Add($"Colonne « {c.Name} » : type {c.Type} dans la base 1, {cols2[c.Name].Type} dans la base 2");
        if (!t1.PrimaryKey.SequenceEqual(t2.PrimaryKey, Names))
            result.SchemaNotes.Add($"Clé primaire : ({Describe(t1.PrimaryKey)}) dans la base 1, ({Describe(t2.PrimaryKey)}) dans la base 2");

        // 2. Données, sur les colonnes communes uniquement
        if (options.CompareData && t1.RowsLoaded && t2.RowsLoaded)
        {
            var common = t1.Columns.Where(c => cols2.ContainsKey(c.Name)).Select(c => c.Name).ToList();
            var idx1 = common.Select(n => t1.Columns.FindIndex(c => Names.Equals(c.Name, n))).ToArray();
            var idx2 = common.Select(n => t2.Columns.FindIndex(c => Names.Equals(c.Name, n))).ToArray();
            var rows1 = t1.Rows.Select(r => idx1.Select(i => Normalize(r[i], options)).ToArray()).ToList();
            var rows2 = t2.Rows.Select(r => idx2.Select(i => Normalize(r[i], options)).ToArray()).ToList();

            var keyPositions = t1.PrimaryKey.Count > 0 && t1.PrimaryKey.SequenceEqual(t2.PrimaryKey, Names)
                ? t1.PrimaryKey.Select(k => common.FindIndex(n => Names.Equals(n, k))).ToArray()
                : Array.Empty<int>();
            if (keyPositions.Length > 0 && keyPositions.All(p => p >= 0)
                && IsUnique(rows1, keyPositions) && IsUnique(rows2, keyPositions))
            {
                result.ComparedBy = "clé primaire (" + Describe(t1.PrimaryKey) + ")";
                CompareByKey(result, common, rows1, rows2, keyPositions, options);
            }
            else
            {
                result.ComparedBy = "ligne entière (pas de clé primaire commune)";
                CompareWholeRows(result, common, rows1, rows2, options);
            }
        }

        if (result.SchemaNotes.Count > 0 || result.DifferenceCount > 0) result.Status = TableStatus.Differente;
        return result;
    }

    private static void CompareByKey(TableResult result, List<string> columns, List<string?[]> rows1, List<string?[]> rows2, int[] key, CompareOptions options)
    {
        string KeyOf(string?[] row) => string.Join(" | ", key.Select(p => row[p] ?? "(vide)"));
        var map2 = rows2.ToDictionary(r => KeyOf(r), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r1 in rows1)
        {
            var k = KeyOf(r1);
            seen.Add(k);
            if (!map2.TryGetValue(k, out var r2))
            {
                result.OnlyIn1++;
                AddDetail(result, options, new RowDifference(RowChange.SeulementDans1, k, Row(columns, r1)));
                continue;
            }
            var changes = Enumerable.Range(0, columns.Count).Where(i => r1[i] != r2[i])
                .Select(i => $"{columns[i]} : {Show(r1[i])} → {Show(r2[i])}").ToList();
            if (changes.Count == 0) continue;
            result.Modified++;
            AddDetail(result, options, new RowDifference(RowChange.Modifiee, k, string.Join(" ; ", changes)));
        }
        foreach (var r2 in rows2)
        {
            var k = KeyOf(r2);
            if (seen.Contains(k)) continue;
            result.OnlyIn2++;
            AddDetail(result, options, new RowDifference(RowChange.SeulementDans2, k, Row(columns, r2)));
        }
    }

    // Sans clé : on compare les lignes comme des ensembles (doublons compris).
    private static void CompareWholeRows(TableResult result, List<string> columns, List<string?[]> rows1, List<string?[]> rows2, CompareOptions options)
    {
        static string Signature(string?[] row) => string.Join("\u001F", row.Select(v => v == null ? "\u0000" : v));
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r2 in rows2)
        {
            var s = Signature(r2);
            remaining[s] = remaining.GetValueOrDefault(s) + 1;
        }
        var unmatched1 = new List<string?[]>();
        foreach (var r1 in rows1)
        {
            var s = Signature(r1);
            if (remaining.GetValueOrDefault(s) > 0) remaining[s]--;
            else unmatched1.Add(r1);
        }
        foreach (var r1 in unmatched1)
        {
            result.OnlyIn1++;
            AddDetail(result, options, new RowDifference(RowChange.SeulementDans1, "", Row(columns, r1)));
        }
        foreach (var r2 in rows2)
        {
            var s = Signature(r2);
            if (remaining.GetValueOrDefault(s) == 0) continue;
            remaining[s]--;
            result.OnlyIn2++;
            AddDetail(result, options, new RowDifference(RowChange.SeulementDans2, "", Row(columns, r2)));
        }
    }

    private static bool IsUnique(List<string?[]> rows, int[] key)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        return rows.All(r => set.Add(string.Join("\u001F", key.Select(p => r[p] ?? "\u0000"))));
    }

    private static void AddDetail(TableResult result, CompareOptions options, RowDifference difference)
    {
        if (result.Details.Count < options.MaxDetailsPerTable) result.Details.Add(difference);
        else result.DetailsTruncated = true;
    }

    private static string Row(List<string> columns, string?[] row) =>
        string.Join(" ; ", columns.Select((c, i) => $"{c} = {Show(row[i])}"));

    private static string Show(string? value) => value == null ? "(vide)" : "« " + (value.Length > 200 ? value[..200] + "…" : value) + " »";

    private static string Describe(List<string> key) => key.Count == 0 ? "aucune" : string.Join(", ", key);

    /// <summary>Convertit une valeur Access en texte stable pour la comparaison (null reste null).</summary>
    public static string? Normalize(object? value, CompareOptions options)
    {
        string? text = value switch
        {
            null or DBNull => null,
            DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            byte[] bytes => $"[binaire {bytes.Length} octets, {Convert.ToHexString(SHA256.HashData(bytes))[..12]}]",
            double x => x.ToString("R", CultureInfo.InvariantCulture),
            float x => x.ToString("R", CultureInfo.InvariantCulture),
            decimal x => x.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "Vrai" : "Faux",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        if (text != null && options.IgnoreCaseAndTrailingSpaces) text = text.TrimEnd().ToUpperInvariant();
        return text;
    }
}
