using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Client.SmsMail.Services
{
    /// <summary>Patient trouvé par la recherche de la fiche famille (nom, prénom ou date de naissance).</summary>
    public sealed class FamilyCardCandidate
    {
        public int PatientId { get; set; }
        public string Name { get; set; } = "";
        public DateTime? BirthDate { get; set; }
        public string City { get; set; } = "";
        /// <summary>Nombre de personnes de sa famille (1 s'il n'est dans aucune famille).</summary>
        public int FamilySize { get; set; } = 1;
        /// <summary>Téléphones connus (« 06 12 34 56 78 · portable »), portables d'abord : pour le message individuel.</summary>
        public List<string> Phones { get; set; } = new();
        public string Display => Name + (BirthDate.HasValue ? $" · né(e) le {BirthDate:dd/MM/yyyy}" : "") + (City.Length > 0 ? $" · {City}" : "")
            + (FamilySize > 1 ? $" · famille de {FamilySize}" : "");
    }

    public sealed class FamilyCardMember
    {
        public int PatientId { get; set; }
        /// <summary>Civilité (Madame, Monsieur…), nom et prénom séparés : la fiche écrit « Madame WINCKEL Sandra ».</summary>
        public string Title { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime? BirthDate { get; set; }
    }

    public sealed class FamilyCardAppointment
    {
        public int PatientId { get; set; }
        public string Name { get; set; } = "";
        public DateTime Start { get; set; }
    }

    /// <summary>Fiche des rendez-vous à venir d'une famille, préparée par le serveur (familles + agenda).</summary>
    public sealed class FamilyCard
    {
        public string Cabinet { get; set; } = "";
        public string CabinetAddress { get; set; } = "";
        public string Specialty { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Fax { get; set; } = "";
        public string Contact { get; set; } = "";
        public int PatientId { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<FamilyCardMember> Members { get; set; } = new();
        public List<FamilyCardAppointment> Appointments { get; set; } = new();
        /// <summary>Adresses mail connues des membres, pour proposer l'envoi.</summary>
        public List<string> Emails { get; set; } = new();
    }

    /// <summary>
    /// Mise en page de la fiche (impression A4 et mail), la même sur le poste et sur le serveur, dans le style de la fiche de
    /// rendez-vous du cabinet : en-tête du cabinet à gauche, patient(s) à droite, puis les rendez-vous en gras.
    /// </summary>
    public static class FamilyCardDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        /// <summary>La fiche sans certaines personnes (retirées à la main) : ni dans l'en-tête, ni dans les rendez-vous.</summary>
        public static FamilyCard Without(FamilyCard card, IEnumerable<int>? excluded)
        {
            var removed = (excluded ?? Array.Empty<int>()).ToHashSet();
            if (removed.Count == 0) return card;
            return new FamilyCard
            {
                Cabinet = card.Cabinet, CabinetAddress = card.CabinetAddress, Specialty = card.Specialty, Phone = card.Phone, Fax = card.Fax,
                Contact = card.Contact, PatientId = card.PatientId, GeneratedAt = card.GeneratedAt, Emails = card.Emails,
                Members = card.Members.Where(m => !removed.Contains(m.PatientId)).ToList(),
                Appointments = card.Appointments.Where(a => !removed.Contains(a.PatientId)).ToList(),
            };
        }

        public static string Title(FamilyCard card) => card.Members.Count > 1 ? "Rendez-vous de la famille" : "Votre prochain rendez-vous";

        /// <summary>« Mme » → « Madame », « M. » → « Monsieur »… ; une civilité inconnue est gardée telle quelle.</summary>
        public static string Civility(string? title)
        {
            var t = (title ?? "").Trim().TrimEnd('.').ToUpperInvariant();
            return t switch
            {
                "MME" or "MADAME" or "MRS" => "Madame",
                "M" or "MR" or "MONSIEUR" => "Monsieur",
                "MLLE" or "MADEMOISELLE" => "Mademoiselle",
                "" => "",
                _ => (title ?? "").Trim(),
            };
        }

        /// <summary>« Madame WINCKEL Sandra » (ou le nom tel qu'il est connu).</summary>
        public static string Addressee(FamilyCardMember m)
        {
            var name = m.LastName.Length > 0 || m.FirstName.Length > 0 ? $"{m.LastName.ToUpper(Fr)} {m.FirstName}".Trim() : m.Name;
            var civility = Civility(m.Title);
            return civility.Length > 0 ? civility + " " + name : name;
        }

        /// <summary>Le prénom ; si deux membres ont le même prénom, le prénom et le nom.</summary>
        private static string DisplayFirstName(FamilyCardMember m, IReadOnlyList<FamilyCardMember> all)
        {
            var first = m.FirstName.Length > 0 ? m.FirstName : m.Name;
            return all.Count(o => string.Equals(o.FirstName, m.FirstName, StringComparison.OrdinalIgnoreCase)) > 1 && m.LastName.Length > 0
                ? $"{first} {m.LastName.ToUpper(Fr)}" : first;
        }

        /// <summary>« Jacques », « Jacques et Lucie », « Jacques, Lucie et Jeanne ».</summary>
        public static string JoinNames(IReadOnlyList<string> names)
            => names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join(", ", names.Take(names.Count - 1)) + " et " + names[^1];

        /// <summary>« 37, Rue de la République, 57 320 BOUZONVILLE » → rue, puis code postal et ville.</summary>
        public static List<string> AddressLines(string address)
        {
            var m = System.Text.RegularExpressions.Regex.Match(address ?? "", @"^(?<street>.*?)[,\s]+(?<city>\d{2}\s?\d{3}\s+.+)$");
            return (m.Success ? new[] { m.Groups["street"].Value, m.Groups["city"].Value } : new[] { address ?? "" })
                .Select(x => x.Trim().TrimEnd(',')).Where(x => x.Length > 0).ToList();
        }

        public static string Html(FamilyCard card, bool forMail = false)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>").Append(E(Title(card))).Append("</title><style>")
              .Append("@page{size:A4;margin:0}")
              .Append("body{font-family:Arial,Helvetica,sans-serif;color:#000;margin:0;padding:24px 28px;font-size:14px;line-height:1.3;max-width:760px}")
              .Append(".head{display:flex;justify-content:space-between;align-items:flex-start;gap:24px;padding-bottom:8px;border-bottom:1px solid #d0d0d0}")
              .Append(".cab b{display:block;font-size:17px}.cab .spe,.cab .adr{width:max-content;min-width:250px;border-bottom:1px solid #000;padding-bottom:2px;margin-bottom:5px}")
              .Append(".cab .adr{font-style:italic;font-size:13px}")
              .Append(".cab .tel{font-style:italic;font-weight:700;font-size:13px}.to{font-weight:700;font-size:15px;text-align:right}.to div+div{margin-top:3px}")
              .Append(".intro{font-size:17px;margin:12px 0 6px 10px}.rdv{display:flex;flex-wrap:nowrap;align-items:baseline;gap:24px;margin:8px 0 2px 60px;font-size:19px;white-space:nowrap}")
              .Append(".rdv .d{font-weight:700;min-width:270px}.rdv .h b{font-weight:700}.for{margin:0 0 8px 90px;font-size:15px}")
              .Append(".none{margin:10px 0 10px 90px;font-size:16px}.note{font-style:italic;font-size:13px;margin:12px 0 0 12px;padding-bottom:10px;border-bottom:1px solid #d0d0d0}")
              .Append("@media print{body{padding:14mm 16mm;max-width:none}}</style></head><body>");

            // En-tête : cabinet à gauche, patient(s) à droite.
            var address = AddressLines(card.CabinetAddress);
            sb.Append("<div class=\"head\"><div class=\"cab\"><b>").Append(E(card.Cabinet)).Append("</b>");
            if (card.Specialty.Length > 0) sb.Append("<div class=\"spe\">").Append(E(card.Specialty)).Append("</div>");
            if (address.Count > 0) sb.Append("<div class=\"adr\">").Append(string.Join("<br>", address.Select(E))).Append("</div>");
            var lines = new List<string>();
            if (card.Phone.Length > 0) lines.Add("TEL : " + E(card.Phone));
            if (card.Fax.Length > 0) lines.Add("FAX : " + E(card.Fax));
            if (card.Contact.Length > 0) lines.Add("CONTACT RDV : " + E(card.Contact));
            if (lines.Count > 0) sb.Append("<div class=\"tel\">").Append(string.Join("<br>", lines)).Append("</div>");
            sb.Append("</div><div class=\"to\">");
            foreach (var m in card.Members) sb.Append("<div>").Append(E(Addressee(m))).Append("</div>");
            sb.Append("</div></div>");

            var upcoming = card.Appointments.OrderBy(a => a.Start).ThenBy(a => a.Name).ToList();
            var many = card.Members.Count > 1;
            sb.Append("<div class=\"intro\">").Append(upcoming.Count > 1 ? "Vos prochains rendez-vous sont fixés au :" : "Votre prochain rendez-vous est fixé au :").Append("</div>");
            if (upcoming.Count == 0) sb.Append("<div class=\"none\">Aucun rendez-vous à venir.</div>");
            // Même jour : une seule ligne à la première heure (la famille vient ensemble), puis « Pour Jacques, Lucie et Jeanne ».
            foreach (var slot in upcoming.GroupBy(a => a.Start.Date))
            {
                var first = slot.Min(a => a.Start);
                sb.Append("<div class=\"rdv\"><span class=\"d\">").Append(E(first.ToString("dddd d MMMM yyyy", Fr))).Append("</span>")
                  .Append("<span class=\"h\">à : <b>").Append(first.ToString("HH:mm", Fr)).Append(" heures</b></span></div>");
                if (many)
                {
                    var names = slot.OrderBy(a => a.Start).ThenBy(a => card.Members.FindIndex(m => m.PatientId == a.PatientId) is var i && i >= 0 ? i : int.MaxValue)
                        .Select(a => card.Members.FirstOrDefault(m => m.PatientId == a.PatientId) is { } m ? DisplayFirstName(m, card.Members) : a.Name)
                        .Distinct().ToList();
                    sb.Append("<div class=\"for\">Pour ").Append(E(JoinNames(names))).Append("</div>");
                }
            }
            sb.Append("<div class=\"note\">En cas d'empêchement, merci de nous prévenir le plus rapidement possible.")
              .Append(forMail ? " Ce message est envoyé automatiquement." : "").Append("</div></body></html>");
            return sb.ToString();
        }
    }
}
