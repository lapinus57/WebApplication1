using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

internal static class FoClinicalContext
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private const string EyeReport = @"\b(?:v\d+\s*)?fo\s*(?:dg|d|g)\b";
    private const string MacularWorkup = @"\bbilan\s+maculaire\b";
    private const string ComparativeWorkup = @"\bbilan\s+comp[a-z]{0,12}atif\b";
    private static bool Has(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, Limit);

    public static IReadOnlyList<string> Clauses(string original)
    {
        var clauses = new List<string>();
        foreach (var part in Regex.Split(original, @"[\r\n;,]+|(?<![FfOo]\.)(?<=[.!?])\s+|(?==>)", RegexOptions.CultureInvariant, Limit))
        {
            var line = part.Trim();
            if (line.Length == 0) continue;
            if (clauses.Count > 0)
            {
                var previous = FundusAnalysis.Normalize(clauses[^1]);
                var text = FundusAnalysis.Normalize(line);
                bool delay = Has(text, @"^(?:=>\s*)?" + FundusAnalysis.Delay) &&
                    (Has(previous, FundusAnalysis.RepeatRequest) || Has(previous, FundusAnalysis.Exam) ||
                     Has(previous, MacularWorkup) || Has(previous, ComparativeWorkup));
                bool splitRequest = Has(previous, @"^(?:=>\s*)?(?:refaire|recontroler|controler|renouveler)(?:\s+(?:l['’])?examen)?$") &&
                    Has(text, @"^(?:examen\s+)?(?:(?:du|des|de|le|les)\s+)?" + FundusAnalysis.Exam);
                if (delay || splitRequest)
                {
                    clauses[^1] += "\n" + line;
                    continue;
                }
            }
            clauses.Add(line);
        }
        return clauses;
    }

    public static FoFinding? Find(FoConsultation consultation)
    {
        var clauses = Clauses(consultation.Text).Concat(Clauses(consultation.RefractionText))
            .Select(original => (Original: original, Text: FundusAnalysis.Normalize(original))).ToList();
        bool IsOct(string text)
        {
            if (!Has(text, @"\boct\b")) return false;
            // "CV 24 OCT" is an examination list, whereas "23 oct. 2026" is a date.
            var dates = Regex.Replace(text, @"\bcv\s*24(?:\s+2)?\b", "cv", RegexOptions.CultureInvariant, Limit);
            return !Has(dates, @"\b\d{1,2}\s+oct\b|\boct\.?\s+(?:19|20)\d{2}\b");
        }
        bool Excluded(string text) => Has(text, @"\b(?:ancien\w*|antecedent\w*|il y a|en (?:19|20)\d{2})\b") ||
            Has(text, @"\b(?:pas de|sans|annul\w*|aucun)\s+(?:bilan\s+)?(?:oct|fo\s*(?:dg|d|g)|maculaire)\b") ||
            Has(text, @"\b(?:oct|fo\s*(?:dg|d|g)|bilan maculaire)\s+(?:non indique|non necessaire|inutile|annule)\b");
        var eligible = clauses.Where(c => !Excluded(c.Text)).ToList();
        bool hasImaging = eligible.Any(c => IsOct(c.Text) || Has(c.Text, EyeReport));
        var workup = eligible.FirstOrDefault(c => Has(c.Text, MacularWorkup) || (hasImaging && Has(c.Text, ComparativeWorkup)));
        if (workup.Original != null)
            return new(FoFindingKind.Potential, workup.Original, "Bilan mentionné ; vérifier si un FO est à préparer.");
        // A normal report, OCT alone or a generic annual visit is not a new FO instruction.
        return null;
    }

    public static FoFinding? PeripheralRequest(FoConsultation consultation)
    {
        var all = FundusAnalysis.Normalize(consultation.Text + "\n" + consultation.RefractionText);
        if (!Has(all, EyeReport + @"|\bretin\w*\b")) return null;
        foreach (var clause in Clauses(consultation.Text).Concat(Clauses(consultation.RefractionText)))
        {
            var text = FundusAnalysis.Normalize(clause);
            const string action = @"\b(?:recontrole[r]?|controle[r]?|revoir|surveiller)\s+(?:la\s+)?peripherie\b";
            if (!Has(text, action) || Has(text, @"\b(?:pas|plus|jamais|inutile de|pas besoin de)\s+" + action) ||
                Has(text, @"\b(?:avait|ancien\w*|antecedent\w*|il y a)\b")) continue;
            return new(Has(text, @"\b(?:si|possible|eventuel\w*|a discuter|au besoin)\b|\?") ? FoFindingKind.Potential : FoFindingKind.Request,
                clause, "Contrôle de la périphérie rétinienne demandé ; préparation à confirmer.");
        }
        return null;
    }
}
