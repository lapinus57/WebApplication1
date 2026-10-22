using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Client.SmsMail.Models;

namespace Client.SmsMail.Utilities
{
    public static class AddressNormalizer
    {
        public static string NormalizeAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            address = address.ToLowerInvariant();

            address = new string(address
                .Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray());

            var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {" le ", " "}, {" la ", " "}, {" l'", " "},
                {" de ", " "}, {" du ", " "},
                {" st ", " saint "}, {" ste ", " sainte "},
                {"lieu dit", string.Empty}, {" lot ", " lotissement "}
            };

            foreach (var kvp in substitutions)
            {
                address = address.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
            }

            var sb = new StringBuilder(address.Length);
            foreach (var ch in address)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        public static bool AreAddressesSimilar(string? address1, string? address2, RdvInfo? rdv1, RdvInfo? rdv2)
        {
            if (string.IsNullOrWhiteSpace(address1) || string.IsNullOrWhiteSpace(address2) || rdv1 is null || rdv2 is null)
            {
                return false;
            }

            var normalized1 = NormalizeAddress(address1);
            var normalized2 = NormalizeAddress(address2);

            if (normalized1 == normalized2)
            {
                return true;
            }

            var distance = LevenshteinDistance.Compute(normalized1, normalized2);
            var maxLength = Math.Max(normalized1.Length, normalized2.Length);
            if (maxLength == 0)
            {
                return false;
            }

            var similarity = 1.0 - (distance / (double)maxLength);

            var timeDiff = Math.Abs((rdv1.HeureRDV - rdv2.HeureRDV).TotalMinutes);
            var priseDiff = Math.Abs((rdv1.DatePriseRDV - rdv2.DatePriseRDV).TotalMinutes);

            var hasCommonContact =
                (!string.IsNullOrEmpty(rdv1.Tel1) && (rdv1.Tel1 == rdv2.Tel1 || rdv1.Tel1 == rdv2.Tel2)) ||
                (!string.IsNullOrEmpty(rdv1.Tel2) && (rdv1.Tel2 == rdv2.Tel1 || rdv1.Tel2 == rdv2.Tel2)) ||
                (!string.IsNullOrEmpty(rdv1.Email) && rdv1.Email == rdv2.Email);

            return similarity > 0.8 && timeDiff <= 90 && timeDiff > 10 && priseDiff < 15 && hasCommonContact;
        }
    }

    public static class LevenshteinDistance
    {
        public static int Compute(string source, string target)
        {
            if (source == target)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(source))
            {
                return target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var sourceLength = source.Length;
            var targetLength = target.Length;

            var distances = new int[sourceLength + 1, targetLength + 1];

            for (var i = 0; i <= sourceLength; i++)
            {
                distances[i, 0] = i;
            }

            for (var j = 0; j <= targetLength; j++)
            {
                distances[0, j] = j;
            }

            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                    var deletion = distances[i - 1, j] + 1;
                    var insertion = distances[i, j - 1] + 1;
                    var substitution = distances[i - 1, j - 1] + cost;

                    var distance = Math.Min(Math.Min(deletion, insertion), substitution);

                    if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                    {
                        distance = Math.Min(distance, distances[i - 2, j - 2] + 1);
                    }

                    distances[i, j] = distance;
                }
            }

            return distances[sourceLength, targetLength];
        }
    }
}

