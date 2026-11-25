// Outil de mesure : reconstitue les familles à partir de tout l'historique de l'agenda (AG_rdv), en lecture seule.
// Produit un rapport anonymisé (transmissible) et un détail nominatif (à garder au cabinet). Ne modifie rien.
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using EyeChat.Data;
using EyeChat.Families;

Console.OutputEncoding = Encoding.UTF8;
try
{
    var options = Options.Parse(args);
    if (string.IsNullOrWhiteSpace(options.Database)) Interactive.Ask(options);
    if (!File.Exists(options.Database)) throw new FileNotFoundException("Base Access introuvable.", options.Database);

    using var connection = ReadOnlyAccess.Open(new AccessReadOptions(options.Database, "", options.Workgroup, options.User, options.Password));
    var table = Schema.FindTable(connection, options.Table);
    var columns = Schema.Columns(connection, table);
    var map = Schema.Map(columns, options.Overrides);
    Console.WriteLine($"Base : {options.Database}");
    Console.WriteLine($"Table : {table} ({columns.Count} colonnes), pilote {connection.Provider}");
    foreach (var (role, column) in map.OrderBy(m => m.Key)) Console.WriteLine($"  {role,-10} → {column ?? "(absente)"}");
    foreach (var required in new[] { "patient", "date" })
        if (map[required] == null) throw new InvalidOperationException($"Colonne « {required} » introuvable. Indiquez-la avec --colonne {required}=NomDeLaColonne. Colonnes : {string.Join(", ", columns)}");

    var (first, last) = Schema.DateRange(connection, table, map["date"]!);
    Console.WriteLine($"Rendez-vous du {first:dd/MM/yyyy} au {last:dd/MM/yyyy}.");
    var scanner = new FamilyScanner();
    var clock = Stopwatch.StartNew();
    for (var year = first.Year; year <= last.Year; year++)
    {
        int count = 0;
        using var command = connection.CreateCommand();
        var order = map["heure"] is { } h ? $"{Schema.Q(map["date"]!)}, {Schema.Q(h)}" : Schema.Q(map["date"]!);
        command.CommandText = $"SELECT * FROM {Schema.Q(table)} WHERE {Schema.Q(map["date"]!)} >= ? AND {Schema.Q(map["date"]!)} < ? ORDER BY {order}";
        command.Parameters.Add("?", OleDbType.Date).Value = new DateTime(year, 1, 1);
        command.Parameters.Add("?", OleDbType.Date).Value = new DateTime(year + 1, 1, 1);
        using var reader = command.ExecuteReader()!;
        var read = new RowReader(reader, map);
        while (reader.Read())
        {
            var row = read.Row();
            if (row == null) continue;
            scanner.Add(row);
            count++;
        }
        Console.WriteLine($"  {year} : {count,7:N0} rendez-vous lus ({clock.Elapsed:mm\\:ss})");
    }

    VitaleMatch? vitaleMatch = null;
    if (options.Vitale != null)
    {
        Console.WriteLine($"Carte Vitale : lecture de {options.Vitale}…");
        List<VitaleRow> vitale;
        using (var csv = new StreamReader(options.Vitale, Encoding.UTF8, detectEncodingFromByteOrderMarks: true)) vitale = VitaleLinks.ReadCsv(csv);
        var identitiesTable = options.Patients ?? (map["naissance"] != null ? table : Schema.FindTable(connection, "Patients"));
        var identitiesMap = identitiesTable == table ? map : Schema.Map(Schema.Columns(connection, identitiesTable), options.Overrides);
        var identities = Schema.ReadIdentities(connection, identitiesTable, identitiesMap);
        vitaleMatch = VitaleLinks.Match(vitale, identities);
        var declared = VitaleLinks.Links(vitale, vitaleMatch, DateTime.Today);
        scanner.AddDeclared(declared);
        Console.WriteLine($"  {vitale.Count:N0} fiches, {vitaleMatch.VitalePatients:N0} patients Vitale, {vitaleMatch.Matched:N0} rapprochés (table « {identitiesTable} », {identities.Count:N0} identités), {declared.Count:N0} liens déclarés.");
    }

    Console.WriteLine("Calcul des liens et des familles…");
    var result = scanner.Finish();
    result.Vitale = vitaleMatch;
    var folder = options.Output ?? Path.Combine(Environment.CurrentDirectory, $"Familles-{DateTime.Now:yyyy-MM-dd-HHmm}");
    Directory.CreateDirectory(folder);
    var reportPath = Path.Combine(folder, "rapport-anonymise.txt");
    var detailPath = Path.Combine(folder, "detail-familles-NE-PAS-DIFFUSER.csv");
    File.WriteAllText(reportPath, FamilyScanReport.Anonymous(result), new UTF8Encoding(true));
    File.WriteAllText(detailPath, FamilyScanReport.DetailCsv(result), new UTF8Encoding(true));
    var verificationPath = Path.Combine(folder, "verification-familles-NE-PAS-DIFFUSER.html");
    File.WriteAllText(verificationPath, FamilyVerificationPage.Build(result), new UTF8Encoding(false));
    Console.WriteLine();
    Console.WriteLine(FamilyScanReport.Anonymous(result));
    Console.WriteLine($"Rapport anonymisé (transmissible) : {reportPath}");
    Console.WriteLine($"Détail nominatif (à garder au cabinet) : {detailPath}");
    Console.WriteLine($"Page de vérification (à ouvrir dans le navigateur, au cabinet) : {verificationPath}");
    Console.WriteLine($"Durée : {clock.Elapsed:mm\\:ss}. La base Access n'a pas été modifiée.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("Erreur : " + ex.Message);
    Environment.ExitCode = 1;
}
if (args.Length == 0) { Console.WriteLine("Appuyez sur Entrée pour fermer."); Console.ReadLine(); }

public sealed class Options
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "AG_rdv";
    public string Workgroup { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Output { get; set; }
    /// <summary>Export CSV de la base Vitale (PATIENT + BENEFICIAIRE, requête fournie).</summary>
    public string? Vitale { get; set; }
    /// <summary>Table Access des patients (date de naissance), si AG_rdv n'en a pas.</summary>
    public string? Patients { get; set; }
    public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Valeur manquante après {args[i]}.");
            switch (args[i].ToLowerInvariant())
            {
                case "--table": o.Table = Next(); break;
                case "--mdw": o.Workgroup = Next(); break;
                case "--utilisateur": o.User = Next(); break;
                case "--motdepasse": o.Password = Next(); break;
                case "--sortie": o.Output = Next(); break;
                case "--vitale": o.Vitale = Next(); break;
                case "--patients": o.Patients = Next(); break;
                case "--colonne":
                    var pair = Next().Split('=', 2);
                    if (pair.Length != 2) throw new ArgumentException("Format attendu : --colonne role=NomDeLaColonne");
                    o.Overrides[pair[0].Trim()] = pair[1].Trim();
                    break;
                default:
                    if (args[i].StartsWith("--")) throw new ArgumentException($"Option inconnue : {args[i]}");
                    o.Database = args[i];
                    break;
            }
        }
        return o;
    }
}

