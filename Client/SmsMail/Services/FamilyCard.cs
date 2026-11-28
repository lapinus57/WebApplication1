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
        public string Display => Name + (BirthDate.HasValue ? $" · né(e) le {BirthDate:dd/MM/yyyy}" : "") + (City.Length > 0 ? $" · {City}" : "")
            + (FamilySize > 1 ? $" · famille de {FamilySize}" : "");
    }

    public sealed class FamilyCardMember
    {
        public int PatientId { get; set; }
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
        public int PatientId { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<FamilyCardMember> Members { get; set; } = new();
        public List<FamilyCardAppointment> Appointments { get; set; } = new();
        /// <summary>Adresses mail connues des membres, pour proposer l'envoi.</summary>
        public List<string> Emails { get; set; } = new();
    }

    /// <summary>Mise en page de la fiche (impression A4 et mail) : la même sur le poste et sur le serveur.</summary>
    public static class FamilyCardDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        public static string Title(FamilyCard card) => card.Members.Count > 1 ? "Rendez-vous de la famille" : "Vos rendez-vous";

        public static string Html(FamilyCard card, bool forMail = false)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>").Append(E(Title(card))).Append("</title><style>")
              .Append("@page{size:A4;margin:18mm 16mm}body{font-family:'Segoe UI',Arial,sans-serif;color:#1d2b3f;margin:0;padding:24px;font-size:14px;line-height:1.45}")
              .Append(".head{display:flex;justify-content:space-between;gap:16px;border-bottom:3px solid #1f5fbf;padding-bottom:12px;margin-bottom:18px}")
              .Append(".cab{font-size:18px;font-weight:700}.addr{color:#5b6b80}.title{font-size:22px;font-weight:700;margin:6px 0 2px}")
              .Append(".who{color:#5b6b80;margin-bottom:16px}table{width:100%;border-collapse:collapse;margin-top:8px}")
              .Append("th{text-align:left;font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:#5b6b80;border-bottom:2px solid #d9e1ec;padding:8px 6px}")
              .Append("td{padding:10px 6px;border-bottom:1px solid #e6ebf2;vertical-align:top}td.d{white-space:nowrap;font-weight:600}td.h{white-space:nowrap;font-weight:700;color:#1f5fbf;font-size:16px}")
              .Append(".none{padding:16px;background:#f4f7fb;border-radius:8px}.foot{margin-top:22px;color:#5b6b80;font-size:12px}")
              .Append("@media print{body{padding:0}}</style></head><body>");
            sb.Append("<div class=\"head\"><div><div class=\"cab\">").Append(E(card.Cabinet)).Append("</div><div class=\"addr\">").Append(E(card.CabinetAddress))
              .Append("</div></div><div class=\"addr\">Édité le ").Append(card.GeneratedAt.ToString("dd/MM/yyyy", Fr)).Append("</div></div>");
            sb.Append("<div class=\"title\">").Append(E(Title(card))).Append("</div><div class=\"who\">")
              .Append(E(string.Join(", ", card.Members.Select(m => m.Name)))).Append("</div>");
            var upcoming = card.Appointments.OrderBy(a => a.Start).ThenBy(a => a.Name).ToList();
            if (upcoming.Count == 0) sb.Append("<div class=\"none\">Aucun rendez-vous à venir.</div>");
            else
            {
                var many = card.Members.Count > 1;
                sb.Append("<table><thead><tr><th>Date</th><th>Heure</th>").Append(many ? "<th>Pour</th>" : "").Append("</tr></thead><tbody>");
                foreach (var a in upcoming)
                {
                    var day = a.Start.ToString("dddd d MMMM yyyy", Fr);
                    sb.Append("<tr><td class=\"d\">").Append(E(char.ToUpper(day[0], Fr) + day[1..])).Append("</td><td class=\"h\">")
                      .Append(a.Start.ToString("HH:mm", Fr)).Append("</td>").Append(many ? "<td>" + E(a.Name) + "</td>" : "").Append("</tr>");
                }
                sb.Append("</tbody></table>");
            }
            sb.Append("<div class=\"foot\">En cas d'empêchement, merci de prévenir le cabinet au plus tôt.")
              .Append(forMail ? " Ce message est envoyé automatiquement, merci de ne pas y répondre." : "").Append("</div></body></html>");
            return sb.ToString();
        }
    }
}
