using System.Data;
using System.Data.OleDb;
using ChatServeur.Fundus;
using EyeChat.Data;

namespace ChatServeur.EyeMail;

/// <summary>
/// Lit la table Patients d'Access en lecture seule, avec le chemin et les droits déjà configurés
/// pour le serveur (paramètres RDV ou FoAccess:DatabasePath). Nom de table modifiable par EyeMail:PatientsTable.
/// </summary>
public sealed class EyeMailAccessSource(FoAccessReader access, IConfiguration configuration) : IEyeMailSource
{
    public EyeMailSourceData ReadPatients(Action<int>? progress, CancellationToken ct)
    {
        using var connection = ReadOnlyAccess.Open(access.AccessOptions());
        var table = FindTable(connection, configuration["EyeMail:PatientsTable"] ?? "Patients");

        // On ne lit que les colonnes utiles : un SELECT * échoue sur certaines colonnes Access
        // (pièces jointes, valeurs multiples) et lirait inutilement les colonnes médicales.
        var names = ColumnNames(connection, table);
        var columns = EyeMailColumns.Detect(names);
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

    private static List<string> ColumnNames(OleDbConnection connection, string table)
    {
        var rows = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!;
        return rows.Rows.Cast<DataRow>()
            .OrderBy(row => Convert.ToInt64(row["ORDINAL_POSITION"], System.Globalization.CultureInfo.InvariantCulture))
            .Select(row => (string)row["COLUMN_NAME"])
            .ToList();
    }

    private static string FindTable(OleDbConnection connection, string wanted)
    {
        var tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" })!;
        foreach (DataRow row in tables.Rows)
        {
            var name = (string)row["TABLE_NAME"];
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        throw new InvalidDataException($"La table « {wanted} » est absente de la base Access. Réglez EyeMail:PatientsTable si elle porte un autre nom.");
    }
}
