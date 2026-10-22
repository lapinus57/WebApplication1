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

        private static string NormalizePhoneNumber(string? value)
        {
            value = (value ?? string.Empty).Split('(')[0];
            var digits = new string(value.TakeWhile(c => !char.IsLetter(c)).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0033", StringComparison.Ordinal)) digits = "0" + digits.Substring(4);
            else if (digits.StartsWith("33", StringComparison.Ordinal) && digits.Length == 11) digits = "0" + digits.Substring(2);
            return digits;
        }
    }
}

