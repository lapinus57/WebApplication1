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
public sealed record FoConsultation(string Id, DateTime Date, string Text, string FollowUpText = "");
public sealed record FoFinding(FoFindingKind Kind, string Excerpt);
public sealed record FoAnalysis(string SourceId, DateTime SourceDate, string Excerpt, FoStatus Status,
    string Reason, string EvidenceHash, string LaterExcerpt, DateTime? LaterDate);

/// <summary>Local, deterministic preparation aid. No diagnosis and no inference from absence alone.</summary>
public static class FundusAnalysis
{
    // Keep expressions together: each extension needs a positive, negative and ambiguous regression case.
    private const string Exam = @"(?:\bf\s*\.?\s*o\.?\b|\bfonds?\s+d['’ ]?\s*(?:oeil|yeux)\b|\bdilat(?:er|ation)\b)";
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

    public static IReadOnlyList<FoFinding> Detect(string original, bool followUpField = false)
    {
        var result = new List<FoFinding>();
        // Retain the source's spelling, punctuation and accents in the evidence.
        foreach (var part in Regex.Split(original, @"[\r\n;,]+|(?<![FfOo]\.)(?<=[.!?])\s+"))
        {
            var excerpt = part.Trim();
            var text = Normalize(excerpt);
            if (!Has(text, Exam)) continue;
            var negated = Has(text, Negative);
            var future = Has(text, Future);
            var uncertain = Has(text, Uncertain);
            var notDone = Has(text, NotDone);
            if (negated)
            {
                result.Add(new(uncertain || notDone || Has(text, Done) ? FoFindingKind.Potential : FoFindingKind.Cancelled, excerpt));
                continue;
            }
            // Require an explicit link between the exam and the instruction, not just any future plan.
            if (future && Has(text, DirectFuture) && !Has(text, @"\b(?:pas|non)\s+(?:prevu|a prevoir|a faire)\b"))
            {
                result.Add(new(uncertain || notDone || Has(text, DirectDone) || Has(text, @"\b(?:ne|pas)\b")
                    ? FoFindingKind.Potential : FoFindingKind.Request, excerpt));
                continue;
            }
            if (Has(text, Historical)) continue;
            if (Has(text, DirectDone) && !Has(text, @"\b(?:a|sera|seront|devra|doit|pour)\s+(?:etre\s+)?(?:realis\w*|effectu\w*|fait\w*)\b"))
            {
                result.Add(new(uncertain || notDone ? FoFindingKind.Potential : FoFindingKind.Performed, excerpt));
                continue;
            }
            // A bare FO result (e.g. FO normal) is neither a new request nor proof of completion.
            if (followUpField || future || notDone || uncertain || Has(text, @"\b(?:a realiser|a effectuer|doit|devra|sera|surveiller|controle a)\b"))
                result.Add(new(FoFindingKind.Potential, excerpt));
        }
        return result;
    }

    public static FoAnalysis? Analyze(IEnumerable<FoConsultation> consultations, DateTime now)
    {
        var rows = consultations.Where(c => c.Date <= now).OrderBy(c => c.Date).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var hash = Hash(string.Join("\n", rows.Select(c => $"{c.Id.Length}:{c.Id}|{c.Date:O}|{Hash(c.Text)}|{Hash(c.FollowUpText)}")));
        FoAnalysis? active = null;
        foreach (var group in rows.GroupBy(c => c.Date))
        {
            var events = group.SelectMany(c => Detect(c.Text).Concat(Detect(c.FollowUpText, true)).Select(f => (Consultation: c, Finding: f))).ToList();
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
                            finding.Kind == FoFindingKind.Request ? "Consigne explicite retrouvée." : "Formulation à vérifier dans le dossier.",
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
        return active == null ? null : active with { EvidenceHash = hash };
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Clip(string value) => value.Length <= 600 ? value : value[..600];
    public static string Label(FoStatus status) => status switch
    {
        FoStatus.Planned => "FO à prévoir", FoStatus.ProbablyPerformed => "FO probablement réalisé", _ => "À vérifier"
    };
}
