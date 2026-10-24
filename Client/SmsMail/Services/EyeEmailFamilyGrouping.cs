using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Client.SmsMail.Models;
using Client.SmsMail.Services.Grouping;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services
{
    public static class EyeEmailFamilyGrouping
    {
        public static List<RdvFamille> Build(IEnumerable<RdvInfo> appointments,
            IReadOnlyDictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>>? history = null,
            Func<string, string, bool>? confirmation = null)
        {
            var rows = new ObservableCollection<RdvInfo>(appointments.Where(r => !r.IsCancelled).OrderBy(r => r.HeureRDV));
            foreach (var row in rows)
            {
                row.NumeroFamille = 0; row.Istried = false; row.GroupingReasons.Clear();
                row.TriedMethodRound1 = row.TriedMethodRound2 = row.TriedMethodRound3 = row.TriedMethodRound4 = row.TriedMethodRound5 = row.TriedMethodRound6 = string.Empty;
                row.FamilyMethodRound1 = row.FamilyMethodRound2 = row.FamilyMethodRound3 = row.FamilyMethodRound4 = row.FamilyMethodRound5 = row.FamilyMethodRound6 = null;
            }
            var knownFamilies = new List<RdvFamille>();
            var families = new List<RdvFamille>();
            int number = 1;
            foreach (var known in rows.Where(r => r.ReferenceFamilyNumber > 0).GroupBy(r => r.ReferenceFamilyNumber))
            {
                var family = FamilyUtilities.CreateNewFamily(knownFamilies, ref number);
                foreach (var row in known)
                {
                    FamilyUtilities.AddAppointmentToFamily(family, row);
                    row.TriedMethodRound1 = "Famille EyeEmail";
                }
                FamilyGroupingTrace.Record("Appartenance à la famille EyeEmail " + known.Key, family.RDVs);
            }
            rows = new ObservableCollection<RdvInfo>(rows.Where(r => r.ReferenceFamilyNumber == 0));
            GroupingByPhoneAndEmail.Regrouper(rows, families, ref number);
            FamilyGroupingTrace.Run("Adresse et ville correspondantes (normalisation EyeEmail)", families, () => GroupingByAddressAndCity.Regrouper(rows, families, ref number));
            FamilyGroupingTrace.Run("Adresse et ville correspondantes (inclusion de texte EyeEmail)", families, () => GroupingByAddress.Regrouper(rows, families, ref number));
            // A singleton is still eligible for the subsequent remark/name/date passes.
            ReleaseSingletons();
            FamilyGroupingTrace.Run("Regroupement ou fusion par la mention « avec » dans une remarque", families, () => GroupingByRemark.Regrouper(rows, families, ref number));
            FamilyGroupingTrace.Run("Nom et proximité de création (10 min) ou de rendez-vous (30 min)", families, () => GroupingByNameAndCreationDate.Regrouper(rows, families, ref number));
            GroupingByDate.Regrouper(rows, families, ref number,
                history ?? new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>(), confirmation);
            foreach (var row in rows.Where(r => r.NumeroFamille == 0))
                FamilyUtilities.AddAppointmentToFamily(FamilyUtilities.CreateNewFamily(families, ref number), row);

            families.AddRange(knownFamilies);
            var result = families.Where(f => f.RDVs.Count > 0).OrderBy(f => f.RDVs.Min(r => r.HeureRDV)).ToList();
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Numero = i + 1;
                result[i].UpdateStatistics();
                foreach (var row in result[i].RDVs) row.NumeroFamille = i + 1;
            }
            return result;

            void ReleaseSingletons()
            {
                foreach (var family in families.Where(f => f.RDVs.Count == 1 && f.RDVs[0].ReferenceFamilyNumber == 0).ToList())
                {
                    family.RDVs[0].Istried = false; family.RDVs[0].NumeroFamille = 0;
                    families.Remove(family);
                }
            }
        }
    }
}
