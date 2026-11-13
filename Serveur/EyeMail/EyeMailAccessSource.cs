using System.Data;
using System.Data.OleDb;
using System.Text.Json;
using ChatServeur.Fundus;
using EyeChat.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.EyeMail;

/// <summary>
/// Lit la table des patients d'Access en lecture seule, avec le chemin et les droits déjà configurés
/// pour le serveur (paramètres RDV ou FoAccess:DatabasePath).
/// Table : EyeMail:PatientsTable, sinon celle choisie dans le registre « Patients · Diabète / HTO », sinon « Patients ».
/// </summary>
public sealed class EyeMailAccessSource(FoAccessReader access, ChatDbContext chat, IConfiguration configuration) : IEyeMailSource
{
    public EyeMailSourceData ReadPatients(Action<int>? progress, CancellationToken ct)
    {
        var registry = RegistryMapping();
        using var connection = ReadOnlyAccess.Open(access.AccessOptions());
        var wanted = configuration["EyeMail:PatientsTable"] is { Length: > 0 } configured ? configured
            : registry?.PatientsTable is { Length: > 0 } registryTable ? registryTable
            : "Patients";
        var table = FindTable(connection, wanted);

        // Colonnes choisies dans le registre patients pour cette table : elles priment sur la reconnaissance par le nom.
        var preferred = registry != null && string.Equals(registry.PatientsTable, table, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string> { ["Id"] = registry.PatientId, ["LastName"] = registry.LastName, ["FirstName"] = registry.FirstName }
            : null;

        // On ne lit que les colonnes utiles : un SELECT * échoue sur certaines colonnes Access
        // (pièces jointes, valeurs multiples) et lirait inutilement les colonnes médicales.
        var names = ColumnNames(connection, table);
        var columns = EyeMailColumns.Detect(names, preferred, table);
        var selected = columns.Found.Values.Distinct().Select(name => names.IndexOf(name)).OrderBy(i => i).ToList();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", selected.Select(i => Quote(names[i])))} FROM {Quote(table)}";
        using var reader = command.ExecuteReader()!;
        var today = access.Now.Date;

        var patients = new List<EyeMailSourcePatient>();
        var values = new object?[names.Count];
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < selected.Count; i++)
                values[selected[i]] = reader.GetValue(i);
            if (columns.Map(values, today) is { } patient)
                patients.Add(patient);
            if (patients.Count > EyeMailStore.MaxPatients)
                throw new InvalidDataException($"Plus de {EyeMailStore.MaxPatients} patients dans {table} : vérifiez la table.");
            if (patients.Count % 500 == 0)
                progress?.Invoke(patients.Count);
        }
        progress?.Invoke(patients.Count);
        return new EyeMailSourceData(table, columns.Found, patients);
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    private EyeChat.Fundus.FoMapping? RegistryMapping()
    {
        try
        {
            var json = chat.PatientRegistrySettings.AsNoTracking().Where(r => r.Id == 1).Select(r => r.MappingJson).FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<EyeChat.Fundus.FoMapping>(json);
        }
        catch
        {
            return null; // Registre non configuré ou illisible : reconnaissance par le nom seulement.
        }
    }

    private static List<string> ColumnNames(OleDbConnection connection, string table)
    {
        var rows = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!;
        var names = rows.Rows.Cast<DataRow>()
            .OrderBy(row => row["ORDINAL_POSITION"] is DBNull ? long.MaxValue : Convert.ToInt64(row["ORDINAL_POSITION"], System.Globalization.CultureInfo.InvariantCulture))
            .Select(row => Convert.ToString(row["COLUMN_NAME"]) ?? "")
            .Where(name => name.Length > 0)
            .ToList();
        if (names.Count > 0)
            return names;

        // Certains pilotes ne décrivent pas les colonnes (table liée, requête) : on demande seulement
        // la structure du résultat, sans lire aucune ligne.
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Quote(table)}";
        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly)!;
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
    }

    // Tables locales, tables liées (base dorsale) et requêtes enregistrées sont acceptées.
    private static readonly string[] ReadableTypes = { "TABLE", "LINK", "PASS-THROUGH", "VIEW" };

    private static string FindTable(OleDbConnection connection, string wanted)
    {
        var tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, null })!.Rows.Cast<DataRow>()
            .Where(row => ReadableTypes.Contains(Convert.ToString(row["TABLE_TYPE"]), StringComparer.OrdinalIgnoreCase))
            .Select(row => Convert.ToString(row["TABLE_NAME"]) ?? "")
            .Where(name => name.Length > 0 && !name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var match = tables.FirstOrDefault(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match;

        var candidates = tables.Where(name => name.Contains("patient", StringComparison.OrdinalIgnoreCase)).ToList();
        throw new InvalidDataException($"La table « {wanted} » est absente de la base Access."
            + (candidates.Count > 0 ? $" Tables qui ressemblent : {string.Join(", ", candidates)}." : "")
            + " Réglez EyeMail:PatientsTable avec le bon nom.");
    }
}
