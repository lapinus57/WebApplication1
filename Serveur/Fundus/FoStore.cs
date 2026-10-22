using System.Text.Json;
using EyeChat.Fundus;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Fundus;

public interface IFoAccessReader
{
    DateTime Now { get; }
    IReadOnlyList<FoField> Inspect();
    string SourceKey(FoMapping mapping);
    FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken cancellationToken);
    FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId);
}

public sealed class FoStore(ChatDbContext db, IFoAccessReader reader)
{
    // Prevent overlapping refresh/configuration/review requests from overwriting a newer decision.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<FoSettings?> SettingsAsync(CancellationToken ct) =>
        await db.FoSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct);

    public async Task SaveMappingAsync(FoMapping mapping, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var fields = reader.Inspect();
            mapping.SelectClinicalFields(fields);
            mapping.Validate(fields);
            var key = reader.SourceKey(mapping);
            var settings = await db.FoSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
            if (settings == null) { settings = new(); db.FoSettings.Add(settings); }
            settings.MappingJson = JsonSerializer.Serialize(mapping);
            settings.SourceKey = key;
            await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }

    public async Task<int> RefreshAsync(DateTime from, DateTime until, CancellationToken ct)
    {
        ValidateRange(from, until, reader.Now.Date);
        await Gate.WaitAsync(ct);
        try
        {
            var settings = await db.FoSettings.SingleOrDefaultAsync(s => s.Id == 1, ct)
                ?? throw new FoDataException("Choisissez et enregistrez les champs Access avant l’analyse.");
            var mapping = JsonSerializer.Deserialize<FoMapping>(settings.MappingJson)!;
            var previousKey = settings.SourceKey;
            if (reader.SourceKey(mapping) != previousKey) throw new FoDataException("La source Access a changé. Vérifiez puis enregistrez à nouveau les champs.");
            // Upgrade saved configurations before reading any clinical text. ProchainRDV is no longer read.
            mapping.SelectClinicalFields(reader.Inspect());
            var source = reader.Load(mapping, from, until, ct);
            if (source.SourceKey != reader.SourceKey(mapping)) throw new FoDataException("La source Access a changé pendant la lecture. Relancez l’analyse.");
            // Analyze everything before opening a write transaction: no partial refresh on a read failure.
            var analyses = source.Patients.Select(p => (Patient: p, Result: FundusAnalysis.Analyze(p.Consultations, reader.Now))).ToList();
            var now = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.FoAlerts.Where(a => a.SourceKey == source.SourceKey || a.SourceKey == previousKey).ToListAsync(ct);
            foreach (var alert in existing.Where(a => a.NextAppointment >= from && a.NextAppointment < until))
                alert.NextAppointment = null; // moved/cancelled slots disappear from this period even without a new consultation
            foreach (var (patient, result) in analyses)
            {
                var alert = existing.FirstOrDefault(a => a.PatientId == patient.Id && a.SourceKey == source.SourceKey)
                    ?? existing.SingleOrDefault(a => a.PatientId == patient.Id && a.SourceKey == previousKey);
                if (result == null)
                {
                    if (alert != null) db.FoAlerts.Remove(alert);
                    continue;
                }
                if (alert == null)
                {
                    alert = new FoAlert { SourceKey = source.SourceKey, PatientId = patient.Id };
                    db.FoAlerts.Add(alert);
                }
                if (alert.EvidenceHash != result.EvidenceHash || alert.SourceConsultationId != result.SourceId)
                {
                    alert.ManualStatus = null; alert.ReviewedUtc = null; alert.ReviewedBy = "";
                }
                alert.SourceKey = source.SourceKey;
                alert.PatientName = patient.Name;
                alert.NextAppointment = patient.Appointment;
                alert.SourceConsultationId = result.SourceId;
                alert.SourceDate = result.SourceDate;
                alert.Excerpt = result.Excerpt;
                alert.Status = result.Status;
                alert.Reason = result.Reason;
                alert.EvidenceHash = result.EvidenceHash;
                alert.LaterExcerpt = result.LaterExcerpt;
                alert.LaterDate = result.LaterDate;
                alert.LastAnalysisUtc = now;
            }
            settings.MappingJson = JsonSerializer.Serialize(mapping);
            settings.SourceKey = source.SourceKey;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return analyses.Count;
        }
        finally { Gate.Release(); }
    }

    public async Task ReviewAsync(int id, string evidenceHash, FoStatus? status, string user, CancellationToken ct)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new FoDataException("État inconnu.");
        await Gate.WaitAsync(ct);
        try
        {
            var settings = await SettingsAsync(ct) ?? throw new FoDataException("Configurez les champs Access.");
            var alert = await db.FoAlerts.SingleOrDefaultAsync(a => a.Id == id && a.SourceKey == settings.SourceKey, ct);
            if (alert == null || settings == null) throw new FoDataException("Alerte indisponible. Rechargez la liste.");
            if (alert.EvidenceHash != evidenceHash) throw new FoDataException("L’analyse a changé depuis l’affichage. Relisez les nouveaux éléments avant de valider.");
            alert.ManualStatus = status;
            alert.ReviewedUtc = status == null ? null : DateTime.UtcNow;
            alert.ReviewedBy = status == null ? "" : user;
            await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }

    public static void ValidateRange(DateTime from, DateTime until, DateTime today)
    {
        if (from.Date < today || until <= from || (until.Date - from.Date).TotalDays > 93 || from.TimeOfDay != TimeSpan.Zero || until.TimeOfDay != TimeSpan.Zero)
            throw new FoDataException("Choisissez une période à partir d’aujourd’hui, de 1 à 93 jours.");
    }
}

public static class FoSchema
{
    // EnsureCreated does not add tables to an existing SQLite database. These additions are idempotent.
    public static void Ensure(ChatDbContext db) => db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS FoSettings (Id INTEGER NOT NULL PRIMARY KEY, MappingJson TEXT NOT NULL, SourceKey TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS FoAlerts (
 Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SourceKey TEXT NOT NULL, PatientId TEXT NOT NULL,
 PatientName TEXT NOT NULL, NextAppointment TEXT NULL, SourceConsultationId TEXT NOT NULL,
 SourceDate TEXT NOT NULL, Excerpt TEXT NOT NULL, Status INTEGER NOT NULL, Reason TEXT NOT NULL,
 LaterExcerpt TEXT NOT NULL, LaterDate TEXT NULL, EvidenceHash TEXT NOT NULL, LastAnalysisUtc TEXT NOT NULL,
 ManualStatus INTEGER NULL, ReviewedUtc TEXT NULL, ReviewedBy TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS IX_FoAlerts_SourceKey_PatientId ON FoAlerts (SourceKey, PatientId);
CREATE INDEX IF NOT EXISTS IX_FoAlerts_NextAppointment ON FoAlerts (NextAppointment);");
}
