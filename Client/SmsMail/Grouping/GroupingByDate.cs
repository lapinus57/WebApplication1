using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByDate
    {
        public static void Regrouper(
            ObservableCollection<RdvInfo> rdvs,
            List<RdvFamille> familles,
            ref int numeroFamille,
            IReadOnlyDictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>> history,
            Func<string, string, bool>? confirmation = null)
        {
            ComparerHeuresPriseRDV(rdvs, familles, confirmation);
            ComparerRDVsEntreEux(rdvs, familles, ref numeroFamille, history, confirmation);
        }

        private static void ComparerHeuresPriseRDV(
            ObservableCollection<RdvInfo> rdvs,
            IEnumerable<RdvFamille> familles,
            Func<string, string, bool>? confirmation)
        {
            foreach (var rdvNonGroupe in rdvs.Where(r => r.NumeroFamille == 0 && !r.Istried))
            {
                foreach (var famille in familles)
                {
                    if (famille.DatePriseRDVMoy is null)
                    {
                        continue;
                    }

                    var difference = Math.Abs((famille.DatePriseRDVMoy.Value - rdvNonGroupe.DatePriseRDV).TotalMinutes);
                    if (difference > 5)
                    {
                        continue;
                    }

                    var familleDetails = string.Join(
                        Environment.NewLine + Environment.NewLine,
                        famille.RDVs.Select(r =>
                            $"{r.Titre} {r.Nom} {r.Prenom} à {r.HeureRDV:HH:mm}{Environment.NewLine}{r.Adresse}, {r.Ville}{Environment.NewLine}Tél 1 : {r.Tel1} - Tél 2 : {r.Tel2} - E-mail : {r.Email}{Environment.NewLine}Remarque : {r.Remarque}"));

                    var messageBuilder = new StringBuilder();
                    messageBuilder.AppendLine("Le rendez-vous suivant semble lié à la famille :");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine($"{rdvNonGroupe.Titre} {rdvNonGroupe.Nom} {rdvNonGroupe.Prenom} à {rdvNonGroupe.HeureRDV:HH:mm}.");
                    messageBuilder.AppendLine($"Adresse : {rdvNonGroupe.Adresse}    Ville : {rdvNonGroupe.Ville}");
                    messageBuilder.AppendLine($"Tél 1 : {rdvNonGroupe.Tel1}    Tél 2 : {rdvNonGroupe.Tel2}    E-mail : {rdvNonGroupe.Email}");
                    messageBuilder.AppendLine($"Remarque : {rdvNonGroupe.Remarque}");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine($"Heure moyenne de prise de RDV de la famille : {famille.DatePriseRDVMoy:HH:mm}");
                    messageBuilder.AppendLine($"Heure de prise de RDV du rendez-vous : {rdvNonGroupe.DatePriseRDV:HH:mm}");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("Détails de la famille :");
                    messageBuilder.AppendLine(familleDetails);
                    messageBuilder.AppendLine();
                    messageBuilder.Append("Voulez-vous l'ajouter à la famille ?");

                    if (confirmation?.Invoke(messageBuilder.ToString(), "Confirmation d'ajout à la famille") == true)
                    {
                        FamilyUtilities.AddAppointmentToFamily(famille, rdvNonGroupe);
                        rdvNonGroupe.FamilyMethodRound2 = famille.Numero;
                        break;
                    }
                }
            }
        }

        private static void ComparerRDVsEntreEux(
            ObservableCollection<RdvInfo> rdvs,
            List<RdvFamille> familles,
            ref int numeroFamille,
            IReadOnlyDictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>> history,
            Func<string, string, bool>? confirmation)
        {
            var datesRendezVous = history;
            var rdvsNonGroupes = rdvs.Where(r => r.NumeroFamille == 0 && !r.Istried).ToList();

            for (var i = 0; i < rdvsNonGroupes.Count - 1; i++)
            {
                for (var j = i + 1; j < rdvsNonGroupes.Count; j++)
                {
                    var rdv1 = rdvsNonGroupes[i];
                    var rdv2 = rdvsNonGroupes[j];

                    if (rdv1.Id == rdv2.Id)
                    {
                        continue;
                    }

                    var differenceDate = Math.Abs((rdv1.DatePriseRDV - rdv2.DatePriseRDV).TotalMinutes);
                    var differenceHeureRDVJour = Math.Abs((rdv1.HeureRDV - rdv2.HeureRDV).TotalMinutes);

                    if (!datesRendezVous.TryGetValue(rdv1.Id, out var rdv1Dates) ||
                        !datesRendezVous.TryGetValue(rdv2.Id, out var rdv2Dates))
                    {
                        continue;
                    }

                    var joursCommuns = rdv1Dates.Select(d => d.Date).Intersect(rdv2Dates.Select(d => d.Date)).ToList();

                    if (differenceDate > 5 || differenceHeureRDVJour < 10 || differenceHeureRDVJour > 30 || joursCommuns.Count == 0)
                    {
                        continue;
                    }

                    var jourValide = false;
                    var detailsJoursCommuns = new StringBuilder();

                    foreach (var jourCommun in joursCommuns)
                    {
                        var detailsRDV1 = rdv1Dates.FirstOrDefault(d => d.Date == jourCommun);
                        var detailsRDV2 = rdv2Dates.FirstOrDefault(d => d.Date == jourCommun);

                        if (detailsRDV1.RdvTime.HasValue && detailsRDV2.RdvTime.HasValue)
                        {
                            var differenceHeureRDV = Math.Abs((detailsRDV1.RdvTime.Value - detailsRDV2.RdvTime.Value).TotalMinutes);
                            if (differenceHeureRDV < 10 || differenceHeureRDV > 30)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }

                        if (detailsRDV1.ArrivalTime.HasValue && detailsRDV2.ArrivalTime.HasValue)
                        {
                            var differenceHeureArrivee = Math.Abs((detailsRDV1.ArrivalTime.Value - detailsRDV2.ArrivalTime.Value).TotalMinutes);
                            if (differenceHeureArrivee >= 10)
                            {
                                continue;
                            }
                        }

                        jourValide = true;
                        detailsJoursCommuns.AppendLine($"Jour commun : {jourCommun:dd/MM/yyyy}");
                        detailsJoursCommuns.AppendLine($"  {rdv1.Nom} {rdv1.Prenom} - Heure RDV : {detailsRDV1.RdvTime?.ToString("HH:mm") ?? "N/A"}, Heure d'arrivée : {detailsRDV1.ArrivalTime?.ToString("HH:mm") ?? "N/A"}");
                        detailsJoursCommuns.AppendLine($"  {rdv2.Nom} {rdv2.Prenom} - Heure RDV : {detailsRDV2.RdvTime?.ToString("HH:mm") ?? "N/A"}, Heure d'arrivée : {detailsRDV2.ArrivalTime?.ToString("HH:mm") ?? "N/A"}");
                        detailsJoursCommuns.AppendLine();
                    }

                    if (!jourValide)
                    {
                        continue;
                    }

                    var messageBuilder = new StringBuilder();
                    messageBuilder.AppendLine("Les rendez-vous suivants semblent liés :");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine($"{rdv1.Titre} {rdv1.Nom} {rdv1.Prenom} à {rdv1.HeureRDV:HH:mm}");
                    messageBuilder.AppendLine($"{rdv1.Adresse}, {rdv1.Ville}");
                    messageBuilder.AppendLine($"Tél 1 : {rdv1.Tel1} - Tél 2 : {rdv1.Tel2} - E-mail : {rdv1.Email}");
                    messageBuilder.AppendLine($"Remarque : {rdv1.Remarque}");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine($"{rdv2.Titre} {rdv2.Nom} {rdv2.Prenom} à {rdv2.HeureRDV:HH:mm}");
                    messageBuilder.AppendLine($"{rdv2.Adresse}, {rdv2.Ville}");
                    messageBuilder.AppendLine($"Tél 1 : {rdv2.Tel1} - Tél 2 : {rdv2.Tel2} - E-mail : {rdv2.Email}");
                    messageBuilder.AppendLine($"Remarque : {rdv2.Remarque}");
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("Détails des jours communs :");
                    messageBuilder.Append(detailsJoursCommuns);
                    messageBuilder.AppendLine("Voulez-vous les regrouper dans une nouvelle famille ?");

                    if (confirmation?.Invoke(messageBuilder.ToString(), "Confirmation de regroupement") == true)
                    {
                        var nouvelleFamille = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
                        FamilyUtilities.AddAppointmentToFamily(nouvelleFamille, rdv1);
                        FamilyUtilities.AddAppointmentToFamily(nouvelleFamille, rdv2);
                        rdvsNonGroupes.Remove(rdv1);
                        rdvsNonGroupes.Remove(rdv2);
                        i--;
                        break;
                    }
                }
            }
        }

    }
}
