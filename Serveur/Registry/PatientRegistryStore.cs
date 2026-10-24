using System.Text.Json;
using EyeChat.Fundus;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Registry;

public interface IRegistryAccessReader
{
    DateTime Now { get; }
    IReadOnlyList<FoField> Inspect();
    string RegistrySourceKey(FoMapping mapping);
    IEnumerable<RegistryPatient> ReadRegistryPatients(FoMapping mapping, CancellationToken ct);
}

public sealed class PatientRegistryStore(ChatDbContext db, IRegistryAccessReader reader)
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private sealed record Snapshot(string Id, string LastName, string FirstName, DateTime? LastVisit, PatientConditionResult Result);
    public Task<PatientRegistrySettings?> SettingsAsync(CancellationToken ct) => db.PatientRegistrySettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct);

    public async Task SaveMappingAsync(FoMapping mapping, CancellationToken ct)
    {
        var schema = reader.Inspect(); mapping.SelectClinicalFields(schema); mapping.ValidateClinical(schema);
        // The registry is independent from the appointment agenda.
        mapping.AppointmentsTable = mapping.AppointmentPatientId = mapping.AppointmentDate = mapping.AppointmentTime = mapping.Cancelled = "";
        mapping.NoCancellationColumn = false;
        var sourceKey = reader.RegistrySourceKey(mapping);
        await WriteGate.WaitAsync(ct);
        try
        {
            var settings = await db.PatientRegistrySettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
            if (settings == null) { settings = new(); db.PatientRegistrySettings.Add(settings); }
            if (settings.SourceKey != sourceKey) { settings.LastImportUtc = null; settings.LastImportCount = 0; }
            settings.MappingJson = JsonSerializer.Serialize(mapping); settings.SourceKey = sourceKey;
            settings.Revision = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
        }
        finally { WriteGate.Release(); }
    }

    public async Task<int> RefreshAllAsync(Action<int>? progress, CancellationToken ct)
    {
        var initial = await SettingsAsync(ct) ?? throw new FoDataException("Configurez les tables Patients et Consultation avant l’import.");
        var mapping = JsonSerializer.Deserialize<FoMapping>(initial.MappingJson) ?? throw new FoDataException("Correspondance Access illisible.");
        var schema = reader.Inspect(); mapping.SelectClinicalFields(schema); mapping.ValidateClinical(schema);
        if (reader.RegistrySourceKey(mapping) != initial.SourceKey) throw new FoDataException("La source Access a changé. Vérifiez les réglages du registre.");
        var snapshots = new List<Snapshot>(); var ids = new HashSet<string>(StringComparer.Ordinal); var now = reader.Now;
        foreach (var patient in reader.ReadRegistryPatients(mapping, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (snapshots.Count >= 100_000 || string.IsNullOrWhiteSpace(patient.Id) || patient.Id.Length > 256 ||
                patient.LastName.Length > 300 || patient.FirstName.Length > 300 || !ids.Add(patient.Id))
                throw new FoDataException("La liste des patients contient un identifiant invalide ou dupliqué, ou dépasse la limite du registre.");
            snapshots.Add(new(patient.Id, patient.LastName, patient.FirstName,
                patient.Consultations.Where(c => c.Date <= now).Select(c => (DateTime?)c.Date).Max(), PatientConditions.Analyze(patient.Consultations, now)));
            progress?.Invoke(snapshots.Count);
        }
        ct.ThrowIfCancellationRequested();
        if (reader.RegistrySourceKey(mapping) != initial.SourceKey) throw new FoDataException("La source Access a changé pendant l’import. Relancez après vérification.");
        await WriteGate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var settings = await db.PatientRegistrySettings.SingleAsync(s => s.Id == 1, ct);
            if (settings.Revision != initial.Revision || settings.SourceKey != initial.SourceKey)
                throw new FoDataException("Les réglages ont changé pendant la lecture. Aucun résultat partiel n’a été enregistré.");
            var rows = await db.PatientRegistry.Where(p => p.SourceKey == initial.SourceKey).ToDictionaryAsync(p => p.AccessPatientId, StringComparer.Ordinal, ct);
            var imported = DateTime.UtcNow;
            foreach (var row in rows.Values) { row.IsActive = false; row.Revision = Guid.NewGuid().ToString("N"); }
            foreach (var snapshot in snapshots)
            {
                if (!rows.TryGetValue(snapshot.Id, out var row))
                {
                    row = new() { SourceKey = initial.SourceKey, AccessPatientId = snapshot.Id };
                    db.PatientRegistry.Add(row);
                }
                row.LastName = snapshot.LastName; row.FirstName = snapshot.FirstName;
                row.SearchName = FundusAnalysis.Normalize(row.LastName + " " + row.FirstName + " " + row.AccessPatientId);
                row.IsActive = true; row.ImportedUtc = imported; row.LastConsultationDate = snapshot.LastVisit;
                row.Revision = Guid.NewGuid().ToString("N");
                var diabetes = snapshot.Result.Diabetes; var hto = snapshot.Result.OcularHypertension;
                row.DiabetesDetected = diabetes.State; row.DiabetesEvidence = diabetes.Evidence;
                row.DiabetesSourceDate = diabetes.SourceDate; row.DiabetesEvidenceHash = diabetes.EvidenceHash;
                row.HtoDetected = hto.State; row.HtoEvidence = hto.Evidence;
                row.HtoSourceDate = hto.SourceDate; row.HtoEvidenceHash = hto.EvidenceHash;
                // Keep each manual decision. Changed evidence is surfaced as a re-review flag.
            }
            settings.LastImportUtc = imported; settings.LastImportCount = snapshots.Count;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return snapshots.Count;
        }
        finally { WriteGate.Release(); }
    }

    public async Task ReviewAsync(int id, string revision, PatientConditionState? diabetes, PatientConditionState? hto, string user, CancellationToken ct)
    {
        foreach (var value in new[] { diabetes, hto })
            if (value.HasValue && (!Enum.IsDefined(value.Value) || value == PatientConditionState.Unknown))
                throw new FoDataException("Choisissez Oui, Non, À vérifier ou Analyse automatique.");
        if (string.IsNullOrWhiteSpace(user)) throw new FoDataException("Une connexion d’administration est requise.");
        await WriteGate.WaitAsync(ct);
        try
        {
            var settings = await SettingsAsync(ct) ?? throw new FoDataException("Configurez le registre.");
            var row = await db.PatientRegistry.SingleOrDefaultAsync(p => p.Id == id && p.SourceKey == settings.SourceKey && p.IsActive, ct)
                ?? throw new FoDataException("Patient indisponible dans la source active. Rechargez le registre.");
            if (row.Revision != revision) throw new FoDataException("Le dossier a changé depuis l’affichage. Relisez-le avant de valider.");
            var now = DateTime.UtcNow;
            row.DiabetesManual = diabetes; row.DiabetesReviewedHash = diabetes.HasValue ? row.DiabetesEvidenceHash : "";
            row.DiabetesReviewedBy = diabetes.HasValue ? user : ""; row.DiabetesReviewedUtc = diabetes.HasValue ? now : null;
            row.HtoManual = hto; row.HtoReviewedHash = hto.HasValue ? row.HtoEvidenceHash : "";
            row.HtoReviewedBy = hto.HasValue ? user : ""; row.HtoReviewedUtc = hto.HasValue ? now : null;
            row.Revision = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync(ct);
        }
        finally { WriteGate.Release(); }
    }
}

