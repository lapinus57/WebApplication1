using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.SmsMail.Models
{
    public class RdvFamille
    {
        public int Numero { get; set; }
        public List<RdvInfo> RDVs { get; } = new();
        public List<string> Adresse { get; } = new();
        public List<string> Ville { get; } = new();
        public List<string> Nom { get; } = new();
        public List<string> Prenom { get; } = new();
        public List<string> Email { get; } = new();
        public List<string> TelList { get; } = new();
        public List<DateTime> DatePriseRDV { get; } = new();
        public List<DateTime> HeureRDV { get; } = new();
        public DateTime DateRDV { get; set; }
        public DateTime? DatePriseRDVMin { get; set; }
        public DateTime? DatePriseRDVMax { get; set; }
        public DateTime? DatePriseRDVMoy { get; set; }
        public bool IsolatedFamily { get; set; } = true;
        public string? Label { get; set; }

        public string TitleDisplay => "Famille " + Numero;
        public int PersonCount => RDVs.Select(r => r.Id > 0 ? "id:" + r.Id : "name:" + (r.Prenom ?? string.Empty).Trim() + "|" + (r.Nom ?? string.Empty).Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        public string CountDisplay => PersonCount + (PersonCount == 1 ? " personne" : " personnes");
        public DateTime FirstAppointment => RDVs.Count == 0 ? DateTime.MaxValue : RDVs.Min(r => r.HeureRDV);
        public IEnumerable<FamilyMemberRow> MemberRows => RDVs.OrderBy(r => r.HeureRDV).ThenBy(r => r.Nom).Select(r => new FamilyMemberRow
        {
            Name = ((r.Prenom ?? string.Empty) + " " + (r.Nom ?? string.Empty)).Trim(),
            Time = r.HeureRDV.ToString("HH:mm")
        });
        public string PhonesDisplay => JoinContacts(RDVs.SelectMany(r => new[] { r.Tel1, r.Tel2 }));
        public string EmailsDisplay => JoinContacts(RDVs.SelectMany(r => new[] { r.Email }.Concat(r.AdditionalEmails ?? new())));
        private static string JoinContacts(IEnumerable<string> contacts)
        {
            var values = contacts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return values.Length == 0 ? "—" : string.Join(Environment.NewLine, values);
        }
        public string GroupingDisplay => string.IsNullOrWhiteSpace(MethodsDisplay)
            ? (PersonCount == 1 ? "Personne seule" : "Non précisé") : MethodsDisplay;

        public string MembersDisplay => string.Join(Environment.NewLine, RDVs.OrderBy(r => r.HeureRDV).Select(r => $"{r.HeureRDV:HH:mm} — {r.Prenom} {r.Nom}"));
        public string ContactsDisplay => string.Join(" | ", RDVs.SelectMany(r => new[] { r.Tel1, r.Tel2, r.Email }.Concat(r.AdditionalEmails)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        public string MethodsDisplay => string.Join(", ", RDVs.SelectMany(r => new[] { r.TriedMethodRound1, r.TriedMethodRound2, r.TriedMethodRound3, r.TriedMethodRound4, r.TriedMethodRound5, r.TriedMethodRound6 }).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        public void UpdateStatistics()
        {
            if (DatePriseRDV.Count == 0)
            {
                DatePriseRDVMin = null;
                DatePriseRDVMax = null;
                DatePriseRDVMoy = null;
                return;
            }

            DatePriseRDVMin = DatePriseRDV.Min();
            DatePriseRDVMax = DatePriseRDV.Max();
            DatePriseRDVMoy = new DateTime((long)DatePriseRDV.Average(static d => d.Ticks));
        }
    }
    public sealed class FamilyMemberRow
    {
        public string Name { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }

}

