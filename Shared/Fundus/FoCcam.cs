using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EyeChat.Fundus;

public static class FoCcam
{
    // Exact CCAM codes, not the cabinet's local Consultation.[Code acte] identifiers.
    // ATIH, chapter 02.01.06.03; source and interpretation limits in docs/preparation-fo.md.
    private static readonly HashSet<string> FundusCodes = new(StringComparer.Ordinal)
        { "BGQP002", "BGQP003", "BGQP004", "BZQK001" };
    // BZQK001 officially describes OCT; the cabinet explicitly uses it for OCT-FO.
    // This is a local workflow convention, never a universal proof of a dilated FO.

    public static string Parse(object? value)
    {
        var text = value == null || value == DBNull.Value ? "" : Convert.ToString(value) ?? "";
        if (text.Length > 4096) throw new FoDataException("Le contenu du champ Tarif.ACTE est trop long. Vérifiez la liaison des actes.");
        var codes = Regex.Matches(text.ToUpperInvariant(), @"(?<![A-Z0-9])[A-Z]{4}[0-9]{3}(?![A-Z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Select(m => m.Value).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (codes.Count > 32) throw new FoDataException("Trop de codes CCAM pour un acte. Vérifiez le champ Tarif.ACTE.");
        return string.Join(" + ", codes);
    }

    public static string Summarize(IEnumerable<string> values)
    {
        var codes = values.SelectMany(v => Parse(v).Split(" + ", StringSplitOptions.RemoveEmptyEntries))
            .Distinct().OrderBy(c => c, StringComparer.Ordinal).Take(13).ToList();
        return string.Join(" + ", codes.Take(12)) + (codes.Count > 12 ? " …" : "");
    }

    public static string FundusEvidence(FoConsultation consultation) => !consultation.ActLinkAvailable ? "" : string.Join(" + ",
        Parse(consultation.CcamCodes).Split(" + ", StringSplitOptions.RemoveEmptyEntries).Where(FundusCodes.Contains));

    public static string Describe(FoConsultation consultation) => !consultation.ActLinkAvailable
        ? "CCAM non disponible : liaison Consultation.Code acte → Tarif.ACTE absente."
        : consultation.CcamCodes.Length == 0 ? "Aucun code CCAM reconnu pour cette consultation."
        : "CCAM : " + consultation.CcamCodes;
}
