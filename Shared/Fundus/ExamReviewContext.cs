using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public static class ExamReviewContext
{
    public static bool Performed(FoConsultation c) =>
        !FundusAnalysis.Detect(c.Text + "\n" + c.RefractionText).Any(f => f.Kind is FoFindingKind.Cancelled or FoFindingKind.Potential)
        && (FoCcam.FundusEvidence(c).Length > 0 || FundusAnalysis.Detect(c.Text + "\n" + c.RefractionText).Any(f => f.Kind == FoFindingKind.Performed));

    private static bool Documented(IReadOnlyList<FoConsultation> rows, FoConsultation candidate) => Performed(candidate) &&
        !rows.Where(c => c.Date == candidate.Date).Any(c => FundusAnalysis.Detect(c.Text + "\n" + c.RefractionText)
            .Any(f => f.Kind is FoFindingKind.Cancelled or FoFindingKind.Potential));

    public static FoConsultation? LastPerformed(IEnumerable<FoConsultation> visits, DateTime now)
    {
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ToArray();
        return rows.FirstOrDefault(c => Documented(rows, c));
    }

    public static string Summary(IEnumerable<FoConsultation> visits, DateTime now, DateTime appointment, DateTime? birth)
    {
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ToArray();
        int? age = birth.HasValue && birth <= appointment ? appointment.Year - birth.Value.Year - (birth.Value.AddYears(appointment.Year - birth.Value.Year) > appointment ? 1 : 0) : null;
        var last = rows.FirstOrDefault(); var fo = rows.FirstOrDefault(c => Documented(rows, c));
        return "Âge au rendez-vous : " + (age is >= 0 and <= 125 ? age + " ans" : "non renseigné")
            + " · Dernière consultation : " + (last?.Date.ToString("dd/MM/yyyy") ?? "non retrouvée")
            + " · Dernier FO/OCT documenté : " + (fo == null ? "non retrouvé" : fo.Date.ToString("dd/MM/yyyy") + (FoCcam.FundusEvidence(fo).Length > 0 ? " (acte CCAM du cabinet)" : " (mention de réalisation)"));
    }

    public static string History(IEnumerable<FoConsultation> visits, DateTime now)
    {
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ToArray();
        var parts = new List<string>();
        foreach (var fo in rows.Where(c => Documented(rows, c)).Take(5))
        {
            string Format(FoConsultation c) => "Consultation du " + c.Date.ToString("dd/MM/yyyy") + "\nREFRACTION\n" + c.RefractionText + "\nObservation\n" + c.Text;
            parts.Add("Pourquoi ce FO/OCT du " + fo.Date.ToString("dd/MM/yyyy") + " ?\n" + Format(fo));
            var preceding = rows.FirstOrDefault(c => c.Date < fo.Date);
            if (preceding != null) parts.Add("Consultation précédente :\n" + Format(preceding));
        }
        var text = parts.Count == 0 ? "Aucun FO/OCT réalisé clairement documenté dans cet historique." : string.Join("\n\n", parts);
        return text.Length <= 100000 ? text : "Historique trop volumineux pour cet aperçu. Consultez le dossier source.";
    }

    public static ExamPrediction[] ApplyMyopia(IEnumerable<FoConsultation> visits, DateTime now, DateTime appointment, ExamPrediction[] predictions)
    {
        var rows = visits.Where(c => c.Date <= now).OrderByDescending(c => c.Date).ToArray();
        if (rows.Length == 0) return predictions;
        var latest = rows[0];
        // Deliberately require an explicit myopia-labelled value; never interpret a cylinder as a sphere.
        bool high = Regex.Split(latest.RefractionText + "\n" + latest.Text, @"[\r\n;]+|(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Any(c =>
            !Regex.IsMatch(FundusAnalysis.Normalize(c), @"\b(pas|sans|non|ancien\w*|antecedent\w*|familial\w*|possible|suspect\w*|suspicion|doute|hypothese|fausse)\b|\?") &&
            Regex.Matches(c, @"\bmyopie\s*(?:(?:OD|OG|ODG)\s*)?(?:de|:|=)?\s*[−-](\d{1,2}(?:[.,]\d{1,2})?)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250))
                .Cast<Match>().Any(m => decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 5 && value <= 30));
        if (!high) return predictions;
        var analysis = FundusAnalysis.Analyze(rows, now, appointment);
        var lastFo = rows.FirstOrDefault(c => Documented(rows, c));
        bool uncertain = analysis?.HasConflict == true || FundusAnalysis.Detect(latest.Text + "\n" + latest.RefractionText).Any(f => f.Kind == FoFindingKind.Cancelled);
        if (predictions[0].Decision == true && !uncertain) return predictions;
        bool? decision = !uncertain && lastFo != null && lastFo.Date.Date.AddYears(5) <= appointment.Date ? true : null;
        string reason = "Règle du cabinet : myopie explicitement supérieure à 5 dioptries en valeur absolue → FO/OCT tous les 5 ans. " +
            (uncertain ? "Consignes contradictoires ou annulation : à confirmer." : lastFo == null ? "Dernier FO non retrouvé : date à vérifier." : "Dernier FO documenté : " + lastFo.Date.ToString("dd/MM/yyyy") + "; échéance : " + lastFo.Date.Date.AddYears(5).ToString("dd/MM/yyyy") + (decision == true ? "." : ". Échéance non atteinte ; autres motifs à vérifier."));
        predictions[0] = predictions[1] = new(decision, decision, 0, 0, reason, Array.Empty<string>());
        return predictions;
    }
}
