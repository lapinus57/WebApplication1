using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

internal static class FoTiming
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    // Numbers in letters are matched on normalized text (accents removed, hyphens turned into spaces).
    private const string Number = @"\d{1,3}|un|une|deux|trois|quatre|cinq|six|sept|huit|neuf|dix huit|dix|onze|douze|quinze|vingt quatre|trente six";
    // "1 an 1/2", "1an1/2", "1 an et demi", "6 mois ½" add half a unit.
    internal const string Interval = @"\b(?:dans|ds|d['’]ici|a)\s+(?<n>" + Number + @")\s*(?<u>mois|mos|m|ans?|annees?|semaines?|jours?)(?:\s*(?<h>et\s+demie?|1\s*/\s*2|½))?(?!\w)";

    internal sealed record Timing(DateTime? Due, bool Ambiguous);

    public static DateTime? Due(FoConsultation source, string instruction) => Read(source, instruction).Due;

    public static Timing Read(FoConsultation source, string instruction)
    {
        var dates = ParseDates(source.Date, instruction).Distinct().ToList();
        if (dates.Count == 1) return new(dates[0], false);
        if (dates.Count > 1) return new(null, true);
        // Only a standalone follow-up sentence in the same visit is transferable.
        // A deadline attached to tonus, OCT, cataract, etc. is never assigned to FO.
        var general = FoClinicalContext.Clauses(source.Text).Concat(FoClinicalContext.Clauses(source.RefractionText))
            .Where(c => Regex.IsMatch(FundusAnalysis.Normalize(c), @"^(?:=>\s*)?revoir\s+" + Interval + @"\s*[.!]?$", RegexOptions.CultureInvariant, Limit));
        dates = general.SelectMany(c => ParseDates(source.Date, c)).Distinct().ToList();
        return new(dates.Count == 1 ? dates[0] : null, dates.Count > 1);
    }

    private static IEnumerable<DateTime> ParseDates(DateTime source, string text)
    {
        foreach (Match match in Regex.Matches(FundusAnalysis.Normalize(text), Interval, RegexOptions.CultureInvariant, Limit))
        {
            int n = match.Groups["n"].Value switch
            { "un" or "une" => 1, "deux" => 2, "trois" => 3, "quatre" => 4, "cinq" => 5, "six" => 6, "sept" => 7, "huit" => 8,
                "neuf" => 9, "dix" => 10, "onze" => 11, "douze" => 12, "quinze" => 15, "dix huit" => 18, "vingt quatre" => 24, "trente six" => 36,
                var number => int.Parse(number, CultureInfo.InvariantCulture) };
            if (n == 0) continue;
            var unit = match.Groups["u"].Value;
            bool half = match.Groups["h"].Success;
            DateTime? date = null;
            try
            {
                date = unit.StartsWith("an", StringComparison.Ordinal) ? source.Date.AddMonths(n * 12 + (half ? 6 : 0))
                    : unit.StartsWith("m", StringComparison.Ordinal) ? source.Date.AddMonths(n).AddDays(half ? 15 : 0)
                    : source.Date.AddDays(unit.StartsWith("semaine", StringComparison.Ordinal) ? n * 7 + (half ? 3 : 0) : n);
            }
            catch (ArgumentOutOfRangeException) { }
            if (date.HasValue) yield return date.Value;
        }
    }
}
