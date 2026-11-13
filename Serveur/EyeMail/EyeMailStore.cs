using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.EyeMail;

/// <summary>Ce qu'une source (Access) fournit : la table lue, les colonnes reconnues et les patients.</summary>
public sealed record EyeMailSourceData(string Table, IReadOnlyDictionary<string, string> Columns, IReadOnlyList<EyeMailSourcePatient> Patients);

public interface IEyeMailSource
{
    EyeMailSourceData ReadPatients(Action<int>? progress, CancellationToken ct);
}

/// <summary>Recopie la liste complète des patients dans eyemail.db, en une seule transaction.</summary>
public sealed class EyeMailStore(EyeMailDbContext db)
{
    public const int MaxPatients = 200_000;
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public async Task<EyeMailImportRun> SyncAsync(EyeMailSourceData source, CancellationToken ct)
    {
        if (source.Patients.Count > MaxPatients)
            throw new InvalidDataException($"Plus de {MaxPatients} patients : vérifiez la table Access lue ({source.Table}).");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patient in source.Patients)
            if (!ids.Add(patient.AccessPatientId))
                throw new InvalidDataException($"L'identifiant patient {patient.AccessPatientId} apparaît deux fois dans Access : import annulé, rien n'a été modifié.");

        await WriteGate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var run = new EyeMailImportRun { StartedUtc = now, SourceTable = source.Table, Read = source.Patients.Count };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.Patients.ToDictionaryAsync(p => p.AccessPatientId, StringComparer.Ordinal, ct);

            foreach (var incoming in source.Patients)
            {
                ct.ThrowIfCancellationRequested();
                var hash = Hash(incoming);
                if (!existing.TryGetValue(incoming.AccessPatientId, out var row))
                {
                    row = new EyeMailPatient { AccessPatientId = incoming.AccessPatientId, CreatedUtc = now, UpdatedUtc = now };
                    Apply(row, incoming, hash);
                    db.Patients.Add(row);
                    run.Added++;
                }
                else
                {
                    if (row.ContentHash != hash)
                    {
                        Apply(row, incoming, hash);
                        row.UpdatedUtc = now;
                        run.Updated++;
                    }
                    if (!row.IsActive) run.Reactivated++;
                }
                row.IsActive = true;
                row.LastSeenUtc = now;
            }

            // Absent d'Access : gardé (historique des envois, familles…), mais marqué inactif.
            foreach (var row in existing.Values.Where(r => r.IsActive && !ids.Contains(r.AccessPatientId)))
            {
                row.IsActive = false;
                run.Deactivated++;
            }

            run.FinishedUtc = DateTime.UtcNow;
            db.ImportRuns.Add(run);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return run;
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private static void Apply(EyeMailPatient row, EyeMailSourcePatient p, string hash)
    {
        row.Civility = p.Civility; row.LastName = p.LastName; row.FirstName = p.FirstName; row.MaidenName = p.MaidenName;
        row.BirthDate = p.BirthDate; row.Address = p.Address; row.Address2 = p.Address2; row.PostalCode = p.PostalCode; row.City = p.City;
        row.Phone1 = p.Phone1; row.Phone1Info = p.Phone1Info; row.Phone2 = p.Phone2; row.Phone2Info = p.Phone2Info;
        row.Email = p.Email; row.EmailInfo = p.EmailInfo;
        row.SearchName = EyeMailColumns.SearchText(p.LastName, p.FirstName, p.MaidenName, p.AccessPatientId);
        row.ContentHash = hash;
    }

    private static string Hash(EyeMailSourcePatient p)
    {
        var text = string.Join("\u001f", p.Civility, p.LastName, p.FirstName, p.MaidenName, p.BirthDate?.ToString("yyyy-MM-dd") ?? "",
            p.Address, p.Address2, p.PostalCode, p.City, p.Phone1, p.Phone1Info, p.Phone2, p.Phone2Info, p.Email, p.EmailInfo);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Création de eyemail.db au démarrage du serveur.</summary>
public static class EyeMailSchema
{
    public static void Ensure(EyeMailDbContext db)
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        // Les prochaines tables (familles, contacts, envois) s'ajouteront ici avec CREATE TABLE IF NOT EXISTS :
        // EnsureCreated ne modifie pas une base qui existe déjà.
    }
}
