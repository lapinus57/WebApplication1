using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services;

// Says, in words a secretary recognises, why several people share one message.
public static class GroupingExplanation
{
    public const string Alone = "Personne seule";

    public static string Explain(IReadOnlyList<RdvInfo> people, Func<RdvInfo, int>? familyOf = null)
    {
        var persons = people.GroupBy(PersonKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (persons.Count < 2) return Alone;
        var reasons = new List<string>();

        if (persons.Where(p => p.ReferenceFamilyNumber > 0).GroupBy(p => p.ReferenceFamilyNumber).Any(g => g.Count() > 1))
            reasons.Add("Famille EyeEmail");

        var phones = Shared(persons, p => new[] { p.Tel1, p.Tel2 }.Select(FamilyUtilities.GroupingPhone)).Select(FormatPhone).ToList();
        if (phones.Count > 0) reasons.Add((phones.Count == 1 ? "Même téléphone : " : "Mêmes téléphones : ") + string.Join(", ", phones));
        var mails = Shared(persons, p => FamilyUtilities.GroupingEmails(p.Email)).ToList();
        if (mails.Count > 0) reasons.Add((mails.Count == 1 ? "Même e-mail : " : "Mêmes e-mails : ") + string.Join(", ", mails));

        var sameAddress = persons
            .Where(p => !string.IsNullOrWhiteSpace(p.Adresse) && !string.IsNullOrWhiteSpace(p.Ville))
            .GroupBy(p => AddressNormalizer.NormalizeAddress(p.Adresse) + "|" + AddressNormalizer.NormalizeAddress(p.Ville))
            .Where(g => g.Count() > 1).ToList();
        foreach (var group in sameAddress)
            reasons.Add("Même adresse : " + group.First().Adresse.Trim() + ", " + group.First().Ville.Trim());
        if (sameAddress.Count == 0 && persons.Any(p => p.TriedMethodRound2 == "Adresse et Ville"))
            reasons.Add("Adresse proche (même ville, faute de frappe probable)");

        foreach (var person in persons.Where(p => IsRemarkLink(p)))
            reasons.Add($"Remarque de {Name(person)} : « {Shorten(person.Remarque)} »");

        foreach (var label in persons.Select(p => p.TriedMethodRound6).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct())
            reasons.Add(label);

        if (reasons.Count == 0)
            reasons.Add(familyOf != null && persons.Select(familyOf).Distinct().Count() > 1 ? "Même destinataire" : "Même famille");
        return string.Join(Environment.NewLine, reasons.Distinct());
    }

    private static IEnumerable<string> Shared(IEnumerable<RdvInfo> persons, Func<RdvInfo, IEnumerable<string>> values) =>
        persons.SelectMany(p => values(p).Where(v => v.Length > 0).Distinct().Select(v => (Value: v, Person: p)))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Person).Distinct().Count() > 1)
            .Select(g => g.Key);

    private static bool IsRemarkLink(RdvInfo person) =>
        new[] { person.TriedMethodRound3, person.TriedMethodRound4, person.TriedMethodRound5 }.Contains("Remarque")
        && person.Remarque.Contains("avec", StringComparison.OrdinalIgnoreCase);

    private static string PersonKey(RdvInfo r) => r.Id > 0 ? "id:" + r.Id : "name:" + (r.Nom ?? string.Empty).Trim() + "|" + (r.Prenom ?? string.Empty).Trim();
    private static string Name(RdvInfo r) => $"{r.Prenom} {r.Nom}".Trim();

    private static string Shorten(string text)
    {
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 60 ? text : text.Substring(0, 59) + "…";
    }

    private static string FormatPhone(string digits) =>
        digits.Length == 10 && digits[0] == '0'
            ? string.Join(" ", Enumerable.Range(0, 5).Select(i => digits.Substring(i * 2, 2)))
            : digits;
}