// Lancement par double-clic : base et fichier .mdw déposés à côté de FamilyScan.exe, identifiants demandés.
static class Interactive
{
    public static void Ask(Options o)
    {
        var folder = AppContext.BaseDirectory;
        var bases = Directory.GetFiles(folder, "*.mdb").Concat(Directory.GetFiles(folder, "*.accdb")).ToList();
        o.Database = Choose("Base Access", bases, "Chemin de la base Access (copie de la base du cabinet, .mdb ou .accdb) :");
        var vitale = Directory.GetFiles(folder, "*vitale*.csv").ToList();
        if (vitale.Count == 1) { o.Vitale = vitale[0]; Console.WriteLine($"Export Vitale : {o.Vitale}"); }
        var workgroups = Directory.GetFiles(folder, "*.mdw").ToList();
        if (workgroups.Count > 0)
        {
            o.Workgroup = Choose("Fichier de groupe de travail", workgroups, "");
            Console.Write("Utilisateur Access : ");
            o.User = (Console.ReadLine() ?? "").Trim();
            Console.Write("Mot de passe Access : ");
            o.Password = ReadHidden();
        }
    }

    private static string Choose(string label, List<string> found, string question)
    {
        if (found.Count == 1) { Console.WriteLine($"{label} : {found[0]}"); return found[0]; }
        if (found.Count > 1)
        {
            Console.WriteLine($"{label} — plusieurs fichiers trouvés :");
            for (int i = 0; i < found.Count; i++) Console.WriteLine($"  {i + 1}. {Path.GetFileName(found[i])}");
            Console.Write("Numéro : ");
            if (int.TryParse(Console.ReadLine(), out var n) && n >= 1 && n <= found.Count) return found[n - 1];
            throw new ArgumentException("Choix invalide.");
        }
        Console.WriteLine(question);
        return (Console.ReadLine() ?? string.Empty).Trim().Trim('"');
    }

    private static string ReadHidden()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
    }
}

