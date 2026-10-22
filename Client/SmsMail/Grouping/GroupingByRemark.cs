using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services.Grouping
{
    public static class GroupingByRemark
    {
        private static readonly Regex AvecPattern = new(@"(?i)\bavec\s+([A-Za-zÀ-ÖØ-öø-ÿ'\-+&/, ]+)", RegexOptions.Compiled);
        private static readonly Regex TitlesPattern = new(@"(?i)\b(monsieur|madame|mademoiselle|mlle|mme|mr)\b", RegexOptions.Compiled);

        public static void Regrouper(ObservableCollection<RdvInfo> rdvs, List<RdvFamille> familles, ref int numeroFamille)
        {
            if (rdvs is null || rdvs.Count == 0)
            {
                return;
            }

            var rdvByFullName = rdvs
                .GroupBy(r => NormalizeName($"{r.Prenom ?? string.Empty} {r.Nom ?? string.Empty}"))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var rdvByLastName = rdvs
                .GroupBy(r => NormalizeName(r.Nom ?? string.Empty))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var rdvsCibles = rdvs.Where(r => !string.IsNullOrWhiteSpace(r.Remarque)).ToList();

            foreach (var rdv in rdvsCibles)
            {
                if (string.IsNullOrWhiteSpace(rdv.Remarque))
                {
                    continue;
                }

                var matches = AvecPattern.Matches(rdv.Remarque);
                if (matches.Count == 0)
                {
                    continue;
                }

                foreach (Match match in matches.Cast<Match>())
                {
                    var nomBrut = match.Groups[1].Value;
                    var nomNettoye = NettoyerNom(nomBrut);
                    if (string.IsNullOrEmpty(nomNettoye))
                    {
                        continue;
                    }

                    var candidats = TrouverCandidats(nomNettoye, rdvByFullName, rdvByLastName);
                    var candidatsPotentiels = candidats
                        .Where(c => c.Id != rdv.Id && c.DateRDV.Date == rdv.DateRDV.Date)
                        .OrderBy(c => CalculerScore(rdv, c))
                        .ToList();

                    if (candidatsPotentiels.Count == 0)
                    {
                        continue;
                    }

                    RdvFamille? familleCible = null;
                    var familleExistantePourRdv = TrouverFamille(familles, rdv.NumeroFamille);

                    foreach (var candidatPotentiel in candidatsPotentiels)
                    {
                        familleCible = AssurerFamillePourCandidat(
                            familles,
                            familleCible,
                            ref familleExistantePourRdv,
                            candidatPotentiel,
                            ref numeroFamille);

                        if (familleCible is null)
                        {
                            break;
                        }

                        FamilyUtilities.AddAppointmentToFamily(familleCible, candidatPotentiel);

                        AppliquerMetadonnees(candidatPotentiel, familleCible.Numero);
                    }

                    if (familleCible is null)
                    {
                        continue;
                    }

                    familleCible = FusionnerFamilles(familles, familleCible, familleExistantePourRdv);

                    FamilyUtilities.AddAppointmentToFamily(familleCible, rdv);

                    AppliquerMetadonnees(rdv, familleCible.Numero);

                    familleCible.IsolatedFamily = false;
                    break;
                }
            }
        }

        private static RdvFamille? AssurerFamillePourCandidat(
            List<RdvFamille> familles,
            RdvFamille? familleActuelle,
            ref RdvFamille? familleExistantePourRdv,
            RdvInfo candidat,
            ref int numeroFamille)
        {
            var familleCandidat = TrouverFamille(familles, candidat.NumeroFamille);

            if (familleActuelle is not null)
            {
                return FusionnerFamilles(familles, familleActuelle, familleCandidat);
            }

            if (familleExistantePourRdv is not null)
            {
                familleExistantePourRdv = FusionnerFamilles(familles, familleExistantePourRdv, familleCandidat);
                return familleExistantePourRdv;
            }

            if (familleCandidat is not null)
            {
                familleExistantePourRdv = familleCandidat;
                return familleCandidat;
            }

            var nouvelleFamille = FamilyUtilities.CreateNewFamily(familles, ref numeroFamille);
            familleExistantePourRdv = nouvelleFamille;
            return nouvelleFamille;
        }

        private static RdvFamille? TrouverFamille(List<RdvFamille> familles, int numeroFamille)
        {
            if (numeroFamille <= 0)
            {
                return null;
            }

            return familles.FirstOrDefault(f => f.Numero == numeroFamille);
        }

        private static RdvFamille FusionnerFamilles(List<RdvFamille> familles, RdvFamille cible, RdvFamille? source)
        {
            if (source is null || ReferenceEquals(cible, source))
            {
                return cible;
            }

            foreach (var rdv in source.RDVs.ToList())
            {
                FamilyUtilities.AddAppointmentToFamily(cible, rdv);
            }

            FusionnerCollections(cible.Adresse, source.Adresse);
            FusionnerCollections(cible.Ville, source.Ville);
            FusionnerCollections(cible.Nom, source.Nom);
            FusionnerCollections(cible.Prenom, source.Prenom);
            FusionnerCollections(cible.Email, source.Email);
            FusionnerCollections(cible.TelList, source.TelList);
            FusionnerDates(cible.DatePriseRDV, source.DatePriseRDV);
            FusionnerDates(cible.HeureRDV, source.HeureRDV);

            if (cible.DateRDV == default && source.DateRDV != default)
            {
                cible.DateRDV = source.DateRDV;
            }

            cible.IsolatedFamily = cible.IsolatedFamily && source.IsolatedFamily;

            if (string.IsNullOrWhiteSpace(cible.Label) && !string.IsNullOrWhiteSpace(source.Label))
            {
                cible.Label = source.Label;
            }


            familles.Remove(source);

            return cible;
        }

        private static void FusionnerCollections(ICollection<string> cible, IEnumerable<string> source)
        {
            foreach (var valeur in source)
            {
                if (!string.IsNullOrWhiteSpace(valeur) && !cible.Contains(valeur, StringComparer.OrdinalIgnoreCase))
                {
                    cible.Add(valeur);
                }
            }
        }

        private static void FusionnerDates(ICollection<DateTime> cible, IEnumerable<DateTime> source)
        {
            foreach (var valeur in source)
            {
                if (!cible.Contains(valeur))
                {
                    cible.Add(valeur);
                }
            }
        }

        private static IEnumerable<RdvInfo> TrouverCandidats(
            string nomNettoye,
            IReadOnlyDictionary<string, List<RdvInfo>> rdvByFullName,
            IReadOnlyDictionary<string, List<RdvInfo>> rdvByLastName)
        {
            var cleComplete = NormalizeName(nomNettoye);
            if (!string.IsNullOrEmpty(cleComplete) && rdvByFullName.TryGetValue(cleComplete, out var correspondanceComplete))
            {
                return correspondanceComplete;
            }

            var result = new List<RdvInfo>();

            if (!string.IsNullOrEmpty(cleComplete) && rdvByLastName.TryGetValue(cleComplete, out var correspondanceNomComplet))
            {
                result.AddRange(correspondanceNomComplet);
            }

            var mots = nomNettoye.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var clesNoms = mots
                .Select(NormalizeName)
                .Where(cle => !string.IsNullOrEmpty(cle))
                .Distinct(StringComparer.Ordinal);

            foreach (var cleNom in clesNoms)
            {
                if (rdvByLastName.TryGetValue(cleNom, out var candidatsParNom))
                {
                    result.AddRange(candidatsParNom);
                }
            }

            if (result.Count == 0)
            {
                foreach (var cleNom in clesNoms)
                {
                    var correspondancesFuzzy = rdvByLastName
                        .Where(pair => LevenshteinDistance.Compute(pair.Key, cleNom) <= 1)
                        .SelectMany(pair => pair.Value);

                    result.AddRange(correspondancesFuzzy);
                }
            }

            return result
                .GroupBy(c => c.Id)
                .Select(g => g.First());
        }

        private static double CalculerScore(RdvInfo source, RdvInfo cible)
        {
            var diffHeure = Math.Abs((cible.HeureRDV - source.HeureRDV).TotalMinutes);
            var diffPrise = Math.Abs((cible.DatePriseRDV - source.DatePriseRDV).TotalMinutes);
            return diffHeure + (diffPrise / 10.0);
        }

        private static void AppliquerMetadonnees(RdvInfo rdv, int numeroFamille)
        {
            if (string.IsNullOrEmpty(rdv.TriedMethodRound3))
            {
                rdv.TriedMethodRound3 = "Remarque";
                rdv.FamilyMethodRound3 = numeroFamille;
            }
            else if (string.IsNullOrEmpty(rdv.TriedMethodRound4))
            {
                rdv.TriedMethodRound4 = "Remarque";
                rdv.FamilyMethodRound4 = numeroFamille;
            }
            else if (string.IsNullOrEmpty(rdv.TriedMethodRound5))
            {
                rdv.TriedMethodRound5 = "Remarque";
                rdv.FamilyMethodRound5 = numeroFamille;
            }

            if (rdv.NumeroFamille == 0)
            {
                rdv.NumeroFamille = numeroFamille;
            }
        }

        private static string NettoyerNom(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom))
            {
                return string.Empty;
            }

            var tronque = Regex
                .Split(nom, @"[;:,\./'""()\[\]\r\n]")
                .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

            if (tronque is null)
            {
                return string.Empty;
            }

            tronque = TitlesPattern.Replace(tronque, " ");

            var sansEt = Regex.Split(tronque, @"(?i)\bet\b").FirstOrDefault();
            if (sansEt is null)
            {
                return string.Empty;
            }

            var nettoye = Regex.Replace(sansEt, @"[^A-Za-zÀ-ÖØ-öø-ÿ'\- ]", " ");
            nettoye = Regex.Replace(nettoye, @"\s+", " ");
            return nettoye.Trim();
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var texte = TitlesPattern.Replace(value, " ");
            texte = texte.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(texte))
            {
                return string.Empty;
            }

            var normalized = texte.Normalize(NormalizationForm.FormD);
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
                    builder.Append(char.ToUpperInvariant(ch));
                }
                else if (ch == ' ' || ch == '-' || ch == (char)0x27)
                {
                    builder.Append(' ');
                }
            }

            var result = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
            return result;
        }
    }
}

