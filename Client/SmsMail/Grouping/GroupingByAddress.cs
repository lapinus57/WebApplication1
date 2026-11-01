using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByAddress
    {
        public static void Regrouper(ObservableCollection<RdvInfo> rdvs, List<RdvFamille> familles, ref int numeroFamille)
        {
            foreach (var rdv in rdvs.Where(r => !r.Istried))
            {
                var familleCorrespondante = TrouverFamille(familles, rdv);

                if (familleCorrespondante is not null)
                {
                    FamilyUtilities.AddAppointmentToFamily(familleCorrespondante, rdv);
                    MarquerFamilleAvecCorrespondanceAdresse(familleCorrespondante, "Adresse identique");
                }
                else
                {
                    familleCorrespondante = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                    FamilyUtilities.AddAppointmentToFamily(familleCorrespondante, rdv);
                }
            }
        }

        private static RdvFamille? TrouverFamille(IEnumerable<RdvFamille> familles, RdvInfo rdv)
        {
            if (string.IsNullOrWhiteSpace(rdv.Adresse) || string.IsNullOrWhiteSpace(rdv.Ville)) return null;
            return familles.FirstOrDefault(famille =>
                famille.Adresse.Any(adresse => string.Equals(adresse.Trim(), rdv.Adresse.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                famille.Ville.Any(ville => string.Equals(ville.Trim(), rdv.Ville.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        private static void MarquerFamilleAvecCorrespondanceAdresse(RdvFamille famille, string method)
        {
            foreach (var rdv in famille.RDVs)
            {
                rdv.TriedMethodRound2 = method;
                rdv.FamilyMethodRound2 = famille.Numero;
            }
        }
    }
}


