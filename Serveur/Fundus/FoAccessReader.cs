using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text.Json;
using EyeChat.Data;
using EyeChat.Fundus;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Fundus;

public sealed class FoAccessReader(ChatDbContext db, IConfiguration configuration) : IFoAccessReader
{
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris"));

    private AccessReadOptions Options()
    {
        var json = db.ServerConfigs.AsNoTracking().Select(c => c.AppointmentSearchJson).FirstOrDefault();
        var config = string.IsNullOrEmpty(json) ? new AppointmentSearchConfig() : JsonSerializer.Deserialize<AppointmentSearchConfig>(json)!;
        // An override is needed when the path configured on desktop workstations differs on the server.
        var path = configuration["FoAccess:DatabasePath"] ?? config.DatabasePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FoDataException("La base Access n’est pas accessible au serveur. Vérifiez le chemin des paramètres RDV ou FoAccess:DatabasePath et les droits de lecture du compte du service.");
        return new(path, configuration["FoAccess:Provider"] ?? "Microsoft.ACE.OLEDB.12.0",
            configuration["FoAccess:WorkgroupPath"] ?? "", configuration["FoAccess:UserName"] ?? "", configuration["FoAccess:Password"] ?? "");
    }

    public IReadOnlyList<FoField> Inspect()
    {
        using var connection = ReadOnlyAccess.Open(Options());
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

    public string SourceKey(FoMapping mapping) => FundusAnalysis.Hash(Path.GetFullPath(Options().DatabasePath).ToUpperInvariant() + "\n" + JsonSerializer.Serialize(mapping));

    public FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        using var connection = ReadOnlyAccess.Open(Options());
        var schema = Schema(connection);
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
                if (!mapping.NoCancellationColumn && IsCancelled(reader.GetValue(3))) continue;
                // Empty slots have no patient; they are not appointments for a patient.
                if (reader.IsDBNull(0) || string.IsNullOrWhiteSpace(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture))) continue;
                var id = Key(reader.GetValue(0));
                if (id == "0") continue;
                var date = Date(reader.GetValue(1));
                var time = Time(reader.GetValue(2));
                var appointment = date.Date + time;
                if (appointment < now || appointment < from || appointment >= until) continue;
                if (!appointments.TryGetValue(id, out var previous) || appointment < previous) appointments[id] = appointment;
                if (appointments.Count > 1000) throw new FoDataException("Plus de 1 000 patients dans cette période. Réduisez la période ; aucun résultat partiel n’a été enregistré.");
            }
        }
        var result = new List<FoPatientData>();
        foreach (var (id, appointment) in appointments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var patient = connection.CreateCommand();
            patient.CommandText = $"SELECT {Q(mapping.LastName)}, {Q(mapping.FirstName)} FROM {Q(mapping.PatientsTable)} WHERE {Q(mapping.PatientId)} = ?";
            AddKey(patient, schema, mapping.PatientsTable, mapping.PatientId, id);
            string name;
            using (var reader = patient.ExecuteReader()!)
            {
                if (!reader.Read()) throw new FoDataException("Un patient de l’agenda n’a pas de correspondance dans Patients. Vérifiez les champs de liaison.");
                name = $"{Convert.ToString(reader.GetValue(0))} {Convert.ToString(reader.GetValue(1))}".Trim();
                if (reader.Read()) throw new FoDataException("L’identifiant patient choisi n’est pas unique dans Patients.");
            }
            using var consultations = connection.CreateCommand();
            consultations.CommandText = $"SELECT {Q(mapping.ConsultationId)}, {Q(mapping.ConsultationDate)}, {string.Join(", ", mapping.ObservationFields.Select(Q))} FROM {Q(mapping.ConsultationsTable)} WHERE {Q(mapping.ConsultationPatientId)} = ?";
            AddKey(consultations, schema, mapping.ConsultationsTable, mapping.ConsultationPatientId, id);
            var rows = new List<FoConsultation>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = consultations.ExecuteReader()!)
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var consultationId = Key(reader.GetValue(0));
                    if (!ids.Add(consultationId)) throw new FoDataException("L’identifiant consultation choisi n’est pas unique pour un patient.");
                    var date = Date(reader.GetValue(1));
                    if (date > now) continue;
                    var (text, followUp) = ClinicalText(reader, mapping, 2);
                    if (text.Length + followUp.Length > 100_000 || rows.Count >= 10_000) throw new FoDataException("Volume d’observations inhabituel. Vérifiez les champs choisis ; l’analyse a été interrompue sans résultat partiel.");
                    rows.Add(new(consultationId, date, text, followUp));
                }
            }
            result.Add(new(id, name, appointment, rows));
        }
        return new(SourceKey(mapping), result);
    }

    public FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId)
    {
        using var connection = ReadOnlyAccess.Open(Options());
        var schema = Schema(connection);
        mapping.Validate(schema);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(mapping.ConsultationDate)}, {string.Join(", ", mapping.ObservationFields.Select(Q))} FROM {Q(mapping.ConsultationsTable)} WHERE {Q(mapping.ConsultationPatientId)} = ? AND {Q(mapping.ConsultationId)} = ?";
        AddKey(command, schema, mapping.ConsultationsTable, mapping.ConsultationPatientId, patientId);
        AddKey(command, schema, mapping.ConsultationsTable, mapping.ConsultationId, consultationId);
        using var reader = command.ExecuteReader()!;
        if (!reader.Read()) throw new FoDataException("La consultation source n’est plus disponible. Actualisez l’analyse.");
        var (text, followUp) = ClinicalText(reader, mapping, 1);
        var row = new FoConsultation(consultationId, Date(reader.GetValue(0)), text, followUp);
        if (reader.Read()) throw new FoDataException("Plusieurs consultations correspondent à cet identifiant. Vérifiez le rapprochement.");
        return row;
    }

    private static (string Text, string FollowUp) ClinicalText(OleDbDataReader reader, FoMapping mapping, int offset)
    {
        var texts = new List<string>();
        var followUp = "";
        for (int i = 0; i < mapping.ObservationFields.Length; i++)
        {
            var text = MedicalText(reader.GetValue(offset + i));
            if (mapping.ObservationFields[i].Equals("ProchainRDV", StringComparison.OrdinalIgnoreCase)) followUp = text;
            else texts.Add(text);
        }
        return (string.Join("\n", texts), followUp);
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
    private static string MedicalText(object value)
    {
        if (value == DBNull.Value) return "";
        if (value is not string text) throw new FoDataException("Le champ médical sélectionné n’est pas un champ texte.");
        if (text.TrimStart().StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(text, @"</?(?:html|div|p|br|span)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new FoDataException("Le compte rendu est au format RTF/HTML. Sélectionnez sa version texte ; ce format doit être adapté avant l’analyse pour éviter des résultats incomplets.");
        return text;
    }
}
