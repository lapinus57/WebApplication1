using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text.Json;
using EyeChat.Data;
using EyeChat.Fundus;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Fundus;

public sealed class FoAccessReader(ChatDbContext db, IConfiguration configuration, ChatServeur.Startup.ServerAccessSettingsStore settings) : IFoAccessReader, ChatServeur.Registry.IRegistryAccessReader
{
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris"));

    private static string? Value(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Ordre : réglages saisis dans l'administration, puis FoAccess:* (appsettings ou variables), puis paramètres RDV des postes.
    private AccessReadOptions Options()
    {
        var (path, _, exists) = DescribeSource();
        if (!exists)
            throw new FoDataException("La base Access n’est pas accessible au serveur. Vérifiez le chemin dans Réglages Access du serveur et les droits de lecture du compte du service.");
        var saved = settings.Load();
        return new(path,
            Value(saved.Provider) ?? Value(configuration["FoAccess:Provider"]) ?? "Microsoft.ACE.OLEDB.12.0",
            Value(saved.WorkgroupPath) ?? Value(configuration["FoAccess:WorkgroupPath"]) ?? "",
            Value(saved.UserName) ?? Value(configuration["FoAccess:UserName"]) ?? "",
            settings.Password(saved) ?? configuration["FoAccess:Password"] ?? "");
    }

    private FoAccessData Reader() => new(Options(), () => Now);
    /// <summary>Chemin de la base Access que lit le serveur, et d'où il vient (affichage seulement, sans exception).</summary>
    public (string Path, string Origin, bool Exists) DescribeSource()
    {
        var saved = Value(settings.Load().DatabasePath);
        if (saved != null)
            return (saved, "Réglages Access du serveur", File.Exists(saved));
        var configured = Value(configuration["FoAccess:DatabasePath"]);
        if (configured != null)
            return (configured, "réglage FoAccess:DatabasePath du serveur", File.Exists(configured));
        var json = db.ServerConfigs.AsNoTracking().Select(c => c.AppointmentSearchJson).FirstOrDefault();
        var path = string.IsNullOrEmpty(json) ? "" : JsonSerializer.Deserialize<AppointmentSearchConfig>(json)?.DatabasePath ?? "";
        return (path, "Paramètres → Recherche RDV, envoyés par un poste", !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    /// <summary>Le serveur utilise-t-il un fichier de groupe de travail (réglages ou FoAccess:WorkgroupPath) ?</summary>
    public bool UsesWorkgroup => Value(settings.Load().WorkgroupPath) != null || Value(configuration["FoAccess:WorkgroupPath"]) != null;

    /// <summary>
    /// Ouvre Access avec les réglages actuels et décrit ce que le serveur voit, sans lire de patient :
    /// pilote utilisé, nombre de tables, colonnes de la table des patients.
    /// </summary>
    public string TestConnection(string patientsTable)
    {
        using var connection = ReadOnlyAccess.Open(Options());
        var tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, null })!.Rows.Cast<DataRow>()
            .Where(r => new[] { "TABLE", "LINK", "VIEW", "PASS-THROUGH" }.Contains(Convert.ToString(r["TABLE_TYPE"]), StringComparer.OrdinalIgnoreCase))
            .Select(r => Convert.ToString(r["TABLE_NAME"]) ?? "")
            .Where(n => n.Length > 0 && !n.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)).ToList();
        var table = tables.FirstOrDefault(t => string.Equals(t, patientsTable, StringComparison.OrdinalIgnoreCase));
        if (table == null)
            return $"Connexion réussie (pilote {connection.Provider}, {tables.Count} table(s)), mais la table « {patientsTable} » est introuvable.";
        var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!.Rows.Cast<DataRow>()
            .Select(r => Convert.ToString(r["COLUMN_NAME"]) ?? "").Where(n => n.Length > 0).ToList();
        return columns.Count == 0
            ? $"Connexion réussie (pilote {connection.Provider}, {tables.Count} table(s)), mais aucune colonne visible dans « {table} » : vérifiez le groupe de travail, l'utilisateur et le mot de passe."
            : $"Connexion réussie (pilote {connection.Provider}, {tables.Count} table(s)). Table « {table} » : {columns.Count} colonne(s), dont {string.Join(", ", columns.Take(12))}{(columns.Count > 12 ? "…" : "")}.";
    }

    /// <summary>Chemin et droits Access du serveur, pour les autres lectures (base EyeMail).</summary>
    public AccessReadOptions AccessOptions() => Options();
    public IReadOnlyList<FoField> Inspect() => Reader().Inspect();
    public string SourceKey(FoMapping mapping) => Reader().SourceKey(mapping);
    public string RegistrySourceKey(FoMapping mapping) => Reader().RegistrySourceKey(mapping);
    public IEnumerable<RegistryPatient> ReadRegistryPatients(FoMapping mapping, CancellationToken ct) => Reader().ReadRegistryPatients(mapping, ct);
    public FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken cancellationToken)
        => Reader().Load(mapping, from, until, cancellationToken);
    public FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId)
        => Reader().ReadConsultation(mapping, patientId, consultationId);
}
