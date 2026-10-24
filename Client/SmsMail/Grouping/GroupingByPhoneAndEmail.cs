using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByPhoneAndEmail
    {
        private const int CreationMinutesThreshold = 10;
        private const int AppointmentMinutesThreshold = 30;

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
                string rule;

                if (!string.IsNullOrEmpty(preparation.NormalizedTel1) &&
                    telMap.TryGetValue(preparation.NormalizedTel1, out famille))
                {
                    rule = "Téléphone 1 commun avec un membre du groupe" + " (" + string.Join(", ", famille.RDVs
                        .Where(r => NormalizePhoneNumber(r.Tel1) == preparation.NormalizedTel1 || NormalizePhoneNumber(r.Tel2) == preparation.NormalizedTel1)
                        .Select(FamilyGroupingTrace.Person)) + ")";
                }
                else if (!string.IsNullOrEmpty(preparation.NormalizedTel2) &&
                         telMap.TryGetValue(preparation.NormalizedTel2, out famille))
                {
                    rule = "Téléphone 2 commun avec un membre du groupe" + " (" + string.Join(", ", famille.RDVs
                        .Where(r => NormalizePhoneNumber(r.Tel1) == preparation.NormalizedTel2 || NormalizePhoneNumber(r.Tel2) == preparation.NormalizedTel2)
                        .Select(FamilyGroupingTrace.Person)) + ")";
                }
                else if (!string.IsNullOrEmpty(preparation.NormalizedEmail) &&
                         mailMap.TryGetValue(preparation.NormalizedEmail, out famille))
                {
                    rule = "Adresse mail commune avec un membre du groupe" + " (" + string.Join(", ", famille.RDVs
                        .Where(r => string.Equals(r.Email?.Trim(), preparation.NormalizedEmail, StringComparison.OrdinalIgnoreCase))
                        .Select(FamilyGroupingTrace.Person)) + ")";
                }
                else
                {
                    famille = TryFindFamilyByNameAndTemporalProximity(familles, rdv);
                    rule = "Nom et proximité de création (10 min) ou de rendez-vous (30 min)";
                }

                if (famille is null)
                {
                    prochainNumeroFamille += 1;
                    famille = new RdvFamille { Numero = prochainNumeroFamille };
                    familles.Add(famille);
                }

                FamilyUtilities.AddAppointmentToFamily(famille, rdv, preparation.NormalizedTel1, preparation.NormalizedTel2, preparation.NormalizedEmail);
                FamilyGroupingTrace.Record(rule, famille.RDVs);

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

        private static RdvFamille? TryFindFamilyByNameAndTemporalProximity(IEnumerable<RdvFamille> familles, RdvInfo rdv)
        {
            if (string.IsNullOrWhiteSpace(rdv.Nom))
            {
                return null;
            }

            var normalizedName = NormalizeName(rdv.Nom);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return null;
            }

            var creationDate = rdv.DatePriseRDV;
            var appointmentDate = GetEffectiveAppointmentDateTime(rdv);

            foreach (var famille in familles)
            {
                if (!famille.Nom.Any(n => NormalizeName(n) == normalizedName))
                {
                    continue;
                }

                if (creationDate != DateTime.MinValue &&
                    famille.DatePriseRDV.Any(d => AreWithinCreationWindow(creationDate, d)))
                {
                    return famille;
                }

                if (appointmentDate is null)
                {
                    continue;
                }

                if (famille.HeureRDV.Any(h => AreWithinAppointmentWindow(appointmentDate.Value, h)))
                {
                    return famille;
                }

                if (famille.DateRDV != default &&
                    AreWithinAppointmentWindow(appointmentDate.Value, famille.DateRDV))
                {
                    return famille;
                }

                foreach (var existingRdv in famille.RDVs)
                {
                    var existingAppointment = GetEffectiveAppointmentDateTime(existingRdv);
                    if (existingAppointment is not null &&
                        AreWithinAppointmentWindow(appointmentDate.Value, existingAppointment.Value))
                    {
                        return famille;
                    }
                }
            }

            return null;
        }

        private static bool AreWithinCreationWindow(DateTime first, DateTime second)
        {
            if (first == DateTime.MinValue || second == DateTime.MinValue)
            {
                return false;
            }

            return Math.Abs((first - second).TotalMinutes) <= CreationMinutesThreshold;
        }

        private static bool AreWithinAppointmentWindow(DateTime first, DateTime second)
        {
            return Math.Abs((first - second).TotalMinutes) <= AppointmentMinutesThreshold;
        }

        private static DateTime? GetEffectiveAppointmentDateTime(RdvInfo rdv)
        {
            if (rdv.HeureRDV != default)
            {
                return rdv.HeureRDV;
            }

            if (rdv.DateRDV != default)
            {
                return rdv.DateRDV;
            }

            return null;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = name.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetter(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
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

