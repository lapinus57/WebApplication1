using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public sealed class LocalRuleProposal
{
    [JsonRequired] public bool NeedsClarification { get; set; }
    [JsonRequired] public string Question { get; set; } = "";
    [JsonRequired] public string? Condition { get; set; }
    [JsonRequired] public string? Exam { get; set; }
    [JsonRequired] public decimal? SphereLessThan { get; set; }
    [JsonRequired] public int? IntervalMonths { get; set; }
}

public sealed record LocalExamRule(Guid Id, string Condition, string Exam, decimal? SphereLessThan,
    int IntervalMonths, DateTime ValidatedUtc, bool Enabled)
{
    [JsonIgnore] public string Description => LocalExamRules.Describe(this);
    [JsonIgnore] public string State => Enabled ? "Active — validée" : "Inactive — activation requise";
}

public sealed class LocalRulePackage
{
    [JsonRequired] public string Kind { get; set; } = "eye-exam-rules";
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public List<LocalExamRule> Rules { get; set; } = new();
}

public static class LocalExamRules
{
    public static readonly string[] Conditions = { "DIABETES", "HTO", "MYOPIA" };
    public static readonly string[] Exams = { "FO_OCT", "V3F", "SKIACOL" };
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12, WriteIndented = true };
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(250);

    public static LocalRuleProposal ParseProposal(string json)
    {
        if (json.Length > 16000) throw new FoDataException("Réponse de l’IA trop volumineuse.");
        try
        {
            var proposal = JsonSerializer.Deserialize<LocalRuleProposal>(json, Json) ?? throw new JsonException();
            if (proposal.Question == null || proposal.Question.Length > 2000) throw new JsonException();
            if (proposal.NeedsClarification)
            {
                if (string.IsNullOrWhiteSpace(proposal.Question)) throw new JsonException();
            }
            else
            {
                if (proposal.Question.Length != 0 || proposal.IntervalMonths == null) throw new JsonException();
                Validate(new(Guid.NewGuid(), proposal.Condition ?? "", proposal.Exam ?? "", proposal.SphereLessThan, proposal.IntervalMonths.Value, DateTime.UtcNow, false));
            }
            return proposal;
        }
        catch (FoDataException) { throw; }
        catch { throw new FoDataException("L’IA n’a pas fourni une proposition structurée valide. Précisez la consigne puis réessayez."); }
    }

    public static void Validate(LocalExamRule rule)
    {
        if (rule == null || rule.Id == Guid.Empty || !Conditions.Contains(rule.Condition) || !Exams.Contains(rule.Exam)
            || rule.IntervalMonths is < 1 or > 120 || rule.ValidatedUtc == default
            || (rule.Condition == "MYOPIA" ? rule.SphereLessThan is not (>= -30m and <= -0.25m) : rule.SphereLessThan != null))
            throw new FoDataException("Règle invalide : choisissez un critère connu, un examen et un délai entier de 1 à 120 mois ; le seuil de myopie doit être négatif.");
    }

    public static void ValidatePackage(LocalRulePackage package)
    {
        if (package == null || package.Kind != "eye-exam-rules" || package.Version != 1 || package.Rules == null || package.Rules.Count > 50)
            throw new FoDataException("Format de règles incompatible ou limite de 50 règles dépassée.");
        foreach (var rule in package.Rules) Validate(rule);
        if (package.Rules.Select(r => r.Id).Distinct().Count() != package.Rules.Count) throw new FoDataException("Identifiants de règles dupliqués.");
    }

    public static string Export(LocalRulePackage package)
    {
        ValidatePackage(package);
        return JsonSerializer.Serialize(new LocalRulePackage { Rules = package.Rules.Where(r => r.Enabled).Select(r => r with { Enabled = false }).ToList() }, Json);
    }

    public static LocalRulePackage Import(string json)
    {
        if (json.Length > 64000) throw new FoDataException("Le fichier de règles dépasse 64 Ko.");
        try
        {
            var package = JsonSerializer.Deserialize<LocalRulePackage>(json, Json) ?? throw new JsonException();
            ValidatePackage(package);
            package.Rules = package.Rules.Select(r => r with { Id = Guid.NewGuid(), Enabled = false }).ToList();
            return package;
        }
        catch (FoDataException) { throw; }
        catch { throw new FoDataException("Fichier de règles illisible ou incompatible."); }
    }

    public static string Describe(LocalExamRule rule) =>
        (rule.Condition == "DIABETES" ? "Diabète documenté" : rule.Condition == "HTO" ? "HTO documentée" : "Myopie : sphère strictement inférieure à " + rule.SphereLessThan?.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")))
        + " → préparer " + (rule.Exam == "FO_OCT" ? "FO/OCT" : rule.Exam == "SKIACOL" ? "Skiacol" : "V3F")
        + " après " + rule.IntervalMonths + " mois depuis le dernier examen documenté.";

    public static bool ExplicitMyopia(string text, decimal threshold)
    {
        return Regex.Split(text, @"[\r\n;]+|(?<=[.!?])\s+", RegexOptions.CultureInvariant, RegexLimit).Any(c =>
            !Regex.IsMatch(FundusAnalysis.Normalize(c), @"\b(pas|sans|non|ancien\w*|antecedent\w*|familial\w*|possible|suspect\w*|suspicion|doute|hypothese|fausse)\b|\?", RegexOptions.CultureInvariant, RegexLimit)
            && Regex.Matches(c, @"\bmyopie\s*(?:(?:OD|OG|ODG)\s*)?(?:de|:|=)?\s*[−-](\d{1,2}(?:[.,]\d{1,2})?)\b", RegexOptions.IgnoreCase, RegexLimit)
                .Cast<Match>().Any(m => decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value <= 30 && -value < threshold));
    }

    public static ExamPrediction[] Apply(LocalRulePackage package, IReadOnlyList<FoConsultation> visits, DateTime now, DateTime appointment, ExamPrediction[] predictions)
    {
        ValidatePackage(package);
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ToArray();
        if (rows.Length == 0) return predictions;
        var conditions = PatientConditions.Analyze(rows, now);
        var latest = rows[0].RefractionText + "\n" + rows[0].Text;
        foreach (var rule in package.Rules.Where(r => r.Enabled))
        {
            bool matches = rule.Condition switch
            {
                "DIABETES" => conditions.Diabetes.State == PatientConditionState.Present,
                "HTO" => conditions.OcularHypertension.State == PatientConditionState.Present,
                _ => ExplicitMyopia(latest, rule.SphereLessThan!.Value)
            };
            if (!matches) continue;
            int index = rule.Exam == "FO_OCT" ? 0 : rule.Exam == "V3F" ? 2 : 3;
            bool conflict = index == 0 && (FundusAnalysis.Analyze(rows, now, appointment)?.HasConflict == true ||
                FundusAnalysis.Detect(latest).Any(f => f.Kind == FoFindingKind.Cancelled));
            var last = index == 0 ? ExamReviewContext.LastPerformed(rows, now) : null;
            // V3F/Skiacol dates require explicit review: a drug mention or an old report is not proof of performance.
            bool due = !conflict && last != null && last.Date.Date.AddMonths(rule.IntervalMonths) <= appointment.Date;
            if (due || conflict || !predictions[index].Decision.HasValue || (predictions[index].Decision == false && last == null))
            {
                var reason = "Règle locale validée " + rule.Id.ToString("N")[..8] + " : " + Describe(rule) + " " +
                    (conflict ? "Contradiction ou annulation : à confirmer." : last == null ? "Date du dernier examen non établie : à confirmer." : "Échéance : " + last.Date.Date.AddMonths(rule.IntervalMonths).ToString("dd/MM/yyyy") + (due ? "." : ". Autres indications à vérifier."));
                predictions[index] = new(due ? true : null, due ? true : null, 0, 0, reason, Array.Empty<string>());
                if (index == 0) predictions[1] = predictions[0];
            }
        }
        return predictions;
    }
}
