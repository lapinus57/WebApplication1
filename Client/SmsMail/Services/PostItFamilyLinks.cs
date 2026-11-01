using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services;

// Reads end-of-consultation post-it and finds who came together. Not used by the
// reminders yet: it only produces links that the cabinet checks first.
//
// Rule agreed with the cabinet:
//  - a person is cited by a line starting with "+Prénom", or a line starting with a first
//    name followed by a cotation or a follow-up ("Gabriel CS 1ok LUN"), or a line holding
//    only a first name; codes, follow-ups, amounts, dates, "cf" and free text are ignored;
//  - the cited person must have an appointment the same day, with the same first name;
//  - same surname: linked. Different surname: linked only if the appointments are less
//    than one hour apart. Several possible people: closest one; a tie gives no link.
public static class PostItFamilyLinks
{
    public static readonly TimeSpan DifferentSurnameWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan HistoryDepth = TimeSpan.FromDays(3 * 365);

    public sealed class Link
    {
        public DateTime Day { get; init; }
        public RdvInfo Owner { get; init; } = new();
        public RdvInfo Cited { get; init; } = new();
        public bool SameSurname { get; init; }
        public int MinutesApart { get; init; }
        public string Line { get; init; } = string.Empty;
    }

    public sealed class Unresolved
    {
        public DateTime Day { get; init; }
        public RdvInfo Owner { get; init; } = new();
        public string CitedName { get; init; } = string.Empty;
        public string Line { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    public const string NoCandidate = "Aucun rendez-vous avec ce prénom ce jour-là";
    public const string TooFar = "Nom différent et plus d'1 h d'écart";
    public const string Ambiguous = "Plusieurs patients possibles";

    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "CS", "OR", "V3F", "LUN", "GOU", "GOUT", "LEN", "DL", "OCT", "TOPO", "MT", "BO", "VP", "VL", "TIPS", "ORDO", "DE", "OK", "CF",
        "OD", "OG", "ODG", "FO", "CV", "AV", "PROG", "ADD", "DOSS", "DOSSIER", "LETTRE", "LUNETTES", "LENTILLES", "GOUTTES",
        "RDV", "RV", "AN", "ANS", "MOIS", "SEUL", "SEULE", "AVEC", "DANS", "ET", "OU", "LE", "LA", "LES", "UN", "UNE", "DU", "DES",
        "FAIRE", "DEJA", "FEUILLE", "ENLEVER", "FICHE", "AVIS", "METTRE", "DOUBLE", "FIXE", "PRIS", "ANNULE", "REVOIR", "BILAN",
        "GARDE", "MME", "MR", "MONSIEUR", "MADAME", "MARI", "FEMME", "FILLE", "FILS", "MAMAN", "PAPA", "FRERE", "SOEUR", "ENFANT",
        "ENFANTS", "MERE", "PERE", "EPOUSE", "EPOUX", "MERCI", "NORMALE", "BESOIN", "AUSSI", "MAIS", "DEPUIS", "JE", "J", "AI",
        "SON", "SA", "SES", "ELLE", "IL", "QUI", "CAR", "SUR", "POUR", "PAS", "NON", "OUI", "VU", "VUE", "REEDUCATION", "DIABETE",
        "ABLATION", "ENVOYER", "DONNER", "ADAPT", "RES", "RESUMER", "TOUS", "TOUT", "NOUVEAU", "NOUVELLE"
    };
    private static readonly Regex Separator = new(@"^[\s=*\-_#~]{3,}$", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"^[\p{L}][\p{L}'\-]*$", RegexOptions.Compiled);
    private static readonly Regex FollowUpOrCode = new(@"^(?:CS|OR|V3F|\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Names cited in one post-it, with the line they come from. Each name is a list of
    // folded words ("JEAN", "MICHEL"): a compound first name, or a first name and a surname.
    public static List<(List<string> Words, string Line)> CitedNames(string? text)
    {
        var result = new List<(List<string>, string)>();
        foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Separator.IsMatch(line)) continue;
            var plus = line.StartsWith('+');
            var rest = plus ? line.Substring(1).TrimStart() : line;
            var tokens = Regex.Split(rest, @"\s+").Where(t => t.Length > 0).ToList();
            var words = new List<string>();
            int i = 0;
            for (; i < tokens.Count && words.Count < 3; i++)
            {
                var token = tokens[i].TrimEnd(':', ',', ';', '.');
                if (!Word.IsMatch(token) || Codes.Contains(Fold(token))) break;
                words.AddRange(Fold(token).Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries));
            }
            if (words.Count == 0 || words.All(w => w.Length < 2)) continue;
            var after = string.Join(" ", tokens.Skip(i));
            // "+Prénom …", "Prénom CS 1ok", or a line holding only the name.
            if (plus || after.Length == 0 || FollowUpOrCode.IsMatch(after))
                result.Add((words, line));
        }
        return result;
    }

    public static (List<Link> Links, List<Unresolved> Unresolved) Build(IEnumerable<PostItAnalysis.PostIt> postIts, IEnumerable<RdvInfo> appointments)
    {
        var byDay = appointments.Where(a => !a.IsCancelled).GroupBy(a => a.DateRDV.Date).ToDictionary(g => g.Key, g => g.ToList());
        var links = new List<Link>();
        var unresolved = new List<Unresolved>();
        foreach (var postIt in postIts)
        {
            var day = postIt.Date.Date;
            var sameDay = byDay.TryGetValue(day, out var list) ? list : new List<RdvInfo>();
            var owner = sameDay.FirstOrDefault(a => a.Id > 0 && a.Id == postIt.PatientCode)
                ?? new RdvInfo { Id = postIt.PatientCode, Nom = postIt.Nom, Prenom = postIt.Prenom, DateRDV = day, HeureRDV = postIt.Date };
            var ownerFirst = NameWords(owner.Prenom);
            var ownerSurnames = NameWords(owner.Nom).Concat(NameWords(owner.NomJeuneFille)).ToHashSet();

            foreach (var (words, line) in CitedNames(postIt.Text))
            {
                // The patient's own line, written "Prénom" or "Nom Prénom".
                if (ownerFirst.Count > 0 && ContainsRun(words, ownerFirst)) continue;
                var candidates = sameDay
                    .Where(a => !ReferenceEquals(a, owner) && (a.Id <= 0 || a.Id != owner.Id))
                    .Select(a => (Rdv: a, First: NameWords(a.Prenom)))
                    .Where(c => c.First.Count > 0 && ContainsRun(words, c.First))
                    .Select(c =>
                    {
                        var surnames = NameWords(c.Rdv.Nom).Concat(NameWords(c.Rdv.NomJeuneFille)).ToHashSet();
                        var others = words.Except(c.First).ToList();
                        bool same = surnames.Overlaps(ownerSurnames) || (others.Count > 0 && surnames.Overlaps(others));
                        var minutes = (int)Math.Round(Math.Abs((c.Rdv.HeureRDV - owner.HeureRDV).TotalMinutes));
                        return (c.Rdv, Same: same, Minutes: minutes);
                    })
                    .GroupBy(c => c.Rdv.Id > 0 ? "id:" + c.Rdv.Id : "ref:" + c.Rdv.GetHashCode())
                    .Select(g => g.OrderBy(c => c.Minutes).First())
                    .ToList();
                var cited = string.Join(" ", words);
                if (candidates.Count == 0) { unresolved.Add(new() { Day = day, Owner = owner, CitedName = cited, Line = line, Reason = NoCandidate }); continue; }
                var accepted = candidates.Where(c => c.Same || c.Minutes < DifferentSurnameWindow.TotalMinutes).ToList();
                if (accepted.Count == 0) { unresolved.Add(new() { Day = day, Owner = owner, CitedName = cited, Line = line, Reason = TooFar }); continue; }
                var best = accepted.OrderByDescending(c => c.Same).ThenBy(c => c.Minutes).ToList();
                if (best.Count > 1 && best[1].Same == best[0].Same && best[1].Minutes == best[0].Minutes)
                { unresolved.Add(new() { Day = day, Owner = owner, CitedName = cited, Line = line, Reason = Ambiguous }); continue; }
                links.Add(new Link { Day = day, Owner = owner, Cited = best[0].Rdv, SameSurname = best[0].Same, MinutesApart = best[0].Minutes, Line = line });
            }
        }
        return (links, unresolved);
    }

    // Families formed by the links (A-B and B-C make one family), as sets of patient codes.
    public static List<HashSet<int>> Families(IEnumerable<Link> links)
    {
        var parent = new Dictionary<int, int>();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var link in links.Where(l => l.Owner.Id > 0 && l.Cited.Id > 0))
        {
            parent.TryAdd(link.Owner.Id, link.Owner.Id);
            parent.TryAdd(link.Cited.Id, link.Cited.Id);
            var a = Root(link.Owner.Id); var b = Root(link.Cited.Id);
            if (a != b) parent[a] = b;
        }
        return parent.Keys.GroupBy(Root).Select(g => g.ToHashSet()).ToList();
    }

    private static List<string> NameWords(string? value) =>
        Fold(value ?? string.Empty).Split(new[] { ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).ToList();

    // The first name ("JEAN MICHEL") appears as consecutive words of the cited name,
    // in first position ("+Jean Michel") or after a surname ("Vocije Cupasu").
    private static bool ContainsRun(List<string> words, List<string> run)
    {
        for (int start = 0; start + run.Count <= words.Count; start++)
            if (run.Select((r, i) => words[start + i] == r).All(x => x)) return true;
        return false;
    }

    private static string Fold(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.ToUpperInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return builder.ToString().Replace('’', '\'');
    }
}
