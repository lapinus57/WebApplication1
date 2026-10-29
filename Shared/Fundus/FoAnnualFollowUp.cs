using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EyeChat.Fundus;

// Cabinet follow-up rule: these documented contexts suggest a review, never a prescription.
internal sealed class FoAnnualFollowUp
{
    private enum Condition { Diabetes, OcularHypertension }
    private sealed record Mention(Condition Condition, FoConsultation Source, string Excerpt, bool Negative);
    private readonly Dictionary<Condition, Mention> current = new();
    private FoConsultation? lastCompletion;
    private static IEnumerable<Mention> Read(FoConsultation source) => ConditionMentions.Read(source)
        .Where(m => m.Assertion != ConditionAssertion.Uncertain)
        .Select(m => new Mention(m.Condition == PatientCondition.Diabetes ? Condition.Diabetes : Condition.OcularHypertension,
            m.Source, m.Excerpt, m.Assertion is ConditionAssertion.Negated or ConditionAssertion.Unconfirmed));

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
            : (FoCcam.FundusEvidence(lastCompletion).Length > 0 ? "Acte FO codé dans la consultation du " : "Mention de réalisation dans la consultation du ")
                + lastCompletion.Date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")) + "; vérifier l’échéance.");
        var excerpt = string.Join("\n", sameSource.Select(m => m.Excerpt).Distinct());
        return new(latest.Source.Id, latest.Source.Date, excerpt.Length > 600 ? excerpt[..600] : excerpt,
            FoStatus.Verify, reason, FundusAnalysis.Hash(evidenceHash + "|annual-fo-v1"), "", null,
            Due: lastCompletion?.Date.Date.AddYears(1));
    }
}
