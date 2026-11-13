using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text.Json;
using EyeChat.Data;
using EyeChat.Fundus;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Fundus;

public sealed class FoAccessReader(ChatDbContext db, IConfiguration configuration) : IFoAccessReader, ChatServeur.Registry.IRegistryAccessReader
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

    private FoAccessData Reader() => new(Options(), () => Now);
    /// <summary>Chemin de la base Access que lit le serveur, et d'où il vient (affichage seulement, sans exception).</summary>
    public (string Path, string Origin, bool Exists) DescribeSource()
    {
        var configured = configuration["FoAccess:DatabasePath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return (configured, "réglage FoAccess:DatabasePath du serveur", File.Exists(configured));
        var json = db.ServerConfigs.AsNoTracking().Select(c => c.AppointmentSearchJson).FirstOrDefault();
        var path = string.IsNullOrEmpty(json) ? "" : JsonSerializer.Deserialize<AppointmentSearchConfig>(json)?.DatabasePath ?? "";
        return (path, "Paramètres → Recherche RDV, envoyés par un poste", !string.IsNullOrWhiteSpace(path) && File.Exists(path));
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
