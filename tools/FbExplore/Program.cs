// Explore la STRUCTURE d'une base Firebird (.fdb) en lecture seule : tables, colonnes, types, nombre de lignes.
// Aucune donnée patient n'est lue ni écrite : le rapport peut être transmis.
using System.Text;
using FbExplore;
using FirebirdSql.Data.FirebirdClient;

Console.OutputEncoding = Encoding.UTF8;
try
{
    var o = ExploreOptions.Parse(args);
    if (string.IsNullOrWhiteSpace(o.Database)) Ask(o);
    if (!File.Exists(o.Database) && o.Server == null) throw new FileNotFoundException("Base Firebird introuvable.", o.Database);

    if (Explorer.DiskVersion(o.Database) is { } disk)
        Console.WriteLine($"Fichier créé par {disk.Firebird} (structure ODS {disk.Ods}) : il faut un moteur Firebird de cette version pour l'ouvrir.");
    Console.WriteLine($"Ouverture en lecture seule de {o.Database} ({(o.ClientLibrary != null ? "mode embarqué" : "serveur " + (o.Server ?? "localhost"))})…");
    using var connection = new FbConnection(o.ConnectionString());
    connection.Open();
    var structure = Explorer.Read(connection);
    var report = Explorer.Report(structure, Path.GetFileName(o.Database), connection.ServerVersion);
    var output = o.Output ?? Path.Combine(Environment.CurrentDirectory, $"structure-{Path.GetFileNameWithoutExtension(o.Database)}-{DateTime.Now:yyyy-MM-dd-HHmm}.txt");
    File.WriteAllText(output, report, new UTF8Encoding(true));
    Console.WriteLine(report);
    Console.WriteLine($"Rapport (structure seulement, transmissible) : {output}");
    Console.WriteLine("La base n'a pas été modifiée.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("Erreur : " + ex.Message);
    if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("login", StringComparison.OrdinalIgnoreCase))
        Console.Error.WriteLine("→ Utilisateur ou mot de passe Firebird refusé (souvent SYSDBA ; le mot de passe dépend de l'installation).");
    if (new[] { "unavailable", "network request", "connection refused", "No connection" }.Any(m => ex.Message.Contains(m, StringComparison.OrdinalIgnoreCase)))
        Console.Error.WriteLine("→ Aucun serveur Firebird joignable : lancez le service Firebird, ou utilisez le mode embarqué (--client chemin\\fbclient.dll).");
    if (ex.Message.Contains("ODS", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("unsupported on-disk structure", StringComparison.OrdinalIgnoreCase))
        Console.Error.WriteLine("→ La base a été créée par une autre version de Firebird : utilisez la même version que le logiciel Vitale.");
    Environment.ExitCode = 1;
}
if (args.Length == 0) { Console.WriteLine("Appuyez sur Entrée pour fermer."); Console.ReadLine(); }

static void Ask(ExploreOptions o)
{
    var folder = AppContext.BaseDirectory;
    var found = Directory.GetFiles(folder, "*.fdb").Concat(Directory.GetFiles(folder, "*.gdb")).ToList();
    if (found.Count == 1) { o.Database = found[0]; Console.WriteLine($"Base : {o.Database}"); }
    else
    {
        Console.WriteLine("Chemin de la copie de la base Firebird (.fdb) :");
        o.Database = (Console.ReadLine() ?? "").Trim().Trim('"');
    }
    var client = new[] { "fbclient.dll", "fbembed.dll" }.Select(f => Path.Combine(folder, f)).FirstOrDefault(File.Exists);
    if (client != null) { o.ClientLibrary = client; Console.WriteLine($"Mode embarqué : {client}"); }
    Console.Write("Utilisateur Firebird [SYSDBA] : ");
    var user = (Console.ReadLine() ?? "").Trim();
    if (user.Length > 0) o.User = user;
    Console.Write("Mot de passe Firebird (vide = masterkey, la valeur par défaut) : ");
    var password = ReadHidden();
    if (password.Length > 0) o.Password = password;
}

static string ReadHidden()
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
