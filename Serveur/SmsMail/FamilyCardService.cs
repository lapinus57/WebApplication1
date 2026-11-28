using System.Globalization;
using ChatServeur.EyeMail;
using ChatServeur.Families;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using EyeChat.SmsMail;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.SmsMail;

/// <summary>
/// Fiche « RDV de la famille » : le serveur trouve le patient (base patients EyeMail : nom, prénom, date de naissance,
/// sinon les patients qui ont un RDV à venir), sa famille (page Familles) et les RDV à venir de chacun (agenda).
/// </summary>
public sealed class FamilyCardService(EyeMailDbContext patients, FamilyStore families, ISmsMailAgenda agenda,
    SmsMailServerSettings settings, SmsMailHistoryStore history)
{
    /// <summary>Jusqu'où chercher les rendez-vous à venir.</summary>
    public const int HorizonDays = 400;

    public async Task<List<FamilyCardCandidate>> SearchAsync(string query, DateTime today)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return new();
        var birth = ParseDate(query);
        var words = birth.HasValue ? Array.Empty<string>() : EyeMailColumns.SearchText(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<FamilyCardCandidate>();
        var q = patients.Patients.AsNoTracking().Where(p => p.IsActive);
        if (birth.HasValue) q = q.Where(p => p.BirthDate == birth.Value);
        else foreach (var word in words) q = q.Where(p => p.SearchName.Contains(word));
        foreach (var p in await q.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(40).ToListAsync())
            if (int.TryParse(p.AccessPatientId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                found.Add(new FamilyCardCandidate { PatientId = id, Name = $"{p.FirstName} {p.LastName}".Trim(), BirthDate = p.BirthDate, City = p.City });

        // Base patients vide (import pas encore fait) : patients ayant un RDV à venir, par leur nom.
        if (found.Count == 0 && !birth.HasValue && !await patients.Patients.AnyAsync())
            foreach (var r in (await agenda.RangeAsync(today, today.AddDays(HorizonDays)))
                         .Where(r => r.Id > 0 && words.All(w => EyeMailColumns.SearchText(r.Nom, r.Prenom, r.NomJeuneFille).Contains(w)))
                         .GroupBy(r => r.Id).Select(g => g.First()).Take(40))
                found.Add(new FamilyCardCandidate { PatientId = r.Id, Name = $"{r.Prenom} {r.Nom}".Trim(), City = r.Ville });

        var sizes = families.Memberships(found.Select(f => f.PatientId)).ToDictionary(m => m.PatientId, m => m.Members.Count);
        foreach (var f in found) f.FamilySize = Math.Max(1, sizes.GetValueOrDefault(f.PatientId, 1));
        return found;
    }

    /// <summary>Date de naissance saisie : 05/03/1980, 5/3/80, 05031980 ou 1980-03-05.</summary>
    public static DateTime? ParseDate(string text)
    {
        var t = text.Trim();
        if (DateTime.TryParseExact(t, new[] { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "ddMMyyyy", "yyyy-MM-dd", "d.M.yyyy", "d-M-yyyy" },
                CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d) && d.Year > 1900)
            return d.Year > DateTime.Today.Year ? d.AddYears(-100) : d.Date;
        return null;
    }

    public async Task<FamilyCard> BuildAsync(int patientId, DateTime today)
    {
        var s = settings.Load();
        var membership = families.Memberships(new[] { patientId }).FirstOrDefault();
        var ids = membership?.Members.Count > 0 ? membership.Members : new List<int> { patientId };
        var stored = membership != null ? families.Families(number: membership.FamilyNumber).FirstOrDefault() : null;
        var codes = ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var known = await patients.Patients.AsNoTracking().Where(p => codes.Contains(p.AccessPatientId)).ToListAsync();
        var rdvs = (await agenda.RangeAsync(today, today.AddDays(HorizonDays))).Where(r => ids.Contains(r.Id) && r.HeureRDV >= DateTime.Now.AddMinutes(-30)).ToList();

        string NameOf(int id)
        {
            var p = known.FirstOrDefault(k => k.AccessPatientId == id.ToString(CultureInfo.InvariantCulture));
            if (p != null) return $"{p.FirstName} {p.LastName}".Trim();
            var r = rdvs.FirstOrDefault(x => x.Id == id);
            if (r != null) return $"{r.Prenom} {r.Nom}".Trim();
            return stored?.NameOf(id) ?? id.ToString(CultureInfo.InvariantCulture);
        }

        var card = new FamilyCard
        {
            Cabinet = s?.Cabinet ?? "", CabinetAddress = s?.CabinetAddress ?? "", Specialty = s?.CabinetSpecialty ?? "",
            Phone = s?.CabinetPhone ?? "", Fax = s?.CabinetFax ?? "", Contact = s?.CabinetContact ?? "", PatientId = patientId, GeneratedAt = DateTime.Now,
            Members = ids.Select(id =>
            {
                var p = known.FirstOrDefault(k => k.AccessPatientId == id.ToString(CultureInfo.InvariantCulture));
                var r = rdvs.FirstOrDefault(x => x.Id == id);
                return new FamilyCardMember
                {
                    PatientId = id, Name = NameOf(id), BirthDate = p?.BirthDate,
                    Title = p?.Civility is { Length: > 0 } c ? c : r?.Titre ?? "",
                    LastName = p?.LastName ?? r?.Nom ?? "", FirstName = p?.FirstName ?? r?.Prenom ?? "",
                };
            }).OrderBy(m => m.PatientId == patientId ? 0 : 1).ThenBy(m => m.Name).ToList(),
            Appointments = rdvs.GroupBy(r => (r.Id, r.HeureRDV)).Select(g => g.First())
                .Select(r => new FamilyCardAppointment { PatientId = r.Id, Name = NameOf(r.Id), Start = r.HeureRDV }).OrderBy(a => a.Start).ToList(),
        };
        card.Emails = known.Select(k => k.Email).Concat(rdvs.Select(r => r.Email))
            .SelectMany(e => (e ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(e => System.Net.Mail.MailAddress.TryCreate(e, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return card;
    }

    /// <summary>Envoie la fiche par mail (réglages SMTP du serveur), avec une trace dans l'historique commun.</summary>
    public async Task<string> SendMailAsync(int patientId, string email, DateTime today)
    {
        if (!System.Net.Mail.MailAddress.TryCreate((email ?? "").Trim(), out var address)) throw new InvalidOperationException("Adresse mail invalide.");
        var s = settings.Load() ?? throw new InvalidOperationException(SmsMailDelivery.NotConfigured);
        var card = await BuildAsync(patientId, today);
        var message = new PreparedMessage
        {
            Key = $"fiche-famille-{patientId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}", Channel = "Mail", Contact = address.Address,
            Subject = $"{FamilyCardDocument.Title(card)} — {s.Cabinet}", Body = FamilyCardDocument.Html(card, forMail: true),
            ProviderService = s.Email.SmtpHost, IncludeCalendar = false, PeopleDisplay = string.Join(", ", card.Members.Select(m => m.Name)),
        };
        var mail = new MailService(s.Email);
        mail.Validate();
        if (!await history.ReserveAsync(new HistoryReservation { Key = message.Key, Channel = "Mail", Contact = message.Contact, Body = message.Body, ProviderService = message.ProviderService }))
            throw new InvalidOperationException("Envoi déjà en cours.");
        string status;
        try { await mail.SendAsync(message); status = "Fiche famille — acceptée par SMTP"; }
        catch (Exception ex) { status = "Fiche famille — non confirmée : " + ex.Message; }
        await history.UpdateAsync(message.Key, status);
        return status;
    }
}
