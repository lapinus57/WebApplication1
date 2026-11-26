using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;
using EyeChat.SmsMail;

namespace Client.SmsMail.Services;

/// <summary>
/// Familles calculées par le serveur (tout l'historique de l'agenda, carte Vitale, paiements, corrections faites sur la
/// page « Familles ») appliquées aux rendez-vous du jour. Une famille « à vérifier » n'est regroupée que si la personne
/// qui envoie l'accepte, après avoir vu pourquoi.
/// </summary>
public static class ServerFamilies
{
    /// <summary>Numéros à part de ceux de la base EyeEmail, pour ne jamais mélanger les deux.</summary>
    public const int NumberOffset = 1_000_000;

    /// <summary>Réponse donnée à une famille à vérifier, à transmettre au serveur pour ne plus poser la question.</summary>
    public sealed record Answer(IReadOnlyList<int> PatientIds, bool Together);

    public static int ReferenceNumber(FamilyMembershipDto m) => NumberOffset + m.FamilyNumber * 100 + Math.Clamp(m.SubGroup, 0, 99);

    public static int Apply(IReadOnlyList<RdvInfo> rows, DateTime day, IReadOnlyList<FamilyMembershipDto> memberships, Func<string, string, bool>? confirm,
        List<Answer>? answers = null, Action<string>? log = null)
    {
        var byPatient = memberships.GroupBy(m => m.PatientId).ToDictionary(g => g.Key, g => g.First());
        var today = rows.Where(r => r.DateRDV.Date == day.Date && !r.IsCancelled && r.Id > 0 && byPatient.ContainsKey(r.Id)).ToList();
        int applied = 0;
        foreach (var family in today.GroupBy(r => ReferenceNumber(byPatient[r.Id])))
        {
            var people = family.GroupBy(r => r.Id).Select(g => g.First()).ToList();
            if (people.Count < 2) continue;
            // Déjà réunis par la base EyeEmail : on ne touche à rien.
            if (people.All(r => r.ReferenceFamilyNumber > 0)) continue;
            var info = byPatient[people[0].Id];
            var names = people.ToDictionary(r => r.Id, r => $"{r.Prenom} {r.Nom}".Trim());
            var why = Explain(people.Select(r => r.Id).ToList(), info, id => names[id]);
            if (info.ToReview)
            {
                var list = string.Join(", ", people.Select(r => $"{names[r.Id]} ({r.HeureRDV:HH:mm})"));
                var message = $"Le serveur range ces personnes dans la même famille : {list}." + Environment.NewLine + Environment.NewLine
                    + "Pourquoi :" + Environment.NewLine + why + Environment.NewLine + Environment.NewLine
                    + "Regrouper leurs rappels dans un seul message ? Votre réponse est enregistrée sur le serveur : "
                    + "la question ne sera plus posée pour ces personnes, sur aucun poste. "
                    + $"(Famille n° {info.FamilyNumber} sur la page « Familles » de l'administration du serveur.)";
                if (confirm == null) continue;
                var together = confirm(message, "Famille à vérifier");
                answers?.Add(new Answer(people.Select(p => p.Id).OrderBy(id => id).ToList(), together));
                log?.Invoke(AnonymousLog(day, people, info, together));
                if (!together) continue;
            }
            foreach (var row in rows.Where(r => people.Any(p => p.Id == r.Id)))
            {
                if (row.ReferenceFamilyNumber == 0) row.ReferenceFamilyNumber = ReferenceNumber(info);
                row.ReferenceFamilyWhy = why;
            }
            applied++;
        }
        return applied;
    }

    /// <summary>
    /// Pourquoi ces personnes sont regroupées, en ne citant qu'elles : leurs liens directs, et les liens par lesquels elles se
    /// rejoignent quand il faut passer par un autre membre de la famille (« une autre personne de la famille »).
    /// </summary>
    public static string Explain(IReadOnlyList<int> present, FamilyMembershipDto info, Func<int, string> nameOf)
    {
        if (info.Links.Count == 0) return info.Why; // serveur d'une version précédente
        var here = present.ToHashSet();
        var neighbours = info.Links.SelectMany(l => new[] { (From: l.A, To: l.B, Link: l), (From: l.B, To: l.A, Link: l) })
            .ToLookup(x => x.From);
        var used = new List<FamilyLinkDto>();
        void Use(FamilyLinkDto l) { if (!used.Contains(l)) used.Add(l); }
        foreach (var l in info.Links.Where(l => here.Contains(l.A) && here.Contains(l.B))) Use(l);
        // Chemin le plus court depuis la première personne vers chacune des autres, quand il n'y a pas de lien direct.
        var start = present[0];
        var previous = new Dictionary<int, FamilyLinkDto?> { [start] = null };
        var queue = new Queue<int>(new[] { start });
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var (_, to, link) in neighbours[current])
                if (!previous.ContainsKey(to)) { previous[to] = link; queue.Enqueue(to); }
        }
        foreach (var target in present.Skip(1))
        {
            if (info.Links.Any(l => (l.A == start && l.B == target) || (l.B == start && l.A == target))) continue;
            for (var node = target; previous.TryGetValue(node, out var link) && link != null; node = link.A == node ? link.B : link.A) Use(link);
        }
        var others = used.SelectMany(l => new[] { l.A, l.B }).Where(id => !here.Contains(id)).Distinct().ToList();
        string Label(int id) => here.Contains(id) ? nameOf(id)
            : others.Count == 1 ? "une autre personne de la famille (absente ce jour)"
            : $"une autre personne de la famille ({others.IndexOf(id) + 1}, absente ce jour)";
        var lines = used.Select(l => $"{Label(l.A)} et {Label(l.B)} : {l.Reasons}").ToList();
        if (info.ToReview && info.ReviewReason.Length > 0) lines.Insert(0, "À vérifier : " + info.ReviewReason);
        if (others.Count > 0) lines.Add($"Ces personnes ne sont reliées que par l'intermédiaire de {(others.Count == 1 ? "quelqu'un d'autre" : others.Count + " autres personnes")} de la famille.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Une question et sa réponse, sans aucun nom : numéros patients, heures, relations de toute la famille.</summary>
    public static string AnonymousLog(DateTime day, IReadOnlyList<RdvInfo> people, FamilyMembershipDto info, bool together)
    {
        string Code(int id) => "n° " + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm} — RDV du {day:dd/MM/yyyy} — famille {info.FamilyNumber} ({info.Members.Count} personnes) ===");
        sb.AppendLine("Question : " + string.Join(", ", people.Select(r => $"{Code(r.Id)} ({r.HeureRDV:HH:mm})")));
        sb.AppendLine("À vérifier : " + (info.ReviewReason.Length > 0 ? info.ReviewReason : "oui"));
        sb.AppendLine("Pourquoi (affiché) :");
        foreach (var line in Explain(people.Select(r => r.Id).ToList(), info, Code).Split(Environment.NewLine)) sb.AppendLine("  " + line);
        sb.AppendLine("Membres de la famille : " + string.Join(", ", info.Members.Select(Code)));
        sb.AppendLine("Relations de la famille :");
        foreach (var l in info.Links) sb.AppendLine($"  {Code(l.A)} – {Code(l.B)} ({l.Strength.ToLowerInvariant()}) : {l.Reasons}");
        sb.AppendLine("Réponse : " + (together ? "Regrouper" : "Garder séparés"));
        return sb.ToString();
    }
}