public static class PatientRegistrySchema
{
    public static void Ensure(ChatDbContext db) => db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS PatientRegistrySettings (Id INTEGER NOT NULL PRIMARY KEY, MappingJson TEXT NOT NULL, SourceKey TEXT NOT NULL, Revision TEXT NOT NULL, LastImportUtc TEXT NULL, LastImportCount INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS PatientRegistry (
          Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SourceKey TEXT NOT NULL, AccessPatientId TEXT NOT NULL,
          LastName TEXT NOT NULL, FirstName TEXT NOT NULL, SearchName TEXT NOT NULL, IsActive INTEGER NOT NULL,
          ImportedUtc TEXT NOT NULL, LastConsultationDate TEXT NULL, Revision TEXT NOT NULL,
          DiabetesDetected INTEGER NOT NULL, DiabetesEvidence TEXT NOT NULL, DiabetesSourceDate TEXT NULL, DiabetesEvidenceHash TEXT NOT NULL,
          DiabetesManual INTEGER NULL, DiabetesReviewedHash TEXT NOT NULL, DiabetesReviewedBy TEXT NOT NULL, DiabetesReviewedUtc TEXT NULL,
          HtoDetected INTEGER NOT NULL, HtoEvidence TEXT NOT NULL, HtoSourceDate TEXT NULL, HtoEvidenceHash TEXT NOT NULL,
          HtoManual INTEGER NULL, HtoReviewedHash TEXT NOT NULL, HtoReviewedBy TEXT NOT NULL, HtoReviewedUtc TEXT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_PatientRegistry_SourceKey_AccessPatientId ON PatientRegistry (SourceKey,AccessPatientId);
        CREATE INDEX IF NOT EXISTS IX_PatientRegistry_SourceKey_IsActive_LastName ON PatientRegistry (SourceKey,IsActive,LastName);
        """);
}
