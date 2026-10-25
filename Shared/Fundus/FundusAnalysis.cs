using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public enum FoStatus { Planned, Verify, ProbablyPerformed }
public enum FoFindingKind { Request, Potential, Performed, Cancelled }
public sealed record FoConsultation(string Id, DateTime Date, string Text, string RefractionText = "");
public sealed record FoFinding(FoFindingKind Kind, string Excerpt, string Reason = "");
public sealed record FoAnalysis(string SourceId, DateTime SourceDate, string Excerpt, FoStatus Status,
    string Reason, string EvidenceHash, string LaterExcerpt, DateTime? LaterDate);

/// <summary>Local, deterministic preparation aid. No diagnosis and no inference from absence alone.</summary>
public static class FundusAnalysis
{
    // Keep expressions together: each extension needs a positive, negative and ambiguous regression case.
    internal const string Exam = @"(?:\bf\s*\.?\s*o(?:dg|d|g)?\.?\b|\bfonds?\s+d['’ ]?\s*(?:oeil|yeux)\b|\bdilat(?:er|ation)\b)";
    internal const string RepeatRequest = @"\b(?:refaire|recontroler|controler|renouveler)\s+(?:(?:un|une|le|la|les|des|du|de)\s+|l['’]\s*)*(?:(?:examen|controle)\s+(?:(?:du|des|de|le|les)\s+|d['’])?)?" + Exam;
    internal const string Delay = @"(?:dans|ds|d['’]ici|a)\s+(?:\d+|un|une|deux|trois|six|douze)\s*(?:mois|mos|m|ans?|annees?|semaines?|jours?)\b";
    private const string ScheduledExam = Exam + @"\s+" + Delay;
    private const string Future = @"\b(?:prochain\w*|proch\.?|prevoir|prevu\w*|a faire|a dilater|revoir pour|faire\b.{0,30}\bpassage)\b";
    private const string Negative = @"\b(?:pas (?:de |d['’])?(?:fo|fond|dilat)|(?:fo|fond.{0,12}|dilatation)\s+(?:non necessaire|non indique|inutile)|sans (?:necessite de )?(?:fo|dilatation)|annul\w*\b.{0,25}(?:fo|fond|dilat))";
    private const string Done = @"\b(?:realis\w*|effectu\w*|fait\w*|dilate\w*)\b";
    private const string DirectFuture = Exam + @"\s*(?:(?:est|sera|a|au|la|le|pour|lors|du|de)\s+)*(?:prochain\w*|proch\.?|prevoir|prevu\w*|faire|dilater)\b|\b(?:prevoir|faire|revoir pour|a)\s+(?:(?:un|une|le|la|les|des)\s+)?" + Exam;
    private const string DirectDone = Exam + @"\s*(?:(?:a|ont|ete|est|sont|bien|deja|aussi)\s+)*" + Done + "|" + Done + @"\s+(?:(?:un|une|le|la|les|des)\s+)?" + Exam;
    private const string Uncertain = @"\b(?:si|peut etre|eventuel\w*|possible|envisag\w*|a confirmer|doute|incertain|a discuter|a verifier)\b|\?";
    private const string Historical = @"\b(?:ancien\w*|anterieur\w*|antecedent\w*|avait|il y a|dernier\w*|en (?:19|20)\d{2})\b";
    private const string NotDone = @"\b(?:non|pas|jamais)\s+(?:encore\s+)?(?:realis\w*|effectu\w*|fait\w*|dilate\w*)\b|\b(?:refus\w*|report\w*|impossible|non fait)\b";

