using System;

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
                string patternTitleName = @"^(?i)(Mr\.?|Mlle|Mademoiselle|Mme|Monsieur|Madame|Enfant)?\s*(?<name>[\p{L}'\-\s]+?)(?:\s+n[eé]e\s+[\p{L}'\-\s]+)?\s+(?<firstName>[\p{L}'-]+)";

                var matchTitleName = Regex.Match(inputText ?? string.Empty, patternTitleName);

                if (matchTitleName.Success)
                {
                    string titre = matchTitleName.Groups[1].Value;
                    string nom = matchTitleName.Groups["name"].Value;
                    string prenom = matchTitleName.Groups["firstName"].Value;

                    patientLastName = nom.ToUpperInvariant();
                    patientTitle = string.IsNullOrWhiteSpace(titre) ? string.Empty : titre;

                    if (!string.IsNullOrEmpty(prenom))
                    {
                        patientFirstName = char.ToUpper(prenom[0]) + prenom.Substring(1).ToLowerInvariant();
                    }
                }
            }
            catch
            {
                patientTitle = string.Empty;
                patientLastName = string.Empty;
                patientFirstName = string.Empty;
            }
        }
    }
}
