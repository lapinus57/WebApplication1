using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using EyeChat.Data;

namespace EyeChat.Fundus;

public sealed record FoPatientData(string Id, string Name, DateTime Appointment, IReadOnlyList<FoConsultation> Consultations);
// Skipped lists the agenda entries left out by a tolerant search (identifier and reason only, never clinical text).
public sealed record FoDataSet(string SourceKey, IReadOnlyList<FoPatientData> Patients, IReadOnlyList<string>? Skipped = null);

/// <summary>Shared read-only Access reader for the Windows client and server.</summary>
[SupportedOSPlatform("windows")]
public sealed class FoAccessData(AccessReadOptions options, Func<DateTime>? clock = null)
{
    public DateTime Now => clock?.Invoke() ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

    public IReadOnlyList<FoField> Inspect()
    {
        using var connection = ReadOnlyAccess.Open(options);
        return Schema(connection);
    }

    private static IReadOnlyList<FoField> Schema(OleDbConnection connection)
    {
        var tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" })!;
        var result = new List<FoField>();
        foreach (DataRow table in tables.Rows)
        {
            var name = (string)table["TABLE_NAME"];
            if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue;
            var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, name, null })!;
            foreach (DataRow column in columns.Rows)
                result.Add(new(name, (string)column["COLUMN_NAME"], Convert.ToInt32(column["DATA_TYPE"], CultureInfo.InvariantCulture)));
        }
        return result.OrderBy(f => f.Table).ThenBy(f => f.Name).ToList();
    }

    public string SourceKey(FoMapping mapping) => FundusAnalysis.Hash(Path.GetFullPath(options.DatabasePath).ToUpperInvariant() + "\n" + JsonSerializer.Serialize(mapping));

    public string RegistrySourceKey(FoMapping mapping) => FundusAnalysis.Hash(Path.GetFullPath(options.DatabasePath).ToUpperInvariant()
        + "\n" + JsonSerializer.Serialize(new { mapping.PatientsTable, mapping.PatientId }));

    // Read every patient, including those without any appointment or consultation.
    // Only one patient's clinical timeline is retained at a time by the registry import.
    public IEnumerable<RegistryPatient> ReadRegistryPatients(FoMapping mapping, CancellationToken cancellationToken)
    {
        using var connection = ReadOnlyAccess.Open(options);
        var schema = Schema(connection);
        mapping.SelectClinicalFields(schema);
        mapping.ValidateClinical(schema);
        var index = new List<(string Id, string LastName, string FirstName)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {Q(mapping.PatientId)}, {Q(mapping.LastName)}, {Q(mapping.FirstName)} FROM {Q(mapping.PatientsTable)}";
            using var reader = command.ExecuteReader()!;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Key(reader.GetValue(0));
                if (!ids.Add(id)) throw new FoDataException("L’identifiant patient choisi n’est pas unique dans Patients.");
                if (index.Count >= 100_000) throw new FoDataException("Plus de 100 000 patients dans cette source. Vérifiez la table choisie avant l’import du registre.");
                index.Add((id, Convert.ToString(reader.GetValue(1)) ?? "", Convert.ToString(reader.GetValue(2)) ?? ""));
            }
        }
        var now = Now;
        foreach (var patient in index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(patient.Id, patient.LastName, patient.FirstName,
                ReadClinicalTimeline(connection, schema, mapping, patient.Id, now, cancellationToken, includeActs: false));
        }
    }

    private static IReadOnlyList<FoConsultation> ReadClinicalTimeline(OleDbConnection connection, IReadOnlyList<FoField> schema,
        FoMapping mapping, string id, DateTime now, CancellationToken cancellationToken, bool includeActs = true)
    {
        using var consultations = connection.CreateCommand();
        var query = FoConsultationQuery.Create(mapping, schema, includeActs: includeActs);
        consultations.CommandText = query.Sql;
        AddKey(consultations, schema, mapping.ConsultationsTable, mapping.ConsultationPatientId, id);
        var rows = new List<FoConsultation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int totalCharacters = 0;
        using var reader = consultations.ExecuteReader()!;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consultationId = Key(reader.GetValue(0));
            if (!ids.Add(consultationId)) throw new FoDataException("Consultation dupliquée : vérifiez l’identifiant de consultation et l’unicité de Tarif.Code acte.");
            var date = Date(reader.GetValue(1));
            if (date > now) continue;
            var (text, refraction) = ClinicalText(reader, mapping, 2);
            totalCharacters += text.Length + refraction.Length;
            if (text.Length + refraction.Length > 100_000 || rows.Count >= 10_000 || totalCharacters > 5_000_000)
                throw new FoDataException("Volume d’observations inhabituel. Vérifiez les champs choisis ; l’analyse a été interrompue sans résultat partiel.");
            rows.Add(WithAct(new(consultationId, date, text, refraction), reader, mapping, query.HasActs));
        }
        return rows;
    }

    /// <param name="skipInvalid">
    /// When true, a badly filled agenda entry or patient record is left out and reported in <see cref="FoDataSet.Skipped"/>
    /// instead of stopping the whole search. Schema and volume errors still stop it.
    /// </param>
    public FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken cancellationToken, bool skipInvalid = false)
    {
        var skipped = new List<string>();
        using var connection = ReadOnlyAccess.Open(options);
        var schema = Schema(connection);
        mapping.SelectClinicalFields(schema);
        mapping.Validate(schema);
        var now = Now;
        var appointments = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {Q(mapping.AppointmentPatientId)}, {Q(mapping.AppointmentDate)}, {Q(mapping.AppointmentTime)}{(mapping.NoCancellationColumn ? "" : ", " + Q(mapping.Cancelled))} FROM {Q(mapping.AppointmentsTable)} WHERE {Q(mapping.AppointmentDate)} >= ? AND {Q(mapping.AppointmentDate)} < ?";
            command.Parameters.Add("?", OleDbType.Date).Value = from.Date;
            command.Parameters.Add("?", OleDbType.Date).Value = until.Date;
            using var reader = command.ExecuteReader()!;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Empty slots have no patient; they are not appointments for a patient.
                if (reader.IsDBNull(0) || string.IsNullOrWhiteSpace(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture))) continue;
                var id = Key(reader.GetValue(0));
                if (id == "0") continue;
                DateTime appointment;
                try
                {
                    if (!mapping.NoCancellationColumn && IsCancelled(reader.GetValue(3))) continue;
                    appointment = Date(reader.GetValue(1)).Date + Time(reader.GetValue(2));
                }
                catch (FoDataException ex) when (skipInvalid)
                {
                    skipped.Add($"Rendez-vous du patient {id} ignoré : {ex.Message}");
                    continue;
                }
                if (appointment < now || appointment < from || appointment >= until) continue;
                if (!appointments.TryGetValue(id, out var previous) || appointment < previous) appointments[id] = appointment;
                if (appointments.Count > 1000) throw new FoDataException("Plus de 1 000 patients dans cette période. Réduisez la période ; aucun résultat partiel n’a été enregistré.");
            }
        }
        var result = new List<FoPatientData>();
        foreach (var (id, appointment) in appointments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = "";
            try
            {
                using var patient = connection.CreateCommand();
                patient.CommandText = $"SELECT {Q(mapping.LastName)}, {Q(mapping.FirstName)} FROM {Q(mapping.PatientsTable)} WHERE {Q(mapping.PatientId)} = ?";
                AddKey(patient, schema, mapping.PatientsTable, mapping.PatientId, id);
                using (var reader = patient.ExecuteReader()!)
                {
                    if (!reader.Read()) throw new FoDataException("Un patient de l’agenda n’a pas de correspondance dans Patients. Vérifiez les champs de liaison.");
                    name = $"{Convert.ToString(reader.GetValue(0))} {Convert.ToString(reader.GetValue(1))}".Trim();
                    if (reader.Read()) throw new FoDataException("L’identifiant patient choisi n’est pas unique dans Patients.");
                }
                var rows = ReadClinicalTimeline(connection, schema, mapping, id, now, cancellationToken);
                result.Add(new(id, name, appointment, rows));
            }
            catch (FoDataException ex) when (skipInvalid)
            {
                skipped.Add($"Patient {id}{(name.Length > 0 ? " (" + name + ")" : "")}, rendez-vous du {appointment:dd/MM/yyyy HH:mm} ignoré : {ex.Message}");
            }
        }
        return new(SourceKey(mapping), result, skipped);
    }

    public FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId)
    {
        using var connection = ReadOnlyAccess.Open(options);
        var schema = Schema(connection);
        mapping.SelectClinicalFields(schema);
        mapping.Validate(schema);
        using var command = connection.CreateCommand();
        var query = FoConsultationQuery.Create(mapping, schema, single: true);
        command.CommandText = query.Sql;
        AddKey(command, schema, mapping.ConsultationsTable, mapping.ConsultationPatientId, patientId);
        AddKey(command, schema, mapping.ConsultationsTable, mapping.ConsultationId, consultationId);
        using var reader = command.ExecuteReader()!;
        if (!reader.Read()) throw new FoDataException("La consultation source n’est plus disponible. Actualisez l’analyse.");
        var (text, refraction) = ClinicalText(reader, mapping, 2);
        var row = WithAct(new(consultationId, Date(reader.GetValue(1)), text, refraction), reader, mapping, query.HasActs);
        if (reader.Read()) throw new FoDataException("Consultation dupliquée : vérifiez l’identifiant de consultation et l’unicité de Tarif.Code acte.");
        return row;
    }

    public IReadOnlyList<FoConsultation> ReadPatientTimeline(FoMapping mapping, string patientId, CancellationToken token)
    {
        using var connection = ReadOnlyAccess.Open(options);
        var schema = Schema(connection);
        mapping.SelectClinicalFields(schema);
        mapping.Validate(schema);
        return ReadClinicalTimeline(connection, schema, mapping, patientId, Now, token);
    }

    public DateTime? ReadBirthDate(FoMapping mapping, string patientId)
    {
        using var connection = ReadOnlyAccess.Open(options);
        var schema = Schema(connection);
        mapping.ValidateClinical(schema);
        var aliases = new[] { "Date de naissance", "DateNaissance", "Date naissance", "Naissance" };
        var fields = schema.Where(f => f.Table == mapping.PatientsTable && aliases.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (fields.Length != 1) return null;
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(fields[0].Name)} FROM {Q(mapping.PatientsTable)} WHERE {Q(mapping.PatientId)} = ?";
        AddKey(command, schema, mapping.PatientsTable, mapping.PatientId, patientId);
        var value = command.ExecuteScalar();
        return value is DateTime date && date.Date <= Now.Date ? date.Date : null;
    }

    private static FoConsultation WithAct(FoConsultation row, OleDbDataReader reader, FoMapping mapping, bool available)
    {
        if (!available) return row;
        int offset = 2 + mapping.ObservationFields.Length;
        var local = Convert.ToString(reader.GetValue(offset), CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (local.Length > 128) throw new FoDataException("Le code acte local est trop long. Vérifiez le champ de liaison.");
        return row with { CcamCodes = FoCcam.Parse(reader.GetValue(offset + 1)), LocalActCode = local, ActLinkAvailable = true };
    }

    private static (string Text, string Refraction) ClinicalText(OleDbDataReader reader, FoMapping mapping, int offset)
    {
        var texts = new List<string>();
        var refraction = "";
        for (int i = 0; i < mapping.ObservationFields.Length; i++)
        {
            var text = FoMedicalText.Read(reader.GetValue(offset + i));
            if (mapping.ObservationFields[i].Equals("REFRACTION", StringComparison.OrdinalIgnoreCase)) refraction = text;
            else texts.Add(text);
        }
        return (string.Join("\n", texts), refraction);
    }

    private static void AddKey(OleDbCommand command, IReadOnlyList<FoField> schema, string table, string column, string value)
    {
        var type = (OleDbType)schema.Single(f => f.Table == table && f.Name == column).DataType;
        object converted = type switch
        {
            OleDbType.Integer => int.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.SmallInt => short.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.TinyInt => sbyte.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.UnsignedTinyInt => byte.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.BigInt => long.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.Double => double.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.Single => float.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.Decimal or OleDbType.Numeric or OleDbType.Currency => decimal.Parse(value, CultureInfo.InvariantCulture),
            OleDbType.Guid => Guid.Parse(value),
            OleDbType.WChar or OleDbType.VarWChar or OleDbType.VarChar or OleDbType.Char => value,
            _ => throw new FoDataException("Type d’identifiant non pris en charge. Choisissez une clé numérique, texte ou GUID.")
        };
        command.Parameters.Add("?", type).Value = converted;
    }

    private static string Q(string name) => "[" + name.Replace("]", "]]") + "]";
    private static string Key(object value) => value == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture))
        ? throw new FoDataException("Un identifiant nécessaire est vide. Vérifiez les champs de liaison.") : Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();
    private static DateTime Date(object value) => value is DateTime date ? date :
        DateTime.TryParse(Convert.ToString(value), CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out date) ? date :
        throw new FoDataException("Une date est absente ou illisible. Vérifiez le champ date sélectionné.");
    private static TimeSpan Time(object value)
    {
        if (value is DateTime date) return date.TimeOfDay;
        if (TimeSpan.TryParse(Convert.ToString(value), CultureInfo.GetCultureInfo("fr-FR"), out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)) return time;
        throw new FoDataException("Une heure de rendez-vous est absente ou illisible. Vérifiez le champ heure sélectionné.");
    }
    private static bool IsCancelled(object value)
    {
        if (value is bool b) return b;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant();
        return text switch { null or "" or "0" or "false" or "non" => false, "1" or "-1" or "true" or "oui" => true,
            _ => throw new FoDataException("Le champ d’annulation contient une valeur non reconnue. Valeurs admises : Oui/Non, True/False, 1/-1/0 ou vide.") };
    }
}

