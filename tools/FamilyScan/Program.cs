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
    if (string.IsNullOrWhiteSpace(options.Database))
    {
        Console.WriteLine("Chemin de la base Access (copie de la base du cabinet, .mdb ou .accdb) :");
        options.Database = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');
    }
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

    Console.WriteLine("Calcul des liens et des familles…");
    var result = scanner.Finish();
    var folder = options.Output ?? Path.Combine(Environment.CurrentDirectory, $"Familles-{DateTime.Now:yyyy-MM-dd-HHmm}");
    Directory.CreateDirectory(folder);
    var reportPath = Path.Combine(folder, "rapport-anonymise.txt");
    var detailPath = Path.Combine(folder, "detail-familles-NE-PAS-DIFFUSER.csv");
    File.WriteAllText(reportPath, FamilyScanReport.Anonymous(result), new UTF8Encoding(true));
    File.WriteAllText(detailPath, FamilyScanReport.DetailCsv(result), new UTF8Encoding(true));
    Console.WriteLine();
    Console.WriteLine(FamilyScanReport.Anonymous(result));
    Console.WriteLine($"Rapport anonymisé (transmissible) : {reportPath}");
    Console.WriteLine($"Détail nominatif (à garder au cabinet) : {detailPath}");
    Console.WriteLine($"Durée : {clock.Elapsed:mm\\:ss}. La base Access n'a pas été modifiée.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("Erreur : " + ex.Message);
    Environment.ExitCode = 1;
}
if (args.Length == 0) { Console.WriteLine("Appuyez sur Entrée pour fermer."); Console.ReadLine(); }

sealed class Options
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "AG_rdv";
    public string Workgroup { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Output { get; set; }
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
