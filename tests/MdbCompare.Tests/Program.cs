// Vérifie la comparaison de deux bases Access déjà lues (outil tools/MdbCompare). Données synthétiques.
using MdbCompare;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

TableData Table(string name, string[] key, (string Name, string Type)[] columns, params object?[][] rows)
{
    var t = new TableData { Name = name, PrimaryKey = key.ToList(), Columns = columns.Select(c => new ColumnInfo(c.Name, c.Type)).ToList(), RowsLoaded = true };
    t.Rows.AddRange(rows);
    return t;
}
DatabaseSnapshot Db(params TableData[] tables)
{
    var db = new DatabaseSnapshot { Path = "x.mdb", Provider = "test" };
    db.Tables.AddRange(tables);
    return db;
}
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
var byKey = MdbComparer.CompareTable(t1, t2, options);
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
var schema = MdbComparer.CompareTable(s1, s2, options);
Check(schema.SchemaNotes.Any(n => n.Contains("Email") && n.Contains("base 2")), "Colonne seulement dans la base 2");
Check(schema.SchemaNotes.Any(n => n.Contains("Texte") && n.Contains("WChar(50)") && n.Contains("WChar(100)")), "Changement de type signalé");
Check(schema.DifferenceCount == 0 && schema.Status == TableStatus.Differente, "Données identiques sur les colonnes communes, table différente par sa structure");

// 4. Sans clé : comparaison ligne entière, doublons compris.
var cols = new[] { ("A", "Integer"), ("B", "WChar(10)") };
var n1 = Table("Journal", Array.Empty<string>(), cols, new object?[] { 1, "x" }, new object?[] { 1, "x" }, new object?[] { 2, "y" });
var n2 = Table("Journal", Array.Empty<string>(), cols, new object?[] { 1, "x" }, new object?[] { 2, "y" }, new object?[] { 3, "z" });
var noKey = MdbComparer.CompareTable(n1, n2, options);
Check(noKey.ComparedBy.StartsWith("ligne entière") && noKey.OnlyIn1 == 1 && noKey.OnlyIn2 == 1 && noKey.Modified == 0, "Sans clé : un doublon en trop d'un côté, une ligne nouvelle de l'autre");

// 5. Clé non unique (pilote mal renseigné) : repli sur la ligne entière au lieu d'un plantage.
var dup = Table("D", new[] { "A" }, cols, new object?[] { 1, "x" }, new object?[] { 1, "y" });
Check(MdbComparer.CompareTable(dup, dup, options).ComparedBy.StartsWith("ligne entière"), "Clé en double : repli sans erreur");

// 6. Options : casse/espaces ignorés, structure seule, limite du détail.
var c1 = Table("C", new[] { "A" }, cols, new object?[] { 1, "Metz  " });
var c2 = Table("C", new[] { "A" }, cols, new object?[] { 1, "METZ" });
Check(MdbComparer.CompareTable(c1, c2, options).Modified == 1, "Casse différente détectée par défaut");
Check(MdbComparer.CompareTable(c1, c2, options with { IgnoreCaseAndTrailingSpaces = true }).Status == TableStatus.Identique, "Casse et espaces de fin ignorés sur demande");
Check(MdbComparer.CompareTable(t1, t2, options with { CompareData = false }).Status == TableStatus.Identique, "Structure seule : données non comparées");
var many1 = Table("M", new[] { "A" }, cols, Enumerable.Range(0, 50).Select(i => new object?[] { i, "v" }).ToArray());
var many2 = Table("M", new[] { "A" }, cols);
var limited = MdbComparer.CompareTable(many1, many2, options with { MaxDetailsPerTable = 10 });
Check(limited.OnlyIn1 == 50 && limited.Details.Count == 10 && limited.DetailsTruncated, "Détail limité mais total exact");

// 7. Normalisation des valeurs Access.
Check(MdbComparer.Normalize(1.5d, options) == "1.5" && MdbComparer.Normalize(true, options) == "Vrai" && MdbComparer.Normalize(DBNull.Value, options) == null, "Nombres, booléens et vides");
Check(MdbComparer.Normalize(new byte[] { 1, 2 }, options)!.StartsWith("[binaire 2 octets"), "Champs binaires résumés");
Check(MdbComparer.Normalize("", options) == "" && MdbComparer.Normalize(null, options) == null, "Texte vide distinct de valeur absente");

// 8. Rapports.
var text = Report.ToText("a.mdb", "b.mdb", new[] { byKey, schema }, options);
Check(text.Contains("a.mdb") && text.Contains("Seulement dans 1") && text.Contains("Email"), "Rapport texte");
var csv = Report.ToCsv(new[] { byKey });
Check(csv.StartsWith("Table;Type;Clé;Détail") && csv.Contains("\"Modifiée\""), "Rapport CSV");

Console.WriteLine($"{checks} vérifications réussies.");
