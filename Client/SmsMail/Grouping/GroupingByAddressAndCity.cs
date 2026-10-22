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
                    familleCorrespondante = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                }

                FamilyUtilities.AddAppointmentToFamily(familleCorrespondante, rdv);
                MarquerFamilleAvecCorrespondanceAdresse(familleCorrespondante, "Adresse et Ville");
            }

            var restants = rdvs.Where(r => !r.Istried).ToList();
            for (var i = 0; i <= restants.Count - 2; i++)
            {
                for (var j = i + 1; j <= restants.Count - 1; j++)
                {
                    var r1 = restants[i];
                    var r2 = restants[j];

                    if (string.Equals(r1.Ville?.Trim(), r2.Ville?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        AddressNormalizer.AreAddressesSimilar(r1.Adresse, r2.Adresse, r1, r2))
                    {
                        var nouvelleFamille = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                        FamilyUtilities.AddAppointmentToFamily(nouvelleFamille, r1);
                        FamilyUtilities.AddAppointmentToFamily(nouvelleFamille, r2);
                        MarquerFamilleAvecCorrespondanceAdresse(nouvelleFamille, "Adresse et Ville");
                        break;
                    }
                }
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

