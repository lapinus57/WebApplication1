using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services;

// One-off export of end-of-consultation post-it (table tPostIT) so their wording can be
// studied before using them to group families. Only post-it that look like a consultation
// note are kept: first names, cotation (CS, OR, V3F…) and follow-up ("1 ok", "4-8-12").
public static class PostItAnalysis
{
    public const string Format = "eyechat-postit-analysis";
    public const string LinksFormat = "eyechat-postit-links";
    public const string Table = "tPostIT";

    public sealed class PostIt
    {
        public int PatientCode { get; init; }
        public DateTime Date { get; init; }
        public string Text { get; init; } = string.Empty;
        public string Nom { get; init; } = string.Empty;
        public string Prenom { get; init; } = string.Empty;
    }

    private static readonly Regex Cotation = new(@"(?<![\p{L}\d])(CS|OR|V3F)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FollowUp = new(@"(?<![\p{L}\d])\d{1,2}(?:\s*-\s*\d{1,2})*\s*ok(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsConsultationPostIt(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (Cotation.IsMatch(text) || FollowUp.IsMatch(text));

    public static string BuildQuery(IReadOnlyCollection<string> tables, Func<string, IReadOnlyCollection<string>> columns, bool withPatients)
    {
        var table = AppointmentQuery.Find(tables, Table)
            ?? throw new InvalidOperationException($"Table « {Table} » introuvable dans la base Access. Tables contenant « post » : "
                + string.Join(", ", tables.Where(t => t.Contains("post", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty("aucune")));
        var cols = columns(table);
        string Need(string name) => AppointmentQuery.Pick(cols, name) ?? throw new InvalidOperationException(
            $"Champ « {name} » introuvable dans « {table} ». Champs disponibles : {string.Join(", ", cols.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))}.");
        var code = Need("CodePat");
        var date = Need("DatePI");
        var text = Need("MessageText");
        var select = $"SELECT t.{AppointmentQuery.Q(code)} AS PostItPatient, t.{AppointmentQuery.Q(date)} AS PostItDate, t.{AppointmentQuery.Q(text)} AS PostItText";
        var from = $" FROM {AppointmentQuery.Q(table)} t";
        var patients = withPatients ? AppointmentQuery.Find(tables, "Patients") : null;
        if (patients != null)
        {
            var pc = columns(patients);
            var key = AppointmentQuery.Pick(pc, AppointmentQuery.PatientKeyNames);
            var nom = AppointmentQuery.Pick(pc, "Nom");
            var prenom = AppointmentQuery.Pick(pc, "Prénom", "Prenom");
            if (key != null && nom != null && prenom != null)
            {
                select += $", p.{AppointmentQuery.Q(nom)} AS PatientNom, p.{AppointmentQuery.Q(prenom)} AS PatientPrenom";
                from += $" LEFT JOIN {AppointmentQuery.Q(patients)} p ON t.{AppointmentQuery.Q(code)} = p.{AppointmentQuery.Q(key)}";
            }
        }
        return select + from + $" WHERE t.{AppointmentQuery.Q(date)} >= ? AND t.{AppointmentQuery.Q(date)} < ? ORDER BY t.{AppointmentQuery.Q(date)}";
    }

    // Every surname and first name of the Patients table: used only to recognise names
    // inside the post-it text, never exported.
    public static string BuildNamesQuery(IReadOnlyCollection<string> tables, Func<string, IReadOnlyCollection<string>> columns)
    {
        var patients = AppointmentQuery.Find(tables, "Patients") ?? throw new InvalidOperationException("Table « Patients » introuvable.");
        var pc = columns(patients);
        var nom = AppointmentQuery.Pick(pc, "Nom") ?? throw new InvalidOperationException("Champ « Nom » introuvable dans « Patients ».");
        var prenom = AppointmentQuery.Pick(pc, "Prénom", "Prenom") ?? throw new InvalidOperationException("Champ « Prénom » introuvable dans « Patients ».");
        return $"SELECT DISTINCT {AppointmentQuery.Q(nom)} AS PatientNom, {AppointmentQuery.Q(prenom)} AS PatientPrenom FROM {AppointmentQuery.Q(patients)}";
    }

    public static string BuildJson(IReadOnlyList<PostIt> all, IEnumerable<(string Nom, string Prenom)> knownNames, DateTime from, DateTime to, bool pseudonymize)
    {
        var kept = all.Where(p => IsConsultationPostIt(p.Text)).OrderBy(p => p.Date).ToList();
        var nameRows = knownNames.Select(n => new RdvInfo { Nom = n.Nom ?? string.Empty, Prenom = n.Prenom ?? string.Empty })
            .Concat(kept.Select(p => new RdvInfo { Id = p.PatientCode, Nom = p.Nom, Prenom = p.Prenom }));
        var ps = pseudonymize ? new Pseudonymizer(nameRows) : null;
        var snapshot = new
        {
            format = Format,
            version = 1,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pseudonymized = pseudonymize,
            totalPostIts = all.Count,
            keptPostIts = kept.Count,
            postits = kept.Select(p => new
            {
                Patient = ps?.Id(p.PatientCode) ?? p.PatientCode,
                Date = p.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Nom = ps?.Name(p.Nom) ?? p.Nom,
                Prenom = ps?.Name(p.Prenom) ?? p.Prenom,
                Texte = ps?.Text(p.Text) ?? p.Text
            })
        };
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    // Report of the family links found in the post-it, for the cabinet to check before
    // the links are ever used for reminders.
    public static string BuildLinksReportJson(IReadOnlyList<PostIt> all, IReadOnlyList<RdvInfo> appointments,
        IEnumerable<(string Nom, string Prenom)> knownNames, DateTime from, DateTime to, bool pseudonymize)
    {
        var kept = all.Where(p => IsConsultationPostIt(p.Text)).OrderBy(p => p.Date).ToList();
        var (links, unresolved) = PostItFamilyLinks.Build(kept, appointments);
        var families = PostItFamilyLinks.Families(links);
        var nameRows = knownNames.Select(n => new RdvInfo { Nom = n.Nom ?? string.Empty, Prenom = n.Prenom ?? string.Empty })
            .Concat(appointments).Concat(kept.Select(p => new RdvInfo { Id = p.PatientCode, Nom = p.Nom, Prenom = p.Prenom }));
        var ps = pseudonymize ? new Pseudonymizer(nameRows) : null;
        object Person(RdvInfo r) => new
        {
            Patient = ps?.Id(r.Id) ?? r.Id, Nom = ps?.Name(r.Nom) ?? r.Nom, Prenom = ps?.Name(r.Prenom) ?? r.Prenom,
            Heure = r.HeureRDV == default ? string.Empty : r.HeureRDV.ToString("HH:mm", CultureInfo.InvariantCulture)
        };
        string Line(string text) => ps?.Text(text) ?? text;
        var report = new
        {
            format = LinksFormat,
            version = 1,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pseudonymized = pseudonymize,
            summary = new
            {
                postIts = all.Count, consultationPostIts = kept.Count, appointments = appointments.Count,
                citedNames = kept.Sum(p => PostItFamilyLinks.CitedNames(p.Text).Count),
                links = links.Count, sameSurname = links.Count(l => l.SameSurname), differentSurname = links.Count(l => !l.SameSurname),
                unresolved = unresolved.GroupBy(u => u.Reason).ToDictionary(g => g.Key, g => g.Count()),
                families = families.GroupBy(f => f.Count).OrderBy(g => g.Key).ToDictionary(g => g.Key + " personnes", g => g.Count())
            },
            links = links.Select(l => new
            {
                Jour = l.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PostItDe = Person(l.Owner), Cite = Person(l.Cited),
                MemeNom = l.SameSurname, EcartMinutes = l.MinutesApart, Ligne = Line(l.Line)
            }),
            unresolved = unresolved.Select(u => new
            {
                Jour = u.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PostItDe = Person(u.Owner),
                Cite = Line(u.CitedName), Ligne = Line(u.Line), Motif = u.Reason
            }),
            familles = families.Select(f => f.Select(id => ps?.Id(id) ?? id).OrderBy(id => id))
        };
        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    // Post-it may be stored as RTF: keep the text and line breaks only.
    public static string PlainText(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal)) return value ?? string.Empty;
        var text = Regex.Replace(value, @"\{\\\*[^{}]*\}|\{\\(?:fonttbl|colortbl|stylesheet|info)(?:[^{}]|\{[^{}]*\})*\}", string.Empty);
        text = Regex.Replace(text, @"\\par[d]?\b ?|\\line\b ?", "\n");
        text = Regex.Replace(text, @"\\'([0-9a-fA-F]{2})", m => Encoding.Latin1.GetString(new[] { Convert.ToByte(m.Groups[1].Value, 16) }));
        text = Regex.Replace(text, @"\\u(-?\d+)\??", m => ((char)(short)int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToString());
        text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?|[{}]", string.Empty);
        text = text.Replace(@"\\", @"\");
        return text.Trim();
    }
}
