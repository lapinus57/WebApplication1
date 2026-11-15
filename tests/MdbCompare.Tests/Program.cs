// Vérifie la comparaison de deux bases Access déjà lues (outil tools/MdbCompare). Données synthétiques.
using MdbCompare;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

T Table(string name, string[] key, (string Name, string Type)[] columns, params object?[][] rows) =>
    new(new TableSchema { Name = name, PrimaryKey = key.ToList(), Columns = columns.Select(c => new ColumnInfo(c.Name, c.Type)).ToList() }, rows.ToList());
MemoryDb Db(params T[] tables) => new(tables);
TableResult Cmp(T a, T b, CompareOptions o) => MdbComparer.CompareTable(Db(a), a.Schema, Db(b), b.Schema, o);
var patientCols = new[] { ("Code", "Integer"), ("Nom", "WChar(50)"), ("Naissance", "Date") };
var options = new CompareOptions();

// 1. Tables présentes d'un seul côté, table identique.
var r = MdbComparer.Compare(
    Db(Table("Patients", new[] { "Code" }, patientCols, new object?[] { 1, "MARTIN", new DateTime(1980, 5, 2) }), Table("Ancienne", Array.Empty<string>(), new[] { ("A", "Integer") })),
    Db(Table("patients", new[] { "Code" }, patientCols, new object?[] { 1, "MARTIN", new DateTime(1980, 5, 2) }), Table("Nouvelle", Array.Empty<string>(), new[] { ("A", "Integer") })),
    options);
Check(r.Single(t => t.Name == "Patients").Status == TableStatus.Identique, "Table identique (nom sans tenir compte de la casse)");
Check(r.Single(t => t.Name == "Ancienne").Status == TableStatus.SeulementDans1, "Table seulement dans la base 1");
Check(r.Single(t => t.Name == "Nouvelle").Status == TableStatus.SeulementDans2, "Table seulement dans la base 2");

// 2. Comparaison par clé primaire : ajout, suppression, modification.
var t1 = Table("Patients", new[] { "Code" }, patientCols,
    new object?[] { 1, "MARTIN", new DateTime(1980, 5, 2) }, new object?[] { 2, "DUPONT", null }, new object?[] { 3, "DURAND", null });
var t2 = Table("Patients", new[] { "Code" }, patientCols,
    new object?[] { 1, "MARTIN", new DateTime(1980, 5, 3) }, new object?[] { 3, "DURAND", null }, new object?[] { 4, "PETIT", null });
var byKey = Cmp(t1, t2, options);
Check(byKey.Status == TableStatus.Differente && byKey.ComparedBy.StartsWith("clé primaire"), "Comparaison par clé primaire");
Check(byKey.OnlyIn1 == 1 && byKey.OnlyIn2 == 1 && byKey.Modified == 1, "1 supprimée, 1 ajoutée, 1 modifiée");
var modified = byKey.Details.Single(d => d.Kind == RowChange.Modifiee);
Check(modified.Key == "1" && modified.Detail.Contains("Naissance") && modified.Detail.Contains("1980-05-02") && modified.Detail.Contains("1980-05-03") && !modified.Detail.Contains("Nom"),
    "La modification cite seulement la colonne changée, avec ancienne et nouvelle valeur");
Check(byKey.Details.Single(d => d.Kind == RowChange.SeulementDans1).Key == "2", "Ligne 2 seulement dans la base 1");
Check(byKey.Details.Single(d => d.Kind == RowChange.SeulementDans2).Key == "4", "Ligne 4 seulement dans la base 2");

// 3. Structure : colonne ajoutée, type changé ; les données sont comparées sur les colonnes communes.
var s1 = Table("T", new[] { "Id" }, new[] { ("Id", "Integer"), ("Texte", "WChar(50)") }, new object?[] { 1, "a" });
var s2 = Table("T", new[] { "Id" }, new[] { ("Id", "Integer"), ("Texte", "WChar(100)"), ("Email", "WChar(80)") }, new object?[] { 1, "a", "x@example.fr" });
var schema = Cmp(s1, s2, options);
Check(schema.SchemaNotes.Any(n => n.Contains("Email") && n.Contains("base 2")), "Colonne seulement dans la base 2");
Check(schema.SchemaNotes.Any(n => n.Contains("Texte") && n.Contains("WChar(50)") && n.Contains("WChar(100)")), "Changement de type signalé");
Check(schema.DifferenceCount == 0 && schema.Status == TableStatus.Differente, "Données identiques sur les colonnes communes, table différente par sa structure");

// 4. Sans clé : comparaison ligne entière, doublons compris.
var cols = new[] { ("A", "Integer"), ("B", "WChar(10)") };
var n1 = Table("Journal", Array.Empty<string>(), cols, new object?[] { 1, "x" }, new object?[] { 1, "x" }, new object?[] { 2, "y" });
var n2 = Table("Journal", Array.Empty<string>(), cols, new object?[] { 1, "x" }, new object?[] { 2, "y" }, new object?[] { 3, "z" });
var noKey = Cmp(n1, n2, options);
Check(noKey.ComparedBy.StartsWith("ligne entière") && noKey.OnlyIn1 == 1 && noKey.OnlyIn2 == 1 && noKey.Modified == 0, "Sans clé : un doublon en trop d'un côté, une ligne nouvelle de l'autre");

// 5. Clé non unique (pilote mal renseigné) : repli sur la ligne entière au lieu d'un plantage.
var dup = Table("D", new[] { "A" }, cols, new object?[] { 1, "x" }, new object?[] { 1, "y" });
Check(Cmp(dup, dup, options).ComparedBy.StartsWith("ligne entière"), "Clé en double : repli sans erreur");

