using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EyeChat.Families
{
    public static class FamilyScanReport
    {
        /// <summary>
        /// Rapport sans aucune donnée nominative : ni nom, ni numéro de patient, ni NoSS, ni téléphone, ni mail.
        /// Il peut être transmis pour régler les seuils.
        /// </summary>
        public static string Anonymous(ScanResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Reconstitution des familles — rapport anonymisé");
            sb.AppendLine($"Historique : {r.FirstDay:dd/MM/yyyy} → {r.LastDay:dd/MM/yyyy}, {r.Rows:N0} rendez-vous, {r.Patients:N0} patients.");
            var reference = r.LastDay is { } last && last < DateTime.Today ? last : DateTime.Today;
            var active = r.PatientsById.Values.Count(p => FamilyScanner.SeenRecently(p, reference));
            sb.AppendLine($"Patients actifs (venus depuis moins de 5 ans, ou RDV à venir) : {active:N0}. Seuls leurs téléphones, mails et adresses comptent comme « actuels ».");
            sb.AppendLine(r.Vitale == null
                ? "Carte Vitale : NON LUE (aucun export *vitale*.csv à côté de FamilyScan.exe, ou option --vitale absente)."
                : $"Carte Vitale : lue ({r.Vitale.VitalePatients:N0} patients).");
            sb.AppendLine();
            sb.AppendLine("Remplissage des colonnes par année (en % des rendez-vous) :");
            sb.AppendLine("Année    RDV   NoSS  Création  Arrivée  Heure  Remarque  Passage");
            foreach (var y in r.Years)
                sb.AppendLine($"{y.Year}  {y.Rows,6}  {Pct(y.WithNoSS, y.Rows),4}  {Pct(y.WithCreated, y.Rows),8}  {Pct(y.WithArrival, y.Rows),7}  {Pct(y.WithTime, y.Rows),5}  {Pct(y.WithRemark, y.Rows),8}  {Pct(y.WithPassage, y.Rows),7}");
            sb.AppendLine();

            sb.AppendLine("Liens entre patients :");
            foreach (var s in new[] { LinkStrength.Forte, LinkStrength.Moyenne, LinkStrength.Faible })
            {
                var group = r.Links.Where(l => l.Strength == s).ToList();
                sb.AppendLine($"  {s,-8} {group.Count,7:N0}   dont même foyer aujourd'hui : {group.Count(l => l.SameHouseholdNow),7:N0}");
            }
            sb.AppendLine();
            sb.AppendLine("Indices rencontrés (un lien peut en cumuler plusieurs) :");
            void Line(string label, Func<PatientLink, bool> test) => sb.AppendLine($"  {label,-45} {r.Links.Count(test),7:N0}");
            Line("même NoSS (agenda, jusqu'en 2005)", l => l.SharedNoSS);
            Line("RDV pris à la même minute", l => l.BookedTogetherDays > 0);
            Line("arrivés ensemble", l => l.ArrivedTogetherDays > 0);
            Line("passés ensemble en consultation", l => l.PassedTogetherDays > 0);
            Line("remarque citant l'autre", l => l.RemarkNamesDays > 0);
            Line("remarque familiale (venus ensemble)", l => l.RemarkFamilyWordDays > 0);
            Line("venus ensemble au moins 2 fois", l => l.CovisitDays >= 2);
            Line("venus ensemble une seule fois", l => l.CovisitDays == 1);
            Line("même téléphone ou mail actuel", l => l.SharedContactNow);
            Line("téléphone ou mail commun seulement par le passé", l => !l.SharedContactNow && (l.SharedPhoneEver || l.SharedEmailEver));
            Line("NoSS commun, pas même foyer aujourd'hui", l => l.SharedNoSS && !l.SameHouseholdNow);
            sb.AppendLine($"  Téléphones collectifs ignorés (plus de {FamilyScanner.CollectiveThreshold} patients) : {r.CollectivePhones:N0} ; mails collectifs : {r.CollectiveEmails:N0}");
            sb.AppendLine();

            if (r.Vitale is { } v)
            {
                sb.AppendLine("Carte Vitale (logiciel Vitale) :");
                sb.AppendLine($"  {v.VitalePatients:N0} patients Vitale : {v.Matched:N0} rapprochés d'un Code Patient, {v.Ambiguous:N0} ambigus, {v.NotFound:N0} non trouvés, {v.WithoutBirthDate:N0} sans date de naissance.");
                sb.AppendLine($"  Dont rapprochés grâce à la dernière consultation (homonymes, nom ou prénom écrit autrement) : {v.MatchedByConsultation:N0}.");
                var vit = r.Links.Where(l => l.VitaleRelation != null).ToList();
                sb.AppendLine($"  {r.DeclaredLinks:N0} liens déclarés entre patients de l'agenda ({r.DeclaredLinksWithoutAgenda:N0} ignorés : patients sans RDV).");
                foreach (var g in vit.GroupBy(l => l.VitaleRelation).OrderByDescending(g => g.Count()))
                    sb.AppendLine($"    {g.Key,-38} {g.Count(),7:N0}   dont en cours : {g.Count(l => l.VitaleCurrent),7:N0}");
                sb.AppendLine($"  Rattachements en cours d'enfants de 20 ans ou plus (foyer décidé par l'agenda) : {vit.Count(l => l.VitaleCurrent && !l.VitaleSeparated && !l.VitaleHousehold):N0}, dont même foyer : {vit.Count(l => l.VitaleCurrent && !l.VitaleSeparated && !l.VitaleHousehold && l.SameHouseholdNow):N0}.");
                var current = vit.Where(l => l.VitaleCurrent && !l.VitaleSeparated && l.VitaleHousehold).ToList();
                sb.AppendLine($"  Rattachements en cours valant « même foyer » (conjoints, enfants de moins de 20 ans) : {current.Count:N0}, dont déjà trouvés par l'agenda seul : {current.Count(l => l.AgendaHouseholdNow):N0}, nouveaux grâce à Vitale : {current.Count(l => !l.AgendaHouseholdNow):N0}.");
                sb.AppendLine($"  Séparés, divorcés ou veufs : {vit.Count(l => l.VitaleSeparated):N0}, dont regroupés à tort par l'agenda seul : {vit.Count(l => l.VitaleSeparated && l.AgendaHouseholdNow):N0}.");
                sb.AppendLine();
            }

            if (r.Payments is { } pay)
            {
                sb.AppendLine("Règlements (qui paie pour qui) :");
                sb.AppendLine($"  {pay.Lines:N0} factures : {pay.Organisms:N0} réglées par un organisme, {pay.SelfPaid:N0} par le patient lui-même, {pay.PaidForSomeoneElse:N0} par une autre personne.");
                sb.AppendLine($"  Bénéficiaire retrouvé (nom, prénom, RDV le jour de la facture) : {pay.BeneficiaryFound:N0} ; payeur retrouvé parmi les patients : {pay.PayerFound:N0} ; payeurs collectifs ignorés (plus de {PaymentLinks.MaxBeneficiariesPerPayer} bénéficiaires) : {pay.CollectivePayers:N0}.");
                var billed = r.Links.Where(l => l.SameBillingCard > 0).ToList();
                sb.AppendLine($"  Carte Vitale à la facturation (NIR de l'assuré, jamais conservé) : {pay.CardInvoices:N0} factures rattachées à un patient, {pay.SharedCards:N0} cartes facturées pour plusieurs patients.");
                sb.AppendLine($"    {billed.Count:N0} liens « même carte à la facturation », dont récents (moins de 3 ans) : {billed.Count(l => l.BillingCardLast is { } d && (DateTime.Today - d).TotalDays <= 3 * 365):N0} ; même foyer : {billed.Count(l => l.SameHouseholdNow):N0}, dont nouveaux (ni agenda ni carte Vitale) : {billed.Count(l => l.SameHouseholdNow && !l.AgendaHouseholdNow && l.VitaleRelation == null):N0}.");
                var contradicted = r.Links.Where(l => l.BillingSeparateCards).ToList();
                sb.AppendLine($"    Rattachements Vitale « actifs » d'enfants contredits (chacun facturé sous sa propre carte récemment) : {contradicted.Count:N0}, dont plus regroupés d'office : {contradicted.Count(l => l.VitaleHousehold):N0}, dont restés même foyer grâce à l'agenda : {contradicted.Count(l => l.VitaleHousehold && l.SameHouseholdNow):N0}.");
                foreach (var g in contradicted.GroupBy(l => l.VitaleRelation).OrderByDescending(g => g.Count()))
                    sb.AppendLine($"      {g.Key,-36} {g.Count(),7:N0}");
                var paid = r.Links.Where(l => l.PaidForCount + l.SamePayerCount > 0).ToList();
                sb.AppendLine($"  Liens : {paid.Count(l => l.PaidForCount > 0):N0} « l'un règle pour l'autre », {paid.Count(l => l.SamePayerCount > 0):N0} « même payeur » ; même foyer : {paid.Count(l => l.SameHouseholdNow):N0}, dont nouveaux (ni agenda ni carte Vitale) : {paid.Count(l => l.SameHouseholdNow && !l.AgendaHouseholdNow && l.VitaleRelation == null):N0}.");
                sb.AppendLine();
            }

            sb.AppendLine("Familles « même foyer aujourd'hui » :");
            sb.AppendLine($"  Parentés entre foyers distincts (adulte qui a fondé son foyer : conjoint ou enfants mineurs) : {r.Links.Count(l => l.OtherHousehold):N0} liens séparés.");
            sb.AppendLine($"  {r.Families.Count:N0} familles, {r.Families.Sum(f => f.Members.Count):N0} patients regroupés.");
            foreach (var size in r.Families.GroupBy(f => Math.Min(f.Members.Count, 7)).OrderBy(g => g.Key))
                sb.AppendLine($"  {(size.Key >= 7 ? "7 personnes ou plus" : size.Key + " personnes"),-22} {size.Count(),6:N0}");
            sb.AppendLine($"  À vérifier : {r.Families.Count(f => f.ToReview):N0} (chaîne sans lien direct : {r.Families.Count(f => f.HasChain):N0}, plus de 6 personnes : {r.Families.Count(f => f.Members.Count > 6):N0}, lien faible : {r.Families.Count(f => f.Weakest < LinkStrength.Moyenne):N0})");
            sb.AppendLine();

            sb.AppendLine("Exemples de familles à vérifier (anonymes : taille et liens internes) :");
            var household = r.Links.Where(l => l.SameHouseholdNow).ToList();
            var byPatient = household.SelectMany(l => new[] { (Id: l.A, Link: l), (Id: l.B, Link: l) }).ToLookup(x => x.Id, x => x.Link);
            foreach (var family in r.Families.Where(f => f.ToReview).OrderByDescending(f => f.Members.Count).Take(5)
                .Concat(r.Families.Where(f => f.HasChain && f.Members.Count <= 4).Take(5)))
            {
                var members = family.Members.ToHashSet();
                var inside = family.Members.SelectMany(id => byPatient[id]).Distinct().Where(l => members.Contains(l.A) && members.Contains(l.B)).ToList();
                sb.AppendLine($"  {family.Members.Count} personnes, {inside.Count} liens directs sur {family.Members.Count * (family.Members.Count - 1) / 2} possibles :");
                foreach (var reason in inside.GroupBy(l => l.Reasons).OrderByDescending(g => g.Count()).Take(4))
                    sb.AppendLine($"      {reason.Count()} × {reason.Key}");
            }
            sb.AppendLine();

            sb.AppendLine("Exemples de liens à examiner (anonymes) :");
            var examples = r.Links.Where(l => l.Strength == LinkStrength.Moyenne).Take(10)
                .Concat(r.Links.Where(l => l.SharedNoSS && !l.SameHouseholdNow).Take(5))
                .Concat(r.Links.Where(l => l.Strength == LinkStrength.Faible).Take(5)).ToList();
            for (int i = 0; i < examples.Count; i++)
                sb.AppendLine($"  Lien {i + 1} — {examples[i].Strength}{(examples[i].SameHouseholdNow ? ", même foyer" : "")} : {examples[i].Reasons}");
            return sb.ToString();
        }

        /// <summary>Détail nominatif (à garder au cabinet) : une ligne par membre de famille, puis les liens à vérifier.</summary>
        public static string DetailCsv(ScanResult r)
        {
            static string Cell(string v) => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine("Famille;À vérifier;Code patient;Nom;Dernière venue;Téléphones actuels;Mails actuels;Liens dans la famille;Parenté dans un autre foyer");
            var familyOf = r.Families.SelectMany(f => f.Members.Select(m => (m, f.Number))).ToDictionary(x => x.m, x => x.Number);
            var kin = r.Links.Where(l => l.OtherHousehold).SelectMany(l => new[] { (Id: l.A, Other: l.B, Link: l), (Id: l.B, Other: l.A, Link: l) })
                .ToLookup(x => x.Id);
            var byPatient = r.Links.Where(l => l.SameHouseholdNow).SelectMany(l => new[] { (Id: l.A, Link: l), (Id: l.B, Link: l) })
                .ToLookup(x => x.Id, x => x.Link);
            foreach (var f in r.Families)
                foreach (var id in f.Members)
                {
                    var p = r.PatientsById[id];
                    var reasons = byPatient[id].Where(l => f.Members.Contains(l.A == id ? l.B : l.A))
                        .Select(l => $"avec {(l.A == id ? l.B : l.A)} : {l.Reasons}");
                    sb.AppendLine(string.Join(";", new[]
                    {
                        f.Number.ToString(CultureInfo.InvariantCulture), f.ToReview ? "oui" : "", id.ToString(CultureInfo.InvariantCulture),
                        p.Name, p.LastSeen == DateTime.MinValue ? "jamais venu" : p.LastSeen.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        string.Join(" ", p.Phones), string.Join(" ", p.Emails), string.Join(" | ", reasons),
                        string.Join(" | ", kin[id].Select(k => $"{k.Other}" + (familyOf.TryGetValue(k.Other, out var n) ? $" (famille {n})" : "") + $" : {k.Link.Reasons}"))
                    }.Select(Cell)));
                }
            return sb.ToString();
        }

        private static string Pct(int part, int total) => total == 0 ? "-" : (100.0 * part / total).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
