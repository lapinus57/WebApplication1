using System.Globalization;
using ChatServeur.Families;
using Client.SmsMail.Models;
using EyeChat.SmsMail;

namespace ChatServeur.SmsMail;

/// <summary>Post-it de famille du jour : la famille vient de la page Familles, les heures du jour de l'agenda (lu par le serveur).</summary>
public sealed class DayPostItService(DayPostItStore store, FamilyStore families, ISmsMailAgenda agenda)
{
    // L'agenda du jour est relu au plus une fois par minute : chaque ouverture de dossier ne relance pas Access.
    private static readonly object CacheGate = new();
    private static (DateTime Day, DateTime ReadAt, List<RdvInfo> Rows)? _cache;

    private async Task<List<RdvInfo>> TodayAsync(DateTime day)
    {
        lock (CacheGate)
            if (_cache is { } c && c.Day == day && DateTime.UtcNow - c.ReadAt < TimeSpan.FromMinutes(1)) return c.Rows;
        List<RdvInfo> rows;
        try { rows = await agenda.DayAsync(day); }
        catch (Exception) { rows = new(); } // Access indisponible : le post-it reste utilisable, sans les heures.
        lock (CacheGate) _cache = (day, DateTime.UtcNow, rows);
        return rows;
    }

    /// <summary>Famille du patient (ou le patient seul), avec les noms connus.</summary>
    private List<DayPostItMember> Family(int patientId, IReadOnlyList<RdvInfo> today)
    {
        var membership = families.Memberships(new[] { patientId }).FirstOrDefault();
        var stored = membership != null ? families.Families(number: membership.FamilyNumber).FirstOrDefault() : null;
        var ids = membership?.Members.Count > 0 ? membership.Members : new List<int> { patientId };
        return ids.Select(id => new DayPostItMember
        {
            PatientId = id,
            Name = today.FirstOrDefault(r => r.Id == id) is { } r ? $"{r.Prenom} {r.Nom}".Trim() : stored?.NameOf(id) ?? id.ToString(CultureInfo.InvariantCulture),
        }).ToList();
    }

    private static void AddTimes(DayPostIt note, IReadOnlyList<RdvInfo> today)
    {
        foreach (var m in note.Members)
        {
            var rdv = today.Where(r => r.Id == m.PatientId && !r.IsCancelled).OrderBy(r => r.HeureRDV).FirstOrDefault();
            m.AppointmentTime = rdv?.HeureRDV;
            if (rdv != null) m.Name = $"{rdv.Prenom} {rdv.Nom}".Trim();
        }
        // Ceux qui viennent aujourd'hui d'abord, dans l'ordre des heures.
        note.Members = note.Members.OrderBy(m => m.AppointmentTime ?? DateTime.MaxValue).ThenBy(m => m.Name).ToList();
    }

    /// <summary>Post-it du jour du patient ouvert : l'existant, sinon un post-it vide pour sa famille.</summary>
    public async Task<DayPostIt> GetAsync(int patientId, DateTime day)
    {
        var today = await TodayAsync(day.Date);
        var note = store.Find(patientId, day.Date) ?? new DayPostIt { PatientId = patientId, Day = day.Date, Members = Family(patientId, today) };
        AddTimes(note, today);
        return note;
    }

    /// <param name="excluded">Membres retirés à la main avant la première saisie : ils ne partagent pas ce post-it.</param>
    public async Task<List<int>> SaveAsync(int patientId, DateTime day, string text, string color, string author, IReadOnlyCollection<int>? excluded = null)
    {
        if ((text ?? "").Length > 4000) throw new InvalidOperationException("Post-it trop long (4000 caractères au plus).");
        var today = await TodayAsync(day.Date);
        var family = Family(patientId, today).Where(m => m.PatientId == patientId || excluded == null || !excluded.Contains(m.PatientId)).ToList();
        return await store.SaveAsync(patientId, day.Date, (text ?? "").TrimEnd(), string.IsNullOrWhiteSpace(color) ? "#FFF8B0" : color, author, family);
    }

    public Task<List<int>> DetachAsync(int patientId, DateTime day) => store.DetachAsync(patientId, day.Date);
}
