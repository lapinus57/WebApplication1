using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

// Cabinet follow-up rule: these documented contexts suggest a review, never a prescription.
internal sealed class FoAnnualFollowUp
{
    private enum Condition { Diabetes, OcularHypertension }
    private sealed record Mention(Condition Condition, FoConsultation Source, string Excerpt, bool Negative);
    private readonly Dictionary<Condition, Mention> current = new();
    private FoConsultation? lastCompletion;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private const string Diabetes = @"\b(?:diabete|diabetiques?|did|dnid)\b";
    private const string OcularHypertension = @"\b(?:h\.?\s*t\.?\s*o\.?|hyper(?:tension|tonie)\s+(?:intra\s*)?oculaire|tension\s+(?:intra\s*)?oculaire\s+(?:elevee|haute))\b";
    private const string Family = @"\b(?:familia\w*|mere|pere|parents?|frere|soeur|fils|fille|conjoint\w*|epou[sx]\w*)\b";
    private const string Uncertain = @"\?|\b(?:si|possible|suspect\w*|suspicion|risque|depist\w*|recherch\w*|a confirmer|a eliminer|a exclure|non exclu\w*|eventuel\w*)\b";
    private static bool Has(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, Limit);

    private static IEnumerable<Mention> Read(FoConsultation source)
    {
        foreach (var field in new[] { source.Text, source.RefractionText })
        {
            bool familySection = false;
            foreach (var original in FoClinicalContext.Clauses(field))
            {
                var text = FundusAnalysis.Normalize(original);
                // A family-history heading can cover several following lines. Resume only at
                // a known clinical heading or an explicitly personal/patient statement.
                if (Has(text, @"^(?:atcd|antecedents?)\s+familia\w*\s*:?$"))
                {
                    familySection = true;
                    continue;
                }
                if (Has(text, @"\b(?:patient\w*|personnel\w*)\b") ||
                    Has(text, @"^(?:observation|refraction|examen|conclusion)\s*:"))
                    familySection = false;
                if (familySection || Has(text, Family)) continue;

                foreach (var (condition, pattern) in new[] { (Condition.Diabetes, Diabetes), (Condition.OcularHypertension, OcularHypertension) })
                {
                    foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant, Limit))
                    {
                        var before = text[..match.Index];
                        var after = text[(match.Index + match.Length)..];
                        if (condition == Condition.Diabetes &&
                            (Has(before, @"\b(?:pre|anti)\s*$") || Has(after, @"^\s+insipide\b"))) continue;
                        bool negative = Has(before, @"\b(?:pas|sans|absence|aucun\w*|non|ni)\s+(?:(?:de|du|d['’])\s*)?(?:antecedent\w*\s+(?:de\s+|d['’]))?$") ||
                            Has(after, @"^\s*:?\s*(?:absent\w*|exclu\w*|infirme\w*|resolu\w*|gueri\w*|non\s+(?:confirme\w*|retenu\w*))\b|^\s*:?\s*non\s*[.!]?$");
                        // Negation of a complication ("pas de rétinopathie diabétique") is
                        // not negation of the diabetic context. Never infer HTO from a number/drug.
                        if (!negative && (Has(text, Uncertain) ||
                            Has(text, @"\b(?:gestationnel\w*|resolu\w*|gueri\w*)\b"))) continue;
                        yield return new(condition, source, original, negative);
                    }
                }
            }
        }
    }

    public void Observe(IEnumerable<FoConsultation> consultations, bool cancelled, FoConsultation? completed)
    {
        foreach (var group in consultations.SelectMany(Read).GroupBy(m => m.Condition))
        {
            // Contradictory fields at the same date do not establish a chronic condition.
            if (group.Any(m => m.Negative)) current.Remove(group.Key);
            else current[group.Key] = group.First();
        }
        if (completed != null) lastCompletion = completed;
        // A later explicit cancellation prevails until a fresh documented context/request.
        if (cancelled) current.Clear();
    }

    public FoAnalysis? Analyze(DateTime now, string evidenceHash)
    {
        if (current.Count == 0) return null;
        // Use only an explicit, non-historical completion from the existing engine.
        // Its consultation date is a reference to check, not a reconstructed exam date.
        if (lastCompletion != null && now.Date < lastCompletion.Date.Date.AddYears(1)) return null;
        var latest = current.Values.OrderByDescending(m => m.Source.Date).ThenBy(m => m.Source.Id, StringComparer.Ordinal).First();
        var sameSource = current.Values.Where(m => m.Source.Id == latest.Source.Id && m.Source.Date == latest.Source.Date).ToList();
        var context = string.Join(" et ", sameSource.OrderBy(m => m.Condition).Select(m =>
            m.Condition == Condition.Diabetes ? "contexte diabétique" : "HTO (tension oculaire)"));
        var reason = "Suivi annuel du FO — " + context + ". " + (lastCompletion == null
            ? "Date du dernier FO à vérifier."
            : "Mention de réalisation dans la consultation du " + lastCompletion.Date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")) + "; vérifier l’échéance.");
        var excerpt = string.Join("\n", sameSource.Select(m => m.Excerpt).Distinct());
        return new(latest.Source.Id, latest.Source.Date, excerpt.Length > 600 ? excerpt[..600] : excerpt,
            FoStatus.Verify, reason, FundusAnalysis.Hash(evidenceHash + "|annual-fo-v1"), "", null);
    }
}
