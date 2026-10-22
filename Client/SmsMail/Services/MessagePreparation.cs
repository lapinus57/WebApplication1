using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public sealed class PreparedMessage
    {
        public bool Selected { get; set; } = true;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string ProviderService { get; set; } = string.Empty;
        public string Status { get; set; } = "À envoyer";
        public string Metrics => Channel == "SMS" ? MessagePreparation.SmsMetrics(Body) : "HTML";
    }

    public static class MessagePreparation
    {
        public static string NormalizePhone(string value)
        {
            value = Regex.Replace((value ?? string.Empty).Split('(')[0], @"[\s.\-()]", "");
            if (value.StartsWith("00", StringComparison.Ordinal)) value = "+" + value.Substring(2);
            if (Regex.IsMatch(value, @"^0[67]\d{8}$")) value = "+33" + value.Substring(1);
            return Regex.IsMatch(value, @"^\+[1-9]\d{7,14}$") ? value : string.Empty;
        }

        public static IEnumerable<string> Emails(string value)
        {
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"[^\s<>;,()]+@[^\s<>;,()]+\.[^\s<>;,()]+"))
                if (MailAddress.TryCreate(match.Value, out var address)) yield return address.Address.ToLowerInvariant();
        }

        public static List<PreparedMessage> Prepare(IEnumerable<RdvInfo> appointments, DateTime date, SmsMailSettings settings)
        {
            // Never infer a family from an address or a remark alone: shared contact
            // details are required before including several patients in one message.
            var rdvs = appointments.Where(r => r.DateRDV.Date == date.Date && !r.IsCancelled).OrderBy(r => r.HeureRDV).ToList();
            var result = new List<PreparedMessage>();
            AddChannel("Mail", r => Emails(r.Email).Concat(r.AdditionalEmails.SelectMany(Emails)).Distinct());
            AddChannel("SMS", r => settings.SmsOnlyWhenNoEmail && Emails(r.Email).Concat(r.AdditionalEmails.SelectMany(Emails)).Any()
                ? Array.Empty<string>() : new[] { NormalizePhone(r.Tel1), NormalizePhone(r.Tel2) }.Where(p => p.Length > 0).Distinct());
            return result;

            void AddChannel(string channel, Func<RdvInfo, IEnumerable<string>> contacts)
            {
                foreach (var group in rdvs.SelectMany(r => contacts(r).Select(c => (Rdv: r, Contact: c))).GroupBy(x => x.Contact, StringComparer.OrdinalIgnoreCase))
                {
                    var people = group.Select(x => x.Rdv).Distinct().OrderBy(r => r.HeureRDV).ToList();
                    var body = Render(channel == "SMS" ? settings.SmsTemplate : settings.EmailTemplate, people, settings.Cabinet, channel == "Mail");
                    // Stable across refreshes and template edits: an accepted reminder
                    // for this day/contact must not be submitted twice by accident.
                    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{date:yyyy-MM-dd}|{channel}|{group.Key}")));
                    result.Add(new PreparedMessage { Channel = channel, Contact = group.Key,
                        Family = string.Join(", ", people.Select(r => $"{r.Prenom} {r.Nom}")),
                        Subject = settings.Subject, Body = body, Key = key,
                        ProviderService = channel == "SMS" ? settings.Sms.ServiceName : settings.Email.SmtpHost });
                }
            }
        }

        public static string Render(string template, IReadOnlyList<RdvInfo> people, string cabinet, bool html)
        {
            if (people.Count == 0) return string.Empty;
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            var first = people.OrderBy(r => r.HeureRDV).First();
            string Encode(string value) => html ? WebUtility.HtmlEncode(value) : value;
            var names = string.Join(", ", people.Select(r => r.Prenom).Distinct());
            var persons = string.Join("", people.Select(r => $"<li>{WebUtility.HtmlEncode($"{r.Prenom} {r.Nom}")} — {r.HeureRDV:HH:mm}</li>"));
            var replacements = new Dictionary<string, string>
            {
                ["[PRENOMS]"] = Encode(names), ["[CABINET]"] = Encode(cabinet),
                ["[DATE_LONGUE]"] = first.DateRDV.ToString("dddd dd MMMM yyyy", culture),
                ["[DATE]"] = first.DateRDV.ToString("dd/MM/yyyy", culture),
                ["[HEURE]"] = first.HeureRDV.ToString("HH:mm", culture),
                ["[LISTE_PERSONNES]"] = html ? persons : string.Join(", ", people.Select(r => $"{r.Prenom} {r.Nom}"))
            };
            return Regex.Replace(template, @"\[[A-Z_]+\]", m => replacements.TryGetValue(m.Value, out var value) ? value : m.Value);
        }

        public static string SmsMetrics(string text)
        {
            const string basic = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
            const string extended = "^{}\\[~]|€";
            bool unicode = text.Any(c => !basic.Contains(c) && !extended.Contains(c));
            int length = unicode ? text.Length : text.Sum(c => extended.Contains(c) ? 2 : 1);
            int single = unicode ? 70 : 160, multiple = unicode ? 67 : 153;
            int segments = length == 0 ? 0 : length <= single ? 1 : (int)Math.Ceiling(length / (double)multiple);
            return $"{length} caractères · {segments} SMS · {(unicode ? "Unicode" : "GSM 7 bits")}";
        }
    }
}
