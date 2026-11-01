using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByAddressAndCity
    {
        public static void Regrouper(ObservableCollection<RdvInfo> rdvs, List<RdvFamille> familles, ref int numeroFamille)
        {
            foreach (var rdv in rdvs.Where(r => !r.Istried))
            {
                RdvFamille? familleCorrespondante = null;

                foreach (var famille in familles)
                {
                    var villeOK = famille.Ville.Any(v => string.Equals(v.Trim(), rdv.Ville?.Trim(), StringComparison.OrdinalIgnoreCase));
                    var adresseOK = false;

                    if (villeOK)
                    {
                        foreach (var adresse in famille.Adresse)
                        {
                            var refRdv = famille.RDVs.FirstOrDefault(r => string.Equals(r.Adresse, adresse, StringComparison.OrdinalIgnoreCase));
                            if (AddressNormalizer.AreAddressesSimilar(adresse, rdv.Adresse, refRdv, rdv))
                            {
                                adresseOK = true;
                                break;
                            }
                        }
                    }

                    if (villeOK && adresseOK)
                    {
                        familleCorrespondante = famille;
                        break;
                    }
                }

                if (familleCorrespondante is null)
                {
                    // Alone for now: a later appointment may still match this address.
                    familleCorrespondante = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                    FamilyUtilities.AddAppointmentToFamily(familleCorrespondante, rdv);
                    continue;
                }

                FamilyUtilities.AddAppointmentToFamily(familleCorrespondante, rdv);
                MarquerFamilleAvecCorrespondanceAdresse(familleCorrespondante, "Adresse et Ville");
            }

            SupprimerFamillesAvecUneSeulePersonne(familles);
            UpdateDatePriseRDVStats(familles);
        }

        private static void MarquerFamilleAvecCorrespondanceAdresse(RdvFamille famille, string method)
        {
            foreach (var rdv in famille.RDVs)
            {
                rdv.TriedMethodRound2 = method;
                rdv.FamilyMethodRound2 = famille.Numero;
                rdv.NumeroFamille = famille.Numero;
            }
        }

        private static void SupprimerFamillesAvecUneSeulePersonne(ICollection<RdvFamille> familles)
        {
            var toRemove = familles.Where(f => f.RDVs.Count == 1).ToList();
            foreach (var famille in toRemove)
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

