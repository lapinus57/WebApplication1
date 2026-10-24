using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.SmsMail.Models;
using EyeChat.Messaging;

namespace Client.SmsMail.Services;

public static class MailTracePreparation
{
    public static string CreateReference() => string.Concat(Enumerable.Range(0, 20).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));

    public static string Footer(string body, string reference)
    {
        var footer = "<p style=\"font-size:12px;color:#555\">Référence du mail : <strong>" + MailReference.Display(reference)
            + "</strong><br>En cas d’erreur dans la liste des personnes, communiquez cette référence au cabinet.</p>";
        var end = body.LastIndexOf("</body", StringComparison.OrdinalIgnoreCase);
        if (end < 0) end = body.LastIndexOf("</html", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? body + footer : body.Insert(end, footer);
    }

    public static string Snapshot(List<RdvInfo> people, List<RdvFamille> families, Dictionary<RdvInfo, DateTime> originalTimes, string destination, bool grouping)
    {
        bool Owns(RdvInfo r) => MessagePreparation.Emails(r.Email).Concat(r.AdditionalEmails.SelectMany(MessagePreparation.Emails))
            .Contains(destination, StringComparer.OrdinalIgnoreCase);
        var trace = new MailTrace { PreparedAtUtc = DateTimeOffset.UtcNow, GroupingEnabled = grouping };
        foreach (var person in people)
        {
            var family = families.Single(f => f.RDVs.Contains(person));
            var owners = family.RDVs.Where(Owns).ToList();
            string Source(RdvInfo r) => r.EyeEmailEmails.Contains(destination, StringComparer.OrdinalIgnoreCase)
                ? "coordonnées présentes dans la base familles EyeEmail" : "coordonnées du rendez-vous Access";
            trace.People.Add(new MailTracePerson
            {
                PatientId = person.Id, Name = $"{person.Prenom} {person.Nom}".Trim(),
                AppointmentAt = person.DateRDV.Date + originalTimes[person].TimeOfDay,
                AnnouncedAt = person.DateRDV.Date + person.HeureRDV.TimeOfDay,
                FamilyNumber = family.Numero, ReferenceFamilyNumber = person.ReferenceFamilyNumber,
                DestinationReason = Owns(person) ? "Adresse destinataire présente dans ses " + Source(person)
                    : "Inclus avec sa famille ; adresse destinataire portée par " + string.Join(", ", owners.Select(r => FamilyGroupingTrace.Person(r) + " — " + Source(r))),
                GroupingReasons = grouping ? person.GroupingReasons.ToList() : new()
            });
        }
        var json = JsonSerializer.Serialize(trace);
        MailTrace.Parse(json); // Fail before reserving/sending if the snapshot cannot be retained.
        return json;
    }

    public static string Describe(string json)
    {
        if (string.IsNullOrEmpty(json)) return "Ancien envoi : les raisons d’inclusion n’étaient pas enregistrées. Elles ne peuvent pas être reconstituées avec certitude.";
        var trace = MailTrace.Parse(json);
        var output = new StringBuilder();
        output.AppendLine(trace.IsTest ? "MESSAGE DE TEST — personnes fictives." : "Composition enregistrée lors de la préparation du mail.");
        output.AppendLine("Préparé le " + trace.PreparedAtUtc.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("fr-FR")));
        output.AppendLine(trace.GroupingEnabled ? "Regroupement par famille activé." : "Regroupement par famille désactivé ; les rendez-vous partageant la même adresse mail restent réunis.");
        foreach (var p in trace.People)
        {
            output.AppendLine(); output.AppendLine($"{p.Name} · patient {p.PatientId}");
            output.AppendLine($"Rendez-vous : {p.AppointmentAt:dd/MM/yyyy HH:mm} · heure annoncée : {p.AnnouncedAt:HH:mm}");
            if (trace.GroupingEnabled) output.AppendLine($"Groupe de cette préparation : {p.FamilyNumber}" + (p.ReferenceFamilyNumber > 0 ? $" · famille EyeEmail : {p.ReferenceFamilyNumber}" : ""));
            output.AppendLine(p.DestinationReason);
            if (p.GroupingReasons.Count == 0 && trace.GroupingEnabled)
                output.AppendLine(trace.People.Count == 1 ? "Aucun autre membre inclus dans ce mail." : "Aucune étape de regroupement enregistrée pour cette personne.");
            foreach (var reason in p.GroupingReasons) output.AppendLine("• " + reason);
        }
        return output.ToString();
    }
}
