using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public sealed record ExamAnnotation(int Exam, bool Yes, string Text);
public sealed class ExamExample
{
    public string Group { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime AppointmentDay { get; set; }
    public bool[] Signals { get; set; } = Array.Empty<bool>();
    public bool?[] Answers { get; set; } = new bool?[4];
    public List<ExamAnnotation> Annotations { get; set; } = new();
}

public static class ExamLearningData
{
    public static readonly string[] Exams = { "FO", "OCT", "V3F", "Skiacol" };
    public static readonly string[] SignalNames =
    {
        "Consultation disponible", "Consigne FO active", "FO à vérifier selon les règles", "FO probablement réalisé selon les règles",
        "Diabète documenté", "HTO documentée", "Acte OCT-FO récent", "Dernière visite avec BZQK001",
        "Demande OCT dans la dernière visite", "Demande V3F dans la dernière visite", "Demande Skiacol dans la dernière visite",
        "Négation FO dans la dernière visite", "Consultation ultérieure à la consigne", "Dernière visite datant d’au moins un an",
        "Liaison CCAM disponible", "Contradiction dans les éléments FO"
    };
    public const int MaxText = 100_000;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    public static (string Text, bool[] Signals) Extract(IEnumerable<FoConsultation> visits, DateTime now, DateTime appointment)
    {
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        // No patient name, identifier or appointment header is added to the learning text.
        var text = string.Join("\n\n", rows.Take(3).Select(c => "Consultation du " + c.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + "\nObservation\n" + c.Text + "\nREFRACTION\n" + c.RefractionText));
        if (text.Length > MaxText) throw new FoDataException("Les trois dernières consultations sont trop longues pour cet apprentissage local. Aucun texte n’a été tronqué ni enregistré.");
        var latest = rows.FirstOrDefault();
        var analysis = FundusAnalysis.Analyze(rows, now, appointment);
        var conditions = PatientConditions.Analyze(rows, now);
        var latestText = latest == null ? "" : latest.Text + "\n" + latest.RefractionText;
        bool Request(string exam) => FoClinicalContext.Clauses(latestText).Any(c =>
        {
            var n = FundusAnalysis.Normalize(c);
            return Regex.IsMatch(n, @"\b(?:prevoir|refaire|faire|controler|prochain\w*|a realiser)\b", RegexOptions.CultureInvariant, Limit)
                && Regex.IsMatch(n, exam, RegexOptions.CultureInvariant, Limit)
                && !Regex.IsMatch(n, @"\b(?:pas|sans|non|annule\w*|ancien\w*|avait)\b", RegexOptions.CultureInvariant, Limit);
        });
        bool lastActRecent = rows.Any(c => FoCcam.FundusEvidence(c).Length > 0 && c.Date.Date.AddYears(1) > appointment.Date);
        return (text, new[]
        {
            latest != null, analysis?.Status == FoStatus.Planned, analysis?.Status == FoStatus.Verify, analysis?.Status == FoStatus.ProbablyPerformed,
            conditions.Diabetes.State == PatientConditionState.Present, conditions.OcularHypertension.State == PatientConditionState.Present,
            lastActRecent, latest?.CcamCodes.Contains("BZQK001", StringComparison.Ordinal) == true,
            Request(@"\boct\b"), Request(@"\bv3\s*f\w*\b"), Request(@"\bskiacol\b|\bcyclopleg\w*\b"),
            FundusAnalysis.Detect(latestText).Any(f => f.Kind == FoFindingKind.Cancelled),
            analysis != null && latest != null && latest.Date > analysis.SourceDate,
            latest != null && latest.Date.Date.AddYears(1) <= appointment.Date,
            latest?.ActLinkAvailable == true, analysis?.HasConflict == true
        });
    }

    public static void Validate(ExamExample example)
    {
        if (!Regex.IsMatch(example.Group ?? "", @"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant, Limit)
            || example.Text == null || example.Text.Length > MaxText || example.Signals == null || example.Signals.Length != SignalNames.Length
            || example.AppointmentDay == default || example.AppointmentDay.TimeOfDay != TimeSpan.Zero
            || example.Answers == null || example.Answers.Length != 4 || example.Annotations == null || example.Annotations.Count > 24)
            throw new FoDataException("Exemple d’apprentissage invalide.");
        foreach (var a in example.Annotations)
            if (a == null || a.Exam < 0 || a.Exam >= 4 || string.IsNullOrWhiteSpace(a.Text) || a.Text.Length > 1200
                || !example.Text.Contains(a.Text, StringComparison.Ordinal) || example.Answers[a.Exam] != a.Yes)
                throw new FoDataException("Chaque extrait doit appartenir au texte relu et correspondre à la réponse Oui/Non de son examen.");
    }

    public static bool IsTest(string group) => Convert.ToByte(group[..2], 16) % 5 == 0;
    public static string Digest(string text, bool[] signals, DateTime appointmentDay) => FundusAnalysis.Hash(text + "\n" + string.Join(",", signals) + "\n" + appointmentDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    // Only hashes of annotated word sequences are exported, never the word vocabulary or excerpts.
    public static Dictionary<string, string> TextFeatures(string text, ISet<string>? wanted = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (wanted?.Count == 0) return result;
        foreach (var clause in Regex.Split(text, @"[\r\n.;!?]+", RegexOptions.CultureInvariant, Limit))
        {
            var words = Regex.Matches(clause, @"[\p{L}\p{N}][\p{L}\p{N}'’]*", RegexOptions.CultureInvariant, Limit).Cast<Match>().ToArray();
            var normalized = words.Select(w => FundusAnalysis.Normalize(w.Value)).ToArray();
            for (int i = 0; i < words.Length; i++)
                for (int count = 1; count <= 4 && i + count <= words.Length; count++)
                {
                    if (count == 1 && normalized[i].Length < 3) continue;
                    var key = "T" + FundusAnalysis.Hash(string.Join(" ", normalized.Skip(i).Take(count)));
                    if (wanted != null && !wanted.Contains(key)) continue;
                    var end = words[i + count - 1].Index + words[i + count - 1].Length;
                    result.TryAdd(key, clause.Substring(words[i].Index, end - words[i].Index));
                }
        }
        return result;
    }

    public static HashSet<string> Features(ExamExample example, ISet<string> textual)
    {
        var keys = new HashSet<string>(TextFeatures(example.Text, textual).Keys, StringComparer.Ordinal);
        for (int i = 0; i < example.Signals.Length; i++) if (example.Signals[i]) keys.Add("S" + i.ToString(CultureInfo.InvariantCulture));
        return keys;
    }
}
