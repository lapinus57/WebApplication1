using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Client.Helpers
{
    public static class PatientStringHelper
    {
        public static void ExtractInfoFromInput(string inputText, out string patientTitle, out string patientLastName, out string patientFirstName)
        {
            patientTitle = string.Empty;
            patientLastName = string.Empty;
            patientFirstName = string.Empty;

            try
            {
                var sanitizedInput = (inputText ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(sanitizedInput))
                    return;

                string patternTitleName = @"^(?i)(Mr\.?|Mlle|Mademoiselle|Mme|Monsieur|Madame|Enfant)?\s*(?<name>[\p{L}'\-\s]+)(?:\s+n[eé]e\s+[\p{L}'\-\s]+)?\s+(?<firstName>[\p{L}'\-\s]+)\s*$";

                var matchTitleName = Regex.Match(sanitizedInput, patternTitleName);

                if (matchTitleName.Success)
                {
                    string titre = matchTitleName.Groups[1].Value;
                    string nom = matchTitleName.Groups["name"].Value;
                    string prenom = matchTitleName.Groups["firstName"].Value;

                    patientTitle = string.IsNullOrWhiteSpace(titre) ? string.Empty : titre.Trim();
                    patientLastName = NormalizeLastName(nom);
                    patientFirstName = NormalizeFirstName(prenom);
                    return;
                }

                ApplyFallbackParsing(sanitizedInput, out patientTitle, out patientLastName, out patientFirstName);
            }
            catch
            {
                patientTitle = string.Empty;
                patientLastName = string.Empty;
                patientFirstName = string.Empty;
            }
        }

        private static void ApplyFallbackParsing(string input, out string patientTitle, out string patientLastName, out string patientFirstName)
        {
            patientTitle = string.Empty;
            patientLastName = string.Empty;
            patientFirstName = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            string workingInput = input;
            string[] possibleTitles = new[] { "MR", "MR.", "MLE", "MLLE", "MADEMOISELLE", "MME", "MONSIEUR", "MADAME", "ENFANT" };

            foreach (var possibleTitle in possibleTitles)
            {
                if (workingInput.StartsWith(possibleTitle, StringComparison.OrdinalIgnoreCase))
                {
                    patientTitle = NormalizeFirstName(possibleTitle);
                    workingInput = workingInput[possibleTitle.Length..].TrimStart();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(workingInput))
            {
                return;
            }

            var tokens = workingInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1)
            {
                patientLastName = tokens[0].ToUpperInvariant();
                return;
            }

            string firstNameRaw = tokens[^1];
            string lastNameRaw = string.Join(' ', tokens, 0, tokens.Length - 1);

            patientLastName = NormalizeLastName(lastNameRaw);
            patientFirstName = NormalizeFirstName(firstNameRaw);
        }

        private static string NormalizeLastName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return normalized.ToUpperInvariant();
        }

        private static string NormalizeFirstName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var textInfo = CultureInfo.GetCultureInfo("fr-FR").TextInfo;
            var normalized = Regex.Replace(value, @"\s+", " ").Trim();

            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                var segments = words[i].Split('-', StringSplitOptions.None);
                for (int j = 0; j < segments.Length; j++)
                {
                    var segment = segments[j];
                    if (string.IsNullOrEmpty(segment))
                        continue;

                    var lowered = segment.ToLowerInvariant();
                    segments[j] = textInfo.ToTitleCase(lowered);
                }

                words[i] = string.Join('-', segments);
            }

            return string.Join(' ', words);
        }
    }
}
