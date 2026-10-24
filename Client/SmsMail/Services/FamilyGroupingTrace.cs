using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services;

public static class FamilyGroupingTrace
{
    public static string Person(RdvInfo p) => $"{p.Prenom} {p.Nom} (patient {p.Id})".Trim();

    public static void Record(string rule, IEnumerable<RdvInfo> members)
    {
        var people = members.Distinct().ToList();
        if (people.Count < 2) return;
        var reason = rule + " : " + string.Join(", ", people.Select(Person));
        foreach (var person in people)
            if (!person.GroupingReasons.Contains(reason)) person.GroupingReasons.Add(reason);
    }

    // Record only effective membership changes; attempted matches and later
    // family renumbering are not evidence of an inclusion.
    public static void Run(string rule, List<RdvFamille> families, Action action)
    {
        var before = families.SelectMany(f => f.RDVs.Select(p => (p, members: f.RDVs.ToHashSet())))
            .ToDictionary(x => x.p, x => x.members);
        action();
        foreach (var family in families.Where(f => f.RDVs.Count > 1))
            if (family.RDVs.Any(p => !before.TryGetValue(p, out var old) || !old.SetEquals(family.RDVs)))
                Record(rule, family.RDVs);
    }
}
