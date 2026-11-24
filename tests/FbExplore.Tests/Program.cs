// Vérifie l'exploration de structure Firebird sans moteur Firebird : types, colonnes utiles, rapport sans données,
// version lue dans l'en-tête du fichier. (Testé aussi à la main sur une vraie base .fdb Firebird 3 en mode embarqué.)
using FbExplore;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("ÉCHEC : " + message); checks++; }

Check(Explorer.TypeName(37, 0, 240, 60, 0) == "VARCHAR(60)" && Explorer.TypeName(8, 0, 4, 0, -2) == "NUMERIC(9,2)" && Explorer.TypeName(261, 1, 8, 0, 0) == "BLOB TEXTE"
    && Explorer.TypeName(35, 0, 8, 0, 0) == "TIMESTAMP" && Explorer.TypeName(14, 0, 13, 13, 0) == "CHAR(13)", "Types Firebird");
Check(Explorer.IsInteresting("NIR_ASSURE") && Explorer.IsInteresting("Qualite") && Explorer.IsInteresting("TEL_PORTABLE") && !Explorer.IsInteresting("MONTANT"), "Colonnes utiles repérées");

var tables = new List<TableInfo>
{
    new("BENEFICIAIRE", 1, new[] { new ColumnInfo("NIR_ASSURE", "CHAR(13)", true), new ColumnInfo("QUALITE", "SMALLINT", true), new ColumnInfo("MONTANT", "NUMERIC(9,2)", true) }, false),
    new("PATIENT", 2, new[] { new ColumnInfo("ID_PATIENT", "INTEGER", false), new ColumnInfo("NOM", "VARCHAR(60)", true) }, false),
    new("V_TEST", -1, new[] { new ColumnInfo("NOM", "VARCHAR(60)", true) }, true)
};
var report = Explorer.Report(tables, "vitale.fdb", "WI-V3.0");
Check(report.Contains("2 tables, 1 vues, 3 lignes") && report.Contains("BENEFICIAIRE (1 ligne) : NIR_ASSURE, QUALITE") && report.Contains("[PATIENT] 2 lignes")
    && report.Contains("ID_PATIENT                       INTEGER NOT NULL") && !report.Contains("MONTANT, "), "Rapport de structure");

var folder = Path.Combine(Path.GetTempPath(), "fbexplore-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    byte[] Header(ushort ods) { var h = new byte[1024]; h[0] = 1; BitConverter.GetBytes((ushort)(0x8000 | ods)).CopyTo(h, 0x12); return h; }
    File.WriteAllBytes(Path.Combine(folder, "fb25.fdb"), Header(11));
    File.WriteAllBytes(Path.Combine(folder, "fb3.fdb"), Header(12));
    File.WriteAllBytes(Path.Combine(folder, "fb4.fdb"), Header(13));
    File.WriteAllBytes(Path.Combine(folder, "autre.fdb"), new byte[100]);
    Check(Explorer.DiskVersion(Path.Combine(folder, "fb25.fdb"))?.Firebird == "Firebird 2.x" && Explorer.DiskVersion(Path.Combine(folder, "fb3.fdb"))?.Ods == 12
        && Explorer.DiskVersion(Path.Combine(folder, "fb4.fdb"))?.Firebird == "Firebird 4 ou 5" && Explorer.DiskVersion(Path.Combine(folder, "autre.fdb")) == null,
        "Version de Firebird lue dans l'en-tête du fichier");
}
finally { Directory.Delete(folder, true); }

Console.WriteLine($"{checks} vérifications réussies.");
