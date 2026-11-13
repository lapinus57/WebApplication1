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

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{table.Replace("]", "]]")}]";
        using var reader = command.ExecuteReader()!;
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var columns = EyeMailColumns.Detect(names);
        var today = access.Now.Date;

        var patients = new List<EyeMailSourcePatient>();
        var values = new object?[reader.FieldCount];
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            reader.GetValues(values!);
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