    public static string Normalize(string text)
    {
        var normalized = text.Replace("œ", "oe").Replace("Œ", "OE").Normalize(NormalizationForm.FormD);
        var plain = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC).ToLowerInvariant();
        plain = Regex.Replace(plain, @"\bf\s*\.?\s*o\.?(?!\w)", "fo", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        return Regex.Replace(plain.Replace('-', ' '), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }

    private static bool Has(string value, string pattern) => Regex.IsMatch(value, pattern,
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static IReadOnlyList<FoFinding> Detect(string original)
    {
        var result = new List<FoFinding>();
        // Retain the source's spelling, punctuation and accents in the evidence.
        foreach (var part in FoClinicalContext.Clauses(original))
        {
            var excerpt = part.Trim();
            var text = Normalize(excerpt);
            if (!Has(text, Exam)) continue;
            var negated = Has(text, Negative);
            var future = Has(text, Future);
            var uncertain = Has(text, Uncertain);
            var notDone = Has(text, NotDone);
            var repeat = Has(text, RepeatRequest);
            var scheduled = Has(text, ScheduledExam);
            if (repeat && Has(text, @"\b(?:(?:ne\s+)?(?:pas|plus|jamais)\s+|(?:inutile|pas necessaire|pas besoin)\s+de\s+)" + RepeatRequest))
            {
                result.Add(new(uncertain ? FoFindingKind.Potential : FoFindingKind.Cancelled, excerpt));
                continue;
            }
            // A historical renewal plan is not a new instruction for the next visit.
            if ((repeat || scheduled) && Has(text, @"\b(?:avait|avaient|etait|etaient|il y a|ancienne? consigne|antecedent\w*)\b")) continue;
            if (negated)
            {
                result.Add(new(uncertain || notDone || Has(text, Done) ? FoFindingKind.Potential : FoFindingKind.Cancelled, excerpt));
                continue;
            }
            // Require an explicit link between the exam and the instruction, not just any future plan.
            if (((future && Has(text, DirectFuture)) || repeat || scheduled) && !Has(text, @"\b(?:pas|non)\s+(?:prevu|a prevoir|a faire)\b"))
            {
                var kind = uncertain || notDone || Has(text, DirectDone) || Has(text, @"\b(?:ne|pas)\b")
                    ? FoFindingKind.Potential : FoFindingKind.Request;
                result.Add(new(kind, excerpt, kind == FoFindingKind.Request && (repeat || scheduled)
                    ? "Consigne de contrôle du FO ; vérifier le délai indiqué." : ""));
                continue;
            }
            if (Has(text, Historical)) continue;
            if (Has(text, DirectDone) && !Has(text, @"\b(?:a|sera|seront|devra|doit|pour)\s+(?:etre\s+)?(?:realis\w*|effectu\w*|fait\w*)\b"))
            {
                result.Add(new(uncertain || notDone ? FoFindingKind.Potential : FoFindingKind.Performed, excerpt));
                continue;
            }
            // A bare FO result (e.g. FO normal) is neither a new request nor proof of completion.
            if (future || notDone || uncertain || Has(text, @"\b(?:a realiser|a effectuer|doit|devra|sera|surveiller|controle a)\b"))
                result.Add(new(FoFindingKind.Potential, excerpt));
        }
        return result;
    }

    private static IEnumerable<FoFinding> ClinicalFindings(FoConsultation consultation)
    {
        var observations = Detect(consultation.Text);
        var refraction = Detect(consultation.RefractionText);
        var findings = observations.Concat(refraction).ToList();
        if (observations.Count == 0 || refraction.Count == 0) return findings;
        bool contradicts = findings.Any(f => f.Kind == FoFindingKind.Cancelled) &&
            findings.Any(f => f.Kind is FoFindingKind.Request or FoFindingKind.Potential);
        // Observation and REFRACTION describe the same visit, not successive visits.
        // A new explicit future request remains open even when the current visit records a completed FO.
        return findings.Select(f => contradicts && f.Kind == FoFindingKind.Request ? f with { Kind = FoFindingKind.Potential } : f)
            .OrderBy(f => f.Kind switch { FoFindingKind.Performed => 0, FoFindingKind.Cancelled => 1, FoFindingKind.Potential => 2, _ => 3 });
    }

    public static FoAnalysis? Analyze(IEnumerable<FoConsultation> consultations, DateTime now)
    {
        var rows = consultations.Where(c => c.Date <= now).OrderBy(c => c.Date).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var hash = Hash(string.Join("\n", rows.Select(c => $"{c.Id.Length}:{c.Id}|{c.Date:O}|{Hash(c.Text)}|{Hash(c.RefractionText)}")));
        FoAnalysis? active = null;
        FoAnalysis? context = null;
        var annual = new FoAnnualFollowUp();
        foreach (var group in rows.GroupBy(c => c.Date))
        {
            var events = group.SelectMany(c => ClinicalFindings(c).Select(f => (Consultation: c, Finding: f))).ToList();
            annual.Observe(group, events.Any(e => e.Finding.Kind == FoFindingKind.Cancelled),
                events.FirstOrDefault(e => e.Finding.Kind == FoFindingKind.Performed).Consultation);
            // Contextual mentions can suggest a manual check, but cannot override an explicit
            // cancellation/completion or establish that a new FO has been prescribed/performed.
            if (events.Any(e => e.Finding.Kind is FoFindingKind.Cancelled or FoFindingKind.Performed)) context = null;
            else
            {
                var candidate = group.Select(c => (Consultation: c, Finding: FoClinicalContext.Find(c)))
                    .FirstOrDefault(c => c.Finding != null);
                if (candidate.Finding != null)
                    context = new(candidate.Consultation.Id, candidate.Consultation.Date, Clip(candidate.Finding.Excerpt),
                        FoStatus.Verify, candidate.Finding.Reason, hash, "", null);
            }
            // Distinct consultations with identical timestamps cannot establish a reliable order.
            var sameTimeConflict = group.Count() > 1 && events.Any(e => e.Finding.Kind is FoFindingKind.Request or FoFindingKind.Potential)
                && events.Any(e => e.Finding.Kind is FoFindingKind.Performed or FoFindingKind.Cancelled);
            foreach (var (consultation, finding) in events)
            {
                if (finding.Kind is FoFindingKind.Request or FoFindingKind.Potential)
                {
                    // A fresh explicit instruction reopens a previously fulfilled episode.
                    if (active == null || active.Status == FoStatus.ProbablyPerformed || finding.Kind == FoFindingKind.Request)
                        active = new(consultation.Id, consultation.Date, Clip(finding.Excerpt),
                            finding.Kind == FoFindingKind.Request ? FoStatus.Planned : FoStatus.Verify,
                            finding.Kind == FoFindingKind.Request ? (finding.Reason.Length > 0 ? finding.Reason : "Consigne explicite retrouvée.") : "Formulation à vérifier dans le dossier.",
                            hash, "", null);
                    else active = active with { Status = FoStatus.Verify, Reason = "Mention ultérieure incertaine.", LaterExcerpt = Clip(finding.Excerpt), LaterDate = consultation.Date };
                }
                else if (active != null && !sameTimeConflict)
                {
                    if (finding.Kind == FoFindingKind.Cancelled) active = null;
                    else active = active with { Status = FoStatus.ProbablyPerformed, Reason = "Mention de réalisation retrouvée ; à confirmer.", LaterExcerpt = Clip(finding.Excerpt), LaterDate = consultation.Date };
                }
            }
            if (active != null)
            {
                if (sameTimeConflict)
                    active = active with { Status = FoStatus.Verify, Reason = "Consultations de même date : chronologie incertaine." };
                else if (group.Key > active.SourceDate && active.Status != FoStatus.ProbablyPerformed &&
                         !events.Any(e => e.Finding.Kind == FoFindingKind.Request))
                    active = active with { Status = FoStatus.Verify, Reason = "Consultation ultérieure sans réalisation clairement établie." };
            }
        }
        // An explicit open instruction keeps priority. A completed episode can give way
        // to the annual review after one year, while chronic context survives silent visits.
        var annualReview = annual.Analyze(now, hash);
        if (annualReview != null && (active == null || active.Status == FoStatus.ProbablyPerformed)) return annualReview;
        var result = active ?? context;
        return result == null ? null : result with { EvidenceHash = hash };
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Clip(string value) => value.Length <= 600 ? value : value[..600];
    public static string Label(FoStatus status) => status switch
    {
        FoStatus.Planned => "FO à prévoir", FoStatus.ProbablyPerformed => "FO probablement réalisé", _ => "À vérifier"
    };
}
