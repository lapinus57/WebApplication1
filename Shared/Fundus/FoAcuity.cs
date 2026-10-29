using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

/// <summary>
/// Reads far visual acuity written in tenths ("OD 8/10", "-7,00 = 10 /10") and reports a marked drop
/// at the latest visit. It only suggests a manual check; it never establishes a diagnosis.
/// </summary>
public static class FoAcuity
{
    // Tenths is 0 for vision below 1/10 written in words (CLD, VBLM, PL, "noir").
    public sealed record Measure(string Eye, decimal Tenths, DateTime Date, string ConsultationId, string Line)
    {
        public string Label => Tenths == 0 ? "< 1/10" : Tenths.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + "/10";
    }
    public sealed record Drop(string Eye, Measure Before, Measure After);

    /// <summary>Minimum loss, in tenths, between the previous and the latest best acuity of one eye.</summary>
    public const decimal Threshold = 3m;

    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    // Eye markers: OD/OG, or both eyes (ODG, AO, AB for binocular acuity, "2 yeux") which cannot be assigned to one eye.
    private const string EyeMarker = @"\b(?<eye>odg|od\s*g|ao|ab|2\s*yeux|od|og)\b";
    // A value in tenths; "10/10/2024" (a date) and "8,60 / 14" (other measures) are excluded.
    // "3-4 /10f" is read as 4 (hyphens become spaces); the letter after /10 (F, f, e) is ignored.
    // Vision below 1/10 written in words counts as 0: "= noir", CLD, VBLM, PL+/PL-.
    private const string Value = @"(?<![\d/.,])(?<v>\d{1,2}(?:[.,]\d{1,2})?)\s*/\s*10(?:\s*(?:e|eme)s?\b)?(?!\s*/\s*\d)(?![\d.,]*\d)"
        + @"|(?<low>=\s*noir\b|\bcld\b|\bcompte (?:les )?doigts\b|\bvblm\b|\bpl\s*[+]?(?!\w)|\bperception lumineuse\b)";
    private const string Excluded = @"\b(?:ancien\w*|anterieur\w*|avant|il y a|en (?:19|20)\d{2}|familia\w*)\b|\?";

    public static IReadOnlyList<Measure> Read(FoConsultation consultation)
    {
        var result = new List<Measure>();
        foreach (var field in new[] { consultation.Text, consultation.RefractionText })
        {
            string? eye = null; // Carried over lines, as in "OD:" followed by indented refraction lines.
            foreach (var original in field.Split('\n'))
            {
                var line = Normalize(original);
                var markers = Regex.Matches(line, EyeMarker, RegexOptions.CultureInvariant, Limit).Cast<Match>().ToList();
                var values = Regex.Matches(line, Value, RegexOptions.CultureInvariant, Limit).Cast<Match>().ToList();
                if (values.Count > 0 && !Regex.IsMatch(line, Excluded, RegexOptions.CultureInvariant, Limit))
                {
                    // "8/10 OD 10/10 OG" writes the eye after its value; "OD 8/10 OG 10/10" before it.
                    bool postfix = markers.Count > 0 && markers[0].Index > values[0].Index;
                    foreach (var value in values)
                    {
                        var owner = postfix
                            ? markers.FirstOrDefault(m => m.Index > value.Index)
                            : markers.LastOrDefault(m => m.Index < value.Index);
                        var valueEye = owner != null ? Eye(owner) : markers.Count == 0 ? eye : null;
                        if (valueEye == null) continue;
                        if (value.Groups["low"].Success)
                            result.Add(new(valueEye, 0, consultation.Date, consultation.Id, original.Trim()));
                        else if (decimal.TryParse(value.Groups["v"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var tenths)
                            && tenths > 0 && tenths <= 20)
                            result.Add(new(valueEye, tenths, consultation.Date, consultation.Id, original.Trim()));
                    }
                }
                if (markers.Count > 0) eye = Eye(markers[^1]);
            }
        }
        return result;
    }

    /// <summary>Drops observed at the latest visit, compared with the previous visit that measured the same eye.</summary>
    public static IReadOnlyList<Drop> LatestDrops(IEnumerable<FoConsultation> consultations)
    {
        var byDate = consultations.GroupBy(c => c.Date).OrderBy(g => g.Key)
            .Select(g => g.SelectMany(Read).GroupBy(m => m.Eye).ToDictionary(e => e.Key, e => e.MaxBy(m => m.Tenths)!))
            .ToList();
        if (byDate.Count < 2) return Array.Empty<Drop>();
        var latest = byDate[^1];
        var drops = new List<Drop>();
        foreach (var eye in new[] { "OD", "OG" })
        {
            if (!latest.TryGetValue(eye, out var after)) continue;
            var before = byDate.Take(byDate.Count - 1).Select(d => d.GetValueOrDefault(eye)).LastOrDefault(m => m != null);
            if (before != null && before.Tenths - after.Tenths >= Threshold) drops.Add(new(eye, before, after));
        }
        return drops;
    }

    public static string Describe(Drop drop) =>
        $"{drop.Eye} {drop.Before.Label} le {drop.Before.Date:dd/MM/yyyy} → {drop.After.Label} le {drop.After.Date:dd/MM/yyyy}";

    private static string? Eye(Match marker) => marker.Groups["eye"].Value switch { "od" => "OD", "og" => "OG", _ => null };

    // Keep commas and slashes, unlike clause splitting: they carry the acuity values.
    private static string Normalize(string text) => Regex.Replace(FundusAnalysis.Normalize(text), @"\s+", " ", RegexOptions.CultureInvariant, Limit);
}
