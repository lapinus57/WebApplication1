// Lecture d'AG_rdv : repérage des colonnes et conversion d'une ligne (séparé de Program.cs pour être testé sans Access).
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using EyeChat.Families;

public static class Schema
{
    // Noms de colonnes connus d'AG_rdv (mêmes que l'onglet SMS et mail), du plus probable au moins probable.
    public static readonly Dictionary<string, string[]> Candidates = new()
    {
        ["patient"] = new[] { "Code Patient", "CodePatient", "IDPatient", "Id" },
        ["date"] = new[] { "Date", "Jour", "Date RDV" },
        ["heure"] = new[] { "Heure", "Heure RDV", "HeureRdv" },
        ["creation"] = new[] { "Pris le", "Créé le", "Date création", "DateCreation", "DatePriseRDV" },
        ["arrivee"] = new[] { "Arrivé à", "Arrivee", "Heure arrivée", "HeureArrivee" },
        ["noss"] = new[] { "NoSS", "No SS", "NumSS", "NIR" },
        ["nom"] = new[] { "Nom" },
        ["nomjf"] = new[] { "Nom jeune fille", "NomJeuneFille", "NomJF" },
        ["prenom"] = new[] { "Prénom", "Prenom" },
        ["adresse"] = new[] { "Adresse", "Adr" },
        ["ville"] = new[] { "Ville", "Commune", "CP Ville", "CPVille" },
        ["tel1"] = new[] { "Tel 1", "Téléphone 1", "Téléphone", "Telephone", "Tel" },
        ["tel2"] = new[] { "Tel Bureau", "Tel 2", "Téléphone 2", "Portable", "Mobile", "Tel2" },
        ["email"] = new[] { "EmailPatient", "E-mail", "Email", "PatientEmail" },
        ["email2"] = new[] { "E-mail 2", "Email 2", "EmailCopie" },
        ["annule"] = new[] { "Annulé", "Annule", "Cancelled" },
    };

    public static string Q(string name) => "[" + name.Replace("]", "]]") + "]";

    public static string FindTable(OleDbConnection connection, string wanted)
    {
        var tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, null })!.Rows.Cast<DataRow>()
            .Where(r => Convert.ToString(r["TABLE_TYPE"]) is "TABLE" or "LINK").Select(r => Convert.ToString(r["TABLE_NAME"]) ?? "").ToList();
        return tables.FirstOrDefault(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Table « {wanted} » introuvable. Tables : {string.Join(", ", tables.Where(t => !t.StartsWith("MSys")))}. Utilisez --table.");
    }

    public static List<string> Columns(OleDbConnection connection, string table)
    {
        var listed = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!.Rows.Cast<DataRow>()
            .Select(r => Convert.ToString(r["COLUMN_NAME"]) ?? "").Where(n => n.Length > 0).ToList();
        if (listed.Count > 0) return listed;
        // Certains pilotes ne décrivent pas les colonnes : on les lit sur une requête vide.
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Q(table)} WHERE 1=0";
        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly)!;
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
    }

    public static Dictionary<string, string?> Map(List<string> columns, Dictionary<string, string> overrides)
    {
        string? Find(string name) => columns.FirstOrDefault(c => string.Equals(Simple(c), Simple(name), StringComparison.Ordinal));
        return Candidates.ToDictionary(c => c.Key, c => overrides.TryGetValue(c.Key, out var forced)
            ? Find(forced) ?? throw new InvalidOperationException($"Colonne « {forced} » (--colonne {c.Key}) introuvable.")
            : c.Value.Select(Find).FirstOrDefault(x => x != null));
    }

    public static string Simple(string value)
        => new string(value.Normalize(NormalizationForm.FormD).Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToUpperInvariant();

    public static (DateTime First, DateTime Last) DateRange(OleDbConnection connection, string table, string dateColumn)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MIN({Q(dateColumn)}), MAX({Q(dateColumn)}) FROM {Q(table)}";
        using var reader = command.ExecuteReader()!;
        reader.Read();
        if (reader.IsDBNull(0)) throw new InvalidOperationException("La table ne contient aucun rendez-vous daté.");
        return (Convert.ToDateTime(reader.GetValue(0)), Convert.ToDateTime(reader.GetValue(1)));
    }
}

public sealed class RowReader
{
    private readonly IDataRecord _record;
    private readonly Dictionary<string, int> _ordinal = new();

    public RowReader(IDataRecord record, Dictionary<string, string?> map)
    {
        _record = record;
        var byName = Enumerable.Range(0, record.FieldCount).GroupBy(i => record.GetName(i), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var (role, column) in map)
            if (column != null && byName.TryGetValue(column, out var i)) _ordinal[role] = i;
    }

    private object? Value(string role) => _ordinal.TryGetValue(role, out var i) && !_record.IsDBNull(i) ? _record.GetValue(i) : null;
    private string Text(string role) => Convert.ToString(Value(role), CultureInfo.InvariantCulture)?.Trim() ?? "";
    private DateTime? Date(string role) => Value(role) switch
    {
        DateTime d => d,
        string s when DateTime.TryParse(s, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d) => d,
        _ => null
    };
    private bool Bool(string role) => Value(role) switch { bool b => b, null => false, var v => Text(role) is "1" or "-1" or "Oui" or "oui" or "Vrai" or "True" };

    public ScanRow? Row()
    {
        var id = Value("patient") is { } v && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var n) ? n : 0;
        var day = Date("date");
        if (id <= 0 || day == null) return null;
        // Les heures Access sont souvent stockées au 30/12/1899 : on garde l'heure, sur le jour du RDV.
        DateTime? OnDay(DateTime? t) => t.HasValue ? day.Value.Date + t.Value.TimeOfDay : null;
        return new ScanRow(id, day.Value.Date, OnDay(Date("heure")), Date("creation") is { Year: > 1900 } c ? c : null, OnDay(Date("arrivee")),
            Text("noss"), Text("nom"), Text("nomjf"), Text("prenom"), Text("adresse"), Text("ville"),
            new[] { Text("tel1"), Text("tel2") }, new[] { Text("email"), Text("email2") }, Bool("annule"));
    }
}
