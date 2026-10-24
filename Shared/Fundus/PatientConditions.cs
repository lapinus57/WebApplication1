using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public enum PatientConditionState { Unknown, Present, Absent, ToReview }
public enum PatientCondition { Diabetes, OcularHypertension }
internal enum ConditionAssertion { Affirmed, Context, Negated, Uncertain, Unconfirmed }
internal sealed record ConditionMention(PatientCondition Condition, FoConsultation Source, string Field, string Excerpt, ConditionAssertion Assertion);
public sealed record PatientConditionEvidence(PatientConditionState State, string Evidence, DateTime? SourceDate, string EvidenceHash);
public sealed record PatientConditionResult(PatientConditionEvidence Diabetes, PatientConditionEvidence OcularHypertension);
public sealed record RegistryPatient(string Id, string LastName, string FirstName, IReadOnlyList<FoConsultation> Consultations);

public static class PatientConditions
{
    public static string Label(PatientConditionState state) => state switch
    { PatientConditionState.Present => "Oui", PatientConditionState.Absent => "Non", PatientConditionState.ToReview => "À vérifier", _ => "Non renseigné" };

    public static PatientConditionResult Analyze(IEnumerable<FoConsultation> consultations, DateTime now)
    {
        var states = Enum.GetValues<PatientCondition>().ToDictionary(c => c, _ => Evidence(PatientConditionState.Unknown, "", null));
        foreach (var date in consultations.Where(c => c.Date <= now).OrderBy(c => c.Date).ThenBy(c => c.Id, StringComparer.Ordinal).GroupBy(c => c.Date))
        {
            foreach (var group in date.SelectMany(ConditionMentions.Read).GroupBy(m => m.Condition))
            {
                var assertions = group.Where(m => m.Assertion != ConditionAssertion.Context).ToList();
                // A complication context alone is not a diagnosis and cannot erase a prior explicit statement.
                if (assertions.Count == 0 && states[group.Key].State != PatientConditionState.Unknown) continue;
                if (assertions.Count == 0) assertions = group.ToList();
                bool yes = assertions.Any(m => m.Assertion == ConditionAssertion.Affirmed);
                bool no = assertions.Any(m => m.Assertion == ConditionAssertion.Negated);
                bool unsure = assertions.Any(m => m.Assertion is ConditionAssertion.Uncertain or ConditionAssertion.Unconfirmed or ConditionAssertion.Context);
                var state = unsure || (yes && no) ? PatientConditionState.ToReview : yes ? PatientConditionState.Present : PatientConditionState.Absent;
                var excerpts = string.Join("\n", assertions.Take(2).Select(m => $"Consultation {m.Source.Id} · {m.Field} : {Clip(m.Excerpt, 230)}"));
                var signature = string.Join("\n", assertions.Select(m => $"{m.Source.Id}|{m.Field}|{m.Assertion}|{FundusAnalysis.Hash(m.Excerpt)}"));
                states[group.Key] = Evidence(state, Clip(excerpts, 600), date.Key, signature);
            }
        }
        return new(states[PatientCondition.Diabetes], states[PatientCondition.OcularHypertension]);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
    private static PatientConditionEvidence Evidence(PatientConditionState state, string excerpt, DateTime? date, string signature = "") =>
        new(state, excerpt, date, FundusAnalysis.Hash($"conditions-v1|{state}|{date:O}|{signature}"));
}

internal static class ConditionMentions
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private const string Diabetes = @"\b(?:diabete|diabetiques?|did|dnid)\b";
    private const string OcularHypertension = @"\b(?:h\.?\s*t\.?\s*o\.?|hyper(?:tension|tonie)\s+(?:intra\s*)?oculaire|tension\s+(?:intra\s*)?oculaire\s+(?:elevee|haute))\b";
    private const string Family = @"\b(?:familia\w*|mere|pere|parents?|frere|soeur|fils|fille|conjoint\w*|epou[sx]\w*)\b";
    private const string Uncertain = @"\?|\b(?:si|possible|suspect\w*|suspicion|risque|depist\w*|recherch\w*|a confirmer|a eliminer|a exclure|non exclu\w*|eventuel\w*)\b";
    private static bool Has(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, Limit);

    internal static IEnumerable<ConditionMention> Read(FoConsultation source)
    {
        foreach (var (field, name) in new[] { (source.Text, "Observation"), (source.RefractionText, "REFRACTION") })
        {
            bool familySection = false;
            foreach (var original in FoClinicalContext.Clauses(field))
            {
                var text = FundusAnalysis.Normalize(original);
                if (Has(text, @"^(?:atcd|antecedents?)\s+familia\w*\s*:?$")) { familySection = true; continue; }
                if (Has(text, @"\b(?:patient\w*|personnel\w*)\b") || Has(text, @"^(?:observation|refraction|examen|conclusion)\s*:")) familySection = false;
                if (familySection || Has(text, Family)) continue;
                foreach (var (condition, pattern) in new[] { (PatientCondition.Diabetes, Diabetes), (PatientCondition.OcularHypertension, OcularHypertension) })
                {
                    foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant, Limit))
                    {
                        var before = text[..match.Index]; var after = text[(match.Index + match.Length)..];
                        if (condition == PatientCondition.Diabetes &&
                            (Has(before, @"\b(?:pre|anti)\s*$") || Has(after, @"^\s+insipide\b"))) continue;
                        bool unconfirmed = Has(after, @"^\s*:?\s*non\s+(?:confirme\w*|retenu\w*)\b");
                        bool negative = Has(before, @"\b(?:pas|sans|absence|aucun\w*|non|ni)\s+(?:(?:de|du|d['’])\s*)?(?:antecedent\w*\s+(?:de\s+|d['’]))?$") ||
                            Has(after, @"^\s*:?\s*(?:absent\w*|exclu\w*|infirme\w*|resolu\w*|gueri\w*)\b|^\s*:?\s*non\s*[.!]?$");
                        if (!negative && !unconfirmed && Has(text, @"\b(?:gestationnel\w*|resolu\w*|gueri\w*)\b")) continue;
                        var assertion = Has(text, Uncertain) ? ConditionAssertion.Uncertain : unconfirmed ? ConditionAssertion.Unconfirmed
                            : negative ? ConditionAssertion.Negated
                            : condition == PatientCondition.Diabetes && Has(text, @"\bretinopathie\b") ? ConditionAssertion.Context : ConditionAssertion.Affirmed;
                        yield return new(condition, source, name, original, assertion);
                    }
                }
            }
        }
    }
}
