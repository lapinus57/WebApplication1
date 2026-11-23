using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;

namespace Client.SmsMail.Utilities
{
    public static class FamilyUtilities
    {
        public static RdvFamille CreateNewFamily(ICollection<RdvFamille> families, ref int numeroFamille)
        {
            var family = new RdvFamille
            {
                Numero = numeroFamille++
            };
            families.Add(family);
            return family;
        }

        public static void AddAppointmentToFamily(
            RdvFamille family,
            RdvInfo rdv,
            string? normalizedTel1 = null,
            string? normalizedTel2 = null,
            string? normalizedEmail = null)
        {
            var isNewAppointment = false;
            if (!family.RDVs.Contains(rdv))
            {
                family.RDVs.Add(rdv);
                isNewAppointment = true;
            }

            AddIfNotNullOrWhiteSpace(family.Nom, rdv.Nom);
            AddIfNotNullOrWhiteSpace(family.Prenom, rdv.Prenom);
            AddIfNotNullOrWhiteSpace(family.Adresse, rdv.Adresse);
            AddIfNotNullOrWhiteSpace(family.Ville, rdv.Ville);
            AddIfNotNullOrWhiteSpace(family.Email, normalizedEmail ?? rdv.Email);

            var tel1 = normalizedTel1 ?? NormalizePhoneNumber(rdv.Tel1);
            var tel2 = normalizedTel2 ?? NormalizePhoneNumber(rdv.Tel2);
            AddIfNotNullOrWhiteSpace(family.TelList, tel1);
            AddIfNotNullOrWhiteSpace(family.TelList, tel2);

            if (rdv.DatePriseRDV != default &&
                (isNewAppointment || !family.DatePriseRDV.Contains(rdv.DatePriseRDV)))
            {
                family.DatePriseRDV.Add(rdv.DatePriseRDV);
            }

            if (rdv.HeureRDV != default &&
                (isNewAppointment || !family.HeureRDV.Contains(rdv.HeureRDV)))
            {
                family.HeureRDV.Add(rdv.HeureRDV);
            }

            if (family.DateRDV == default)
            {
                family.DateRDV = rdv.DateRDV;
            }

            rdv.Istried = true;
            rdv.NumeroFamille = family.Numero;
        }

        private static void AddIfNotNullOrWhiteSpace(ICollection<string> list, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(value);
                }
            }
        }

        // Family number from the EyeEmail database, or 0 for a family built from today's data.
        public static int KnownFamilyNumber(RdvFamille? family) =>
            family?.RDVs.Select(r => r.ReferenceFamilyNumber).FirstOrDefault(n => n > 0) ?? 0;

        public static string NormalizePhoneNumber(string? value)
        {
            value = (value ?? string.Empty).Split('(')[0];
            var digits = new string(value.TakeWhile(c => !char.IsLetter(c)).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0033", StringComparison.Ordinal)) digits = "0" + digits.Substring(4);
            else if (digits.StartsWith("33", StringComparison.Ordinal) && digits.Length == 11) digits = "0" + digits.Substring(2);
            return digits;
        }

        // Placeholder numbers such as 0000000000 or 0600000000 must not link patients.
        public static bool IsUsableGroupingPhone(string? normalized)
        {
            if (string.IsNullOrEmpty(normalized) || normalized.Length < 9) return false;
            if (normalized.All(c => c == normalized[0])) return false;
            return normalized.Length <= 2 || normalized.Substring(2).Any(c => c != '0');
        }

        public static string GroupingPhone(string? value)
        {
            var normalized = NormalizePhoneNumber(value);
            return IsUsableGroupingPhone(normalized) ? normalized : string.Empty;
        }

        public static IEnumerable<string> GroupingEmails(string? value) =>
            Client.SmsMail.Services.MessagePreparation.Emails(value ?? string.Empty);

        /// <summary>Écart maximal entre deux rendez-vous d'une même famille reconnue par l'adresse et le nom.</summary>
        public const int MaxHouseholdGapMinutes = 90;

        /// <summary>
        /// Une même adresse ne suffit pas à faire une famille (immeuble, résidence, voisins) :
        /// il faut un téléphone / mail commun, ou un nom commun (nom ou nom de jeune fille) ET des rendez-vous
        /// pris ensemble : le même jour, et à moins de 90 minutes d'écart.
        /// </summary>
        public static bool SameHousehold(RdvInfo a, RdvInfo b) => SharesContact(a, b) || (SharesSurname(a, b) && BookedTogether(a, b));

        public static bool BookedTogether(RdvInfo a, RdvInfo b)
        {
            if (Math.Abs((a.HeureRDV - b.HeureRDV).TotalMinutes) > MaxHouseholdGapMinutes) return false;
            // Date de prise de RDV inconnue sur l'une des deux : seul l'écart entre les rendez-vous compte.
            return a.DatePriseRDV == default || b.DatePriseRDV == default || a.DatePriseRDV.Date == b.DatePriseRDV.Date;
        }

        public static bool SharesContact(RdvInfo a, RdvInfo b)
        {
            var phonesA = new[] { GroupingPhone(a.Tel1), GroupingPhone(a.Tel2) }.Where(p => p.Length > 0);
            var phonesB = new[] { GroupingPhone(b.Tel1), GroupingPhone(b.Tel2) }.Where(p => p.Length > 0);
            return phonesA.Intersect(phonesB).Any() || GroupingEmails(a.Email).Intersect(GroupingEmails(b.Email), StringComparer.OrdinalIgnoreCase).Any();
        }

        public static bool SharesSurname(RdvInfo a, RdvInfo b) => SurnameTokens(a).Overlaps(SurnameTokens(b));

        private static HashSet<string> SurnameTokens(RdvInfo r)
        {
            var text = (r.Nom + " " + r.NomJeuneFille).Normalize(System.Text.NormalizationForm.FormD);
            var letters = new string(text.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .Select(c => char.IsLetter(c) ? char.ToUpperInvariant(c) : ' ').ToArray());
            // Particules (« DE », « LE », « DI »…) ignorées : trois lettres au moins.
            return letters.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3).ToHashSet();
        }
    }
}

