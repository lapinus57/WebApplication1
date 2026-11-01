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
            var contactMap = new Dictionary<string, RdvFamille>(StringComparer.Ordinal);

            foreach (var famille in familles)
            {
                foreach (var contact in famille.RDVs.SelectMany(Contacts))
                {
                    contactMap[contact] = famille;
                }
            }

            var prochainNumeroFamille = familles.Count > 0 ? familles.Max(f => f.Numero) : 0;

            foreach (var rdv in rdvs.Where(r => !r.Istried).ToList())
            {
                var contacts = Contacts(rdv).ToList();

                // A shared name or nearby booking time is not evidence of a family.
                // Every family reached through one of the contacts is merged, so the
                // result does not depend on the order of the appointments.
                var trouvees = contacts
                    .Select(c => contactMap.TryGetValue(c, out var f) ? f : null)
                    .OfType<RdvFamille>()
                    .Distinct()
                    .ToList();

                RdvFamille famille;
                if (trouvees.Count == 0)
                {
                    prochainNumeroFamille += 1;
                    famille = new RdvFamille { Numero = prochainNumeroFamille };
                    familles.Add(famille);
                }
                else
                {
                    famille = trouvees[0];
                    foreach (var autre in trouvees.Skip(1))
                    {
                        foreach (var membre in autre.RDVs.ToList())
                        {
                            FamilyUtilities.AddAppointmentToFamily(famille, membre);
                        }

                        familles.Remove(autre);
                        foreach (var key in contactMap.Where(p => ReferenceEquals(p.Value, autre)).Select(p => p.Key).ToList())
                        {
                            contactMap[key] = famille;
                        }
                    }
                }

                FamilyUtilities.AddAppointmentToFamily(famille, rdv);
                foreach (var contact in contacts)
                {
                    contactMap[contact] = famille;
                }
            }

            SupprimerFamillesAvecUneSeulePersonne(familles);
            foreach (var membre in familles.SelectMany(f => f.RDVs).Where(r => r.ReferenceFamilyNumber == 0))
            {
                membre.TriedMethodRound1 = "Téléphone ou e-mail";
                membre.FamilyMethodRound1 = membre.NumeroFamille;
            }

            UpdateDatePriseRDVStats(familles);

            numeroFamille = Math.Max(numeroFamille, prochainNumeroFamille + 1);
        }

        private static IEnumerable<string> Contacts(RdvInfo rdv)
        {
            foreach (var tel in new[] { rdv.Tel1, rdv.Tel2 })
            {
                var normalized = FamilyUtilities.GroupingPhone(tel);
                if (normalized.Length > 0)
                {
                    yield return "tel:" + normalized;
                }
            }

            foreach (var mail in FamilyUtilities.GroupingEmails(rdv.Email))
            {
                yield return "mail:" + mail;
            }
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
    }
}
