using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

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

/// <summary>
/// Compare deux bases : tables, colonnes, puis lignes (par clé primaire si possible).
/// Les lignes sont lues en flux ; seules la clé et une empreinte de 16 octets par ligne de la base 2
/// restent en mémoire, plus les valeurs des différences affichées. Une ligne de la base 2 n'est relue
/// que pour afficher le détail d'une différence.
/// </summary>
public static class MdbComparer
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static List<TableResult> Compare(IDatabase db1, IDatabase db2, CompareOptions options, Action<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<TableResult>();
        var tables2 = db2.Tables.ToDictionary(t => t.Name, Names);
        foreach (var t1 in db1.Tables)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(t1.Name);
            if (!tables2.TryGetValue(t1.Name, out var t2))
            {
                results.Add(new TableResult { Name = t1.Name, Status = TableStatus.SeulementDans1, Rows1 = db1.CountRows(t1) });
                continue;
            }
            results.Add(CompareTable(db1, t1, db2, t2, options, ct));
        }
        var names1 = db1.Tables.Select(t => t.Name).ToHashSet(Names);
        foreach (var t2 in db2.Tables.Where(t => !names1.Contains(t.Name)))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(t2.Name);
            results.Add(new TableResult { Name = t2.Name, Status = TableStatus.SeulementDans2, Rows2 = db2.CountRows(t2) });
        }
        return results.OrderBy(r => r.Name, Names).ToList();
    }

    public static TableResult CompareTable(IDatabase db1, TableSchema t1, IDatabase db2, TableSchema t2, CompareOptions options, CancellationToken ct = default)
    {
        var result = new TableResult { Name = t1.Name };

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
        var common = t1.Columns.Where(c => cols2.ContainsKey(c.Name)).Select(c => c.Name).ToList();
        if (!options.CompareData || common.Count == 0)
        {
            result.Rows1 = db1.CountRows(t1);
            result.Rows2 = db2.CountRows(t2);
        }
        else
        {
            var source1 = new Source(db1, t1, common, options, ct);
            var source2 = new Source(db2, t2, common.Select(n => cols2[n].Name).ToList(), options, ct);
            var key = t1.PrimaryKey.Count > 0 && t1.PrimaryKey.SequenceEqual(t2.PrimaryKey, Names)
                ? t1.PrimaryKey.Select(k => common.FindIndex(n => Names.Equals(n, k))).ToArray()
                : Array.Empty<int>();
            bool done = false;
            if (key.Length > 0 && key.All(p => p >= 0))
            {
                result.ComparedBy = "clé primaire (" + Describe(t1.PrimaryKey) + ")";
                done = TryCompareByKey(result, common, source1, source2, key, options);
            }
            if (!done)
            {
                Reset(result);
                result.ComparedBy = key.Length > 0 ? "ligne entière (clé primaire en double)" : "ligne entière (pas de clé primaire commune)";
                CompareWholeRows(result, common, source1, source2, options);
            }
        }

        if (result.DifferenceCount > result.Details.Count) result.DetailsTruncated = true;
        if (result.SchemaNotes.Count > 0 || result.DifferenceCount > 0) result.Status = TableStatus.Differente;
        return result;
    }

    private sealed record Source(IDatabase Db, TableSchema Table, IReadOnlyList<string> Columns, CompareOptions Options, CancellationToken Ct)
    {
        public IEnumerable<string?[]> Rows() => Db.ReadRows(Table, Columns, Ct)
            .Select(r => r.Select(v => Normalize(v, Options)).ToArray());
    }

    private struct KeyEntry { public Guid Hash; public bool Seen; }

    // Retourne false si une clé est en double (clé mal décrite par le pilote) : on repasse alors en ligne entière.
    private static bool TryCompareByKey(TableResult result, List<string> columns, Source source1, Source source2, int[] key, CompareOptions options)
    {
        string KeyOf(string?[] row) => string.Join(" | ", key.Select(p => row[p] ?? "(vide)"));

        var map2 = new Dictionary<string, KeyEntry>(StringComparer.Ordinal);
        int rows2 = 0;
        foreach (var r2 in source2.Rows())
        {
            rows2++;
            if (!map2.TryAdd(KeyOf(r2), new KeyEntry { Hash = Hash(r2) })) return false;
        }

        // Lignes modifiées dont il faudra relire la version de la base 2 pour le détail.
        var pending = new Dictionary<string, string?[]>(StringComparer.Ordinal);
        var only1Keys = new HashSet<string>(StringComparer.Ordinal);
        int rows1 = 0, matched = 0;
        foreach (var r1 in source1.Rows())
        {
            rows1++;
            var k = KeyOf(r1);
            ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(map2, k);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref entry))
            {
                if (!only1Keys.Add(k)) return false;
                result.OnlyIn1++;
                AddDetail(result, options, new RowDifference(RowChange.SeulementDans1, k, Row(columns, r1)));
                continue;
            }
            if (entry.Seen) return false;
            entry.Seen = true;
            matched++;
            if (entry.Hash == Hash(r1)) continue;
            result.Modified++;
            if (pending.Count < options.MaxDetailsPerTable) pending[k] = r1;
            else result.DetailsTruncated = true;
        }
        result.Rows1 = rows1;
        result.Rows2 = rows2;
        result.OnlyIn2 = map2.Count - matched;

        if (pending.Count > 0 || result.OnlyIn2 > 0)
            foreach (var r2 in source2.Rows())
            {
                var k = KeyOf(r2);
                if (pending.Remove(k, out var r1))
                {
                    var changes = Enumerable.Range(0, columns.Count).Where(i => r1[i] != r2[i])
                        .Select(i => $"{columns[i]} : {Show(r1[i])} → {Show(r2[i])}");
                    AddDetail(result, options, new RowDifference(RowChange.Modifiee, k, string.Join(" ; ", changes)));
                }
                else if (!map2[k].Seen)
                    AddDetail(result, options, new RowDifference(RowChange.SeulementDans2, k, Row(columns, r2)));
                if (pending.Count == 0 && result.Details.Count >= options.MaxDetailsPerTable) break;
            }
        return true;
    }

    // Sans clé : les lignes sont comparées comme des ensembles (doublons compris), par empreinte.
    private static void CompareWholeRows(TableResult result, List<string> columns, Source source1, Source source2, CompareOptions options)
    {
        var remaining = new Dictionary<Guid, int>();
        int rows2 = 0;
        foreach (var r2 in source2.Rows())
        {
            rows2++;
            CollectionsMarshal.GetValueRefOrAddDefault(remaining, Hash(r2), out _)++;
        }
        int rows1 = 0;
        foreach (var r1 in source1.Rows())
        {
            rows1++;
            ref var count = ref CollectionsMarshal.GetValueRefOrNullRef(remaining, Hash(r1));
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref count) && count > 0) { count--; continue; }
            result.OnlyIn1++;
            AddDetail(result, options, new RowDifference(RowChange.SeulementDans1, "", Row(columns, r1)));
        }
        result.Rows1 = rows1;
        result.Rows2 = rows2;
        result.OnlyIn2 = remaining.Values.Sum();

        if (result.OnlyIn2 > 0)
            foreach (var r2 in source2.Rows())
            {
                ref var count = ref CollectionsMarshal.GetValueRefOrNullRef(remaining, Hash(r2));
                if (count == 0) continue;
                count--;
                AddDetail(result, options, new RowDifference(RowChange.SeulementDans2, "", Row(columns, r2)));
                if (result.Details.Count >= options.MaxDetailsPerTable) break;
            }
    }

    private static void Reset(TableResult result)
    {
        result.OnlyIn1 = result.OnlyIn2 = result.Modified = 0;
        result.Details.Clear();
        result.DetailsTruncated = false;
    }

    /// <summary>Empreinte de 16 octets de la ligne normalisée : seule elle est gardée en mémoire.</summary>
    private static Guid Hash(string?[] row)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in row)
        {
            if (value == null) hash.AppendData(new byte[] { 0 });
            else
            {
                hash.AppendData(new byte[] { 1 });
                hash.AppendData(Encoding.UTF8.GetBytes(value));
                hash.AppendData(BitConverter.GetBytes(value.Length));
            }
        }
        return new Guid(hash.GetHashAndReset().AsSpan(0, 16));
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
