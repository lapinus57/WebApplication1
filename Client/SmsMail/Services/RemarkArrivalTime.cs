using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services;

public sealed record ArrivalTimeDecision(DateTime Time, string Reason, bool NeedsReview = false);

// Interpret only explicit arrival instructions, never an arbitrary number in a remark.
// This is applied to reminder copies; the Access appointment is never changed.
public static class RemarkArrivalTime
{
    private const string Cue = @"\b(?:venir|arriver|arrivee|se presenter|convoque(?:e|s|es)?)\b";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);
    private static bool Has(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, Timeout);

    public static ArrivalTimeDecision Resolve(RdvInfo appointment, IReadOnlyDictionary<TimeSpan, TimeSpan> rules)
    {
        var original = appointment.HeureRDV;
        var fallback = ReminderTimes.Apply(original, rules);
        var normal = new ArrivalTimeDecision(fallback, fallback == original ? "Horaire du rendez-vous" : "Règle générale");
        if (string.IsNullOrWhiteSpace(appointment.Remarque)) return normal;
        ArrivalTimeDecision Review() => new(original, "Remarque horaire à vérifier", true);
        if (appointment.Remarque.Length > 8000) return Review();
        try
        {
            var text = new string(appointment.Remarque.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
                .ToLowerInvariant().Replace('’', '\'');
            if (!Has(text, Cue)) return normal;
            if (!Has(text, @"\d\s*(?:h|:)|\b(?:heure|horaire)\b")) return normal;
            // Negation, alternatives and other dates must be resolved by a person.
            if (Has(text, @"\b(?:pas|ne|non|plus|jamais|annule\w*|peut[ -]etre|si|ou|demain|hier|lendemain|avant|apres|vers|entre)\b|\?|\b\d{1,2}[/.-]\d{1,2}\b|\b(?:lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)\b|\ble\s+\d{1,2}\b|\b(?:souhait\w*|voudr\w*|pourr\w*|prefer\w*|possible|eventuel\w*|prochain\w*|suivant\w*|semaine|mois)\b")) return Review();
            var matches = Regex.Matches(text, Cue + @"\s*(?:(?:a|pour)\s*)?(?<h>\d{1,2})\s*(?:h(?:eures?)?\s*(?<m>\d{1,2})?|:\s*(?<m>\d{2}))(?!\d)", RegexOptions.CultureInvariant, Timeout);
            if (matches.Count == 0) return Review();
            var times = new List<TimeSpan>();
            foreach (Match match in matches)
            {
                int hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                int minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
                if (hour > 23 || minute > 59) return Review();
                times.Add(new TimeSpan(hour, minute, 0));
            }
            if (times.Distinct().Count() != 1) return Review();
            // A second time not attached to the same clear instruction is ambiguous.
            var remainder = Regex.Replace(text, Cue + @"\s*(?:(?:a|pour)\s*)?\d{1,2}\s*(?:h(?:eures?)?\s*\d{0,2}|:\s*\d{2})(?!\d)", "", RegexOptions.CultureInvariant, Timeout);
            // An explicit "au lieu de" is allowed only when it names the real appointment.
            remainder = Regex.Replace(remainder, @"\bau lieu de\s*(?<h>\d{1,2})\s*(?:h\s*(?<m>\d{1,2})?|:\s*(?<m>\d{2}))(?!\d)", m =>
                int.TryParse(m.Groups["h"].Value, out var h) && h == original.Hour
                && (m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0) == original.Minute
                    ? "" : m.Value, RegexOptions.CultureInvariant, Timeout);
            if (Has(remainder, @"\d\s*(?:h|:)|\b(?:janvier|fevrier|mars|avril|mai|juin|juillet|aout|septembre|octobre|novembre|decembre)\b")) return Review();
            return new(original.Date + times[0], "Remarque : heure de venue explicite");
        }
        catch (RegexMatchTimeoutException) { return Review(); }
    }
}
