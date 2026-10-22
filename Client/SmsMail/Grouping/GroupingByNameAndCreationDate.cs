using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByNameAndCreationDate
    {
        private const int CreationMinutesThreshold = 10;
        private const int AppointmentMinutesThreshold = 30;
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        public static void Regrouper(ObservableCollection<RdvInfo> rdvs, List<RdvFamille> familles, ref int numeroFamille)
        {
            if (rdvs is null || rdvs.Count == 0)
            {
                return;
            }

            var groupes = rdvs
                .Where(r => !string.IsNullOrWhiteSpace(r.Nom) && r.DatePriseRDV != DateTime.MinValue)
                .GroupBy(r => NormalizeName(r.Nom!), NameComparer);

            foreach (var groupe in groupes)
            {
                var sortedGroup = groupe.OrderBy(r => r.DatePriseRDV).ToList();
                var clusters = RegrouperParProximite(sortedGroup);

                foreach (var cluster in clusters)
                {
                    if (cluster.Count < 2)
                    {
                        continue;
                    }

                    var familleCible = TrouverFamillePourCluster(familles, cluster, groupe.Key);
                    if (familleCible is null)
                    {
                        familleCible = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                    }

                    foreach (var rdv in cluster)
                    {
                        if (rdv.NumeroFamille == familleCible.Numero)
                        {
                            continue;
                        }

                        if (!rdv.Istried || rdv.NumeroFamille == 0)
                        {
                            FamilyUtilities.AddAppointmentToFamily(familleCible, rdv);
                        }
                    }

                    familleCible.IsolatedFamily = false;
                    MarquerFamille(familleCible);
                }
            }
        }

        private static List<List<RdvInfo>> RegrouperParProximite(IReadOnlyList<RdvInfo> rdvList)
        {
            var clusters = new List<List<RdvInfo>>();
            var visited = new HashSet<RdvInfo>();

            for (var i = 0; i < rdvList.Count; i++)
            {
                var start = rdvList[i];
                if (!visited.Add(start))
                {
                    continue;
                }

                var cluster = new List<RdvInfo> { start };
                var queue = new Queue<RdvInfo>();
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();

                    for (var j = 0; j < rdvList.Count; j++)
                    {
                        var candidate = rdvList[j];
                        if (visited.Contains(candidate))
                        {
                            continue;
                        }

                        if (!AreAppointmentsRelated(current, candidate))
                        {
                            continue;
                        }

                        visited.Add(candidate);
                        cluster.Add(candidate);
                        queue.Enqueue(candidate);
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static RdvFamille? TrouverFamillePourCluster(IEnumerable<RdvFamille> familles, List<RdvInfo> cluster, string nomNormalise)
        {
            var familleExistante = cluster.Select(r => r.NumeroFamille).FirstOrDefault(num => num > 0);
            if (familleExistante > 0)
            {
                return familles.FirstOrDefault(f => f.Numero == familleExistante);
            }

            return familles.FirstOrDefault(f =>
            {
                if (!f.Nom.Any(n => NormalizeName(n) == nomNormalise))
                {
                    return false;
                }

                if (f.DatePriseRDV.Any(d => cluster.Any(r => AreWithinCreationWindow(r.DatePriseRDV, d))))
                {
                    return true;
                }

                return f.HeureRDV.Any(h => cluster.Any(r => AreWithinAppointmentWindow(GetEffectiveAppointmentDateTime(r), h)));
            });
        }

        private static void MarquerFamille(RdvFamille famille)
        {
            foreach (var rdv in famille.RDVs)
            {
                if (string.IsNullOrEmpty(rdv.TriedMethodRound4))
                {
                    rdv.TriedMethodRound4 = "Nom + création";
                    rdv.FamilyMethodRound4 = famille.Numero;
                }
                else if (string.IsNullOrEmpty(rdv.TriedMethodRound5))
                {
                    rdv.TriedMethodRound5 = "Nom + création";
                    rdv.FamilyMethodRound5 = famille.Numero;
                }

                if (rdv.NumeroFamille == 0)
                {
                    rdv.NumeroFamille = famille.Numero;
                }

                rdv.Istried = true;
            }
        }

        private static bool AreAppointmentsRelated(RdvInfo first, RdvInfo second)
        {
            if (AreWithinCreationWindow(first.DatePriseRDV, second.DatePriseRDV))
            {
                return true;
            }

            return AreWithinAppointmentWindow(GetEffectiveAppointmentDateTime(first), GetEffectiveAppointmentDateTime(second));
        }

        private static bool AreWithinCreationWindow(DateTime first, DateTime second)
        {
            if (first == DateTime.MinValue || second == DateTime.MinValue)
            {
                return false;
            }

            return Math.Abs((first - second).TotalMinutes) <= CreationMinutesThreshold;
        }

        private static bool AreWithinAppointmentWindow(DateTime? first, DateTime? second)
        {
            if (first is null || second is null)
            {
                return false;
            }

            return Math.Abs((first.Value - second.Value).TotalMinutes) <= AppointmentMinutesThreshold;
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
            var builder = new StringBuilder();

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
    }
}

