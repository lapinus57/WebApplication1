using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByPhoneAndEmail
    {

        public static void Regrouper(
            ObservableCollection<RdvInfo> rdvs,
            List<RdvFamille> familles,
            ref int numeroFamille)
        {
            var telMap = new Dictionary<string, RdvFamille>(StringComparer.Ordinal);
            var mailMap = new Dictionary<string, RdvFamille>(StringComparer.Ordinal);

            foreach (var famille in familles)
            {
                foreach (var t in famille.TelList.Distinct(StringComparer.Ordinal))
                {
                    var normalized = NormalizePhoneNumber(t);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        telMap[normalized] = famille;
                    }
                }

                foreach (var mail in famille.Email.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(mail))
                    {
                        mailMap[mail.Trim().ToLowerInvariant()] = famille;
                    }
                }
            }

            var rdvsATraiter = rdvs.Where(r => !r.Istried).ToList();
            var rdvPreparations = rdvsATraiter
                .AsParallel().AsOrdered()
                .Select(r => new RdvPreparation
                {
                    Rdv = r,
                    NormalizedTel1 = NormalizePhoneNumber(r.Tel1),
                    NormalizedTel2 = NormalizePhoneNumber(r.Tel2),
                    NormalizedEmail = r.Email?.Trim().ToLowerInvariant() ?? string.Empty
                })
                .ToList();

            var prochainNumeroFamille = familles.Count > 0 ? familles.Max(f => f.Numero) : 0;

            foreach (var preparation in rdvPreparations)
            {
                var rdv = preparation.Rdv;
                RdvFamille? famille = null;

                if (!string.IsNullOrEmpty(preparation.NormalizedTel1) &&
                    telMap.TryGetValue(preparation.NormalizedTel1, out famille))
                {
                    // Family found via Tel1
                }
                else if (!string.IsNullOrEmpty(preparation.NormalizedTel2) &&
                         telMap.TryGetValue(preparation.NormalizedTel2, out famille))
                {
                    // Family found via Tel2
                }
                else if (!string.IsNullOrEmpty(preparation.NormalizedEmail) &&
                         mailMap.TryGetValue(preparation.NormalizedEmail, out famille))
                {
                    // Family found via Email
                }
                // A shared name or nearby booking time is not evidence of a family.

                if (famille is null)
                {
                    prochainNumeroFamille += 1;
                    famille = new RdvFamille { Numero = prochainNumeroFamille };
                    familles.Add(famille);
                }

                FamilyUtilities.AddAppointmentToFamily(famille, rdv, preparation.NormalizedTel1, preparation.NormalizedTel2, preparation.NormalizedEmail);

                if (!string.IsNullOrEmpty(preparation.NormalizedTel1))
                {
                    telMap[preparation.NormalizedTel1] = famille;
                }

                if (!string.IsNullOrEmpty(preparation.NormalizedTel2))
                {
                    telMap[preparation.NormalizedTel2] = famille;
                }

                if (!string.IsNullOrEmpty(preparation.NormalizedEmail))
                {
                    mailMap[preparation.NormalizedEmail] = famille;
                }

                rdv.Istried = true;
                rdv.NumeroFamille = famille.Numero;
            }

            SupprimerFamillesAvecUneSeulePersonne(familles);
            UpdateDatePriseRDVStats(familles);

            numeroFamille = Math.Max(numeroFamille, prochainNumeroFamille + 1);
        }

        private static void SupprimerFamillesAvecUneSeulePersonne(ICollection<RdvFamille> familles)
        {
            var famillesAvecUnePersonne = familles.Where(f => f.RDVs.Count == 1).ToList();
            foreach (var famille in famillesAvecUnePersonne)
            {
                foreach (var rdv in famille.RDVs)
                {
                    rdv.Istried = false;
                    rdv.NumeroFamille = 0;
                }

                familles.Remove(famille);
            }
        }

        private static void UpdateDatePriseRDVStats(IEnumerable<RdvFamille> familles)
        {
            foreach (var famille in familles)
            {
                famille.UpdateStatistics();
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

        private sealed class RdvPreparation
        {
            public required RdvInfo Rdv { get; init; }
            public string? NormalizedTel1 { get; init; }
            public string? NormalizedTel2 { get; init; }
            public string NormalizedEmail { get; init; } = string.Empty;
        }
    }
}