// 6. Options : casse/espaces ignorés, structure seule, limite du détail.
var c1 = Table("C", new[] { "A" }, cols, new object?[] { 1, "Metz  " });
var c2 = Table("C", new[] { "A" }, cols, new object?[] { 1, "METZ" });
Check(Cmp(c1, c2, options).Modified == 1, "Casse différente détectée par défaut");
Check(Cmp(c1, c2, options with { IgnoreCaseAndTrailingSpaces = true }).Status == TableStatus.Identique, "Casse et espaces de fin ignorés sur demande");
Check(Cmp(t1, t2, options with { CompareData = false }).Status == TableStatus.Identique, "Structure seule : données non comparées");
var many1 = Table("M", new[] { "A" }, cols, Enumerable.Range(0, 50).Select(i => new object?[] { i, "v" }).ToArray());
var many2 = Table("M", new[] { "A" }, cols);
var limited = Cmp(many1, many2, options with { MaxDetailsPerTable = 10 });
Check(limited.OnlyIn1 == 50 && limited.Details.Count == 10 && limited.DetailsTruncated, "Détail limité mais total exact");
var limited2 = Cmp(many2, many1, options with { MaxDetailsPerTable = 10 });
Check(limited2.OnlyIn2 == 50 && limited2.Details.Count == 10 && limited2.DetailsTruncated, "Détail limité côté base 2");
var noKeyMany = Cmp(Table("M", Array.Empty<string>(), cols), Table("M", Array.Empty<string>(), cols, Enumerable.Range(0, 50).Select(i => new object?[] { i, "v" }).ToArray()), options with { MaxDetailsPerTable = 10 });
Check(noKeyMany.OnlyIn2 == 50 && noKeyMany.Details.Count == 10 && noKeyMany.DetailsTruncated, "Sans clé : détail limité, total exact");

// 7. Normalisation des valeurs Access.
Check(MdbComparer.Normalize(1.5d, options) == "1.5" && MdbComparer.Normalize(true, options) == "Vrai" && MdbComparer.Normalize(DBNull.Value, options) == null, "Nombres, booléens et vides");
Check(MdbComparer.Normalize(new byte[] { 1, 2 }, options)!.StartsWith("[binaire 2 octets"), "Champs binaires résumés");
Check(MdbComparer.Normalize("", options) == "" && MdbComparer.Normalize(null, options) == null, "Texte vide distinct de valeur absente");

// 8. Rapports.
var text = Report.ToText("a.mdb", "b.mdb", new[] { byKey, schema }, options);
Check(text.Contains("a.mdb") && text.Contains("Seulement dans 1") && text.Contains("Email"), "Rapport texte");
var csv = Report.ToCsv(new[] { byKey });
Check(csv.StartsWith("Table;Type;Clé;Détail") && csv.Contains("\"Modifiée\""), "Rapport CSV");

// 9. Grosse base : les lignes sont lues en flux, la mémoire reste faible.
// 2 × 100 000 lignes de 10 000 caractères (≈ 4 Go de texte) : l'ancienne version chargeait tout et saturait la mémoire.
var big1 = new GeneratedDb(100_000, changedRow: -1);
var big2 = new GeneratedDb(100_000, changedRow: 4242);
var bigResult = MdbComparer.Compare(big1, big2, options);
var big = bigResult.Single();
long peak = Math.Max(big1.PeakBytes, big2.PeakBytes);
Check(big.Modified == 1 && big.OnlyIn1 == 0 && big.OnlyIn2 == 0 && big.Rows1 == 100_000 && big.Details.Single().Key == "4242",
    "Grosse table : la seule ligne modifiée est trouvée");
Check(peak < 300_000_000, $"Grosse table : mémoire maximale {peak / 1_000_000} Mo (< 300 Mo)");

Console.WriteLine($"{checks} vérifications réussies.");

record T(TableSchema Schema, List<object?[]> Rows);

sealed class MemoryDb(params T[] tables) : IDatabase
{
    public string Path => "memoire.mdb";
    public IReadOnlyList<TableSchema> Tables { get; } = tables.Select(t => t.Schema).ToList();
    private T Find(TableSchema table) => tables.Single(t => t.Schema == table);
    public int CountRows(TableSchema table) => Find(table).Rows.Count;
    public IEnumerable<object?[]> ReadRows(TableSchema table, IReadOnlyList<string> columns, CancellationToken ct)
    {
        var positions = columns.Select(c => table.Columns.FindIndex(x => string.Equals(x.Name, c, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var row in Find(table).Rows) yield return positions.Select(p => row[p]).ToArray();
    }
}

// Table générée à la volée, comme un lecteur Access : rien n'est stocké.
sealed class GeneratedDb(int rows, int changedRow) : IDatabase
{
    private static readonly string Memo = new('x', 10_000);
    private readonly TableSchema table = new() { Name = "Consultations", PrimaryKey = new() { "Id" },
        Columns = new() { new("Id", "Integer"), new("Compte rendu", "WChar") } };
    public long PeakBytes { get; private set; }
    public string Path => "grosse.mdb";
    public IReadOnlyList<TableSchema> Tables => new[] { table };
    public int CountRows(TableSchema t) => rows;
    public IEnumerable<object?[]> ReadRows(TableSchema t, IReadOnlyList<string> columns, CancellationToken ct)
    {
        for (int i = 0; i < rows; i++)
        {
            if (i % 5000 == 0) PeakBytes = Math.Max(PeakBytes, GC.GetTotalMemory(false));
            var memo = i == changedRow ? Memo + "!" : string.Concat(Memo.AsSpan(0, 9_990), i.ToString("D10"));
            yield return columns.Select(c => c == "Id" ? (object?)i : memo).ToArray();
        }
    }
}
