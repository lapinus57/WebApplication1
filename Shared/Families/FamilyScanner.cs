using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EyeChat.Families
{
    /// <summary>Une ligne de l'agenda (AG_rdv), déjà lue. Les champs absents de la base restent vides ou null.</summary>
    public sealed record ScanRow(
        int PatientId, DateTime Day, DateTime? Time, DateTime? Created, DateTime? Arrival,
        string NoSS, string Surname, string MaidenName, string FirstName, string Address, string City,
        IReadOnlyList<string> Phones, IReadOnlyList<string> Emails, bool Cancelled);

    public enum LinkStrength { Faible = 1, Moyenne = 2, Forte = 3 }

    /// <summary>Lien constaté entre deux patients sur tout l'historique (sans nommer la relation).</summary>
    public sealed class PatientLink
    {
        public int A { get; init; }
        public int B { get; init; }
        public int CovisitDays { get; set; }
        public int BookedTogetherDays { get; set; }
        public int ArrivedTogetherDays { get; set; }
        public DateTime? FirstCovisit { get; set; }
        public DateTime? LastCovisit { get; set; }
        public DateTime? NoSSFirst { get; set; }
        public DateTime? NoSSLast { get; set; }
        public bool SharedPhoneEver { get; set; }
        public bool SharedEmailEver { get; set; }
        public bool SharedContactNow { get; set; }
        public bool SameSurname { get; set; }
        public bool SameAddressNow { get; set; }
        public LinkStrength Strength { get; set; }
        public bool SameHouseholdNow { get; set; }
        /// <summary>Parenté entre deux foyers : l'un a fondé son propre foyer (conjoint, enfants mineurs) ailleurs.</summary>
        public bool OtherHousehold { get; set; }
        /// <summary>Pour un lien Vitale « parent et enfant » : l'enfant.</summary>
        public int? VitaleChild { get; set; }
        public bool SharedNoSS => NoSSFirst.HasValue;
        /// <summary>Lien déclaré par la carte Vitale (« parent et enfant », « conjoints »…), ou null.</summary>
        public string? VitaleRelation { get; set; }
        public bool VitaleCurrent { get; set; }
        public bool VitaleSeparated { get; set; }
        /// <summary>Le rattachement en cours suffit à dire « même foyer » (conjoints, enfants de moins de 20 ans).</summary>
        public bool VitaleHousehold { get; set; }
        /// <summary>Factures réglées par l'un pour l'autre (export des paiements).</summary>
        public int PaidForCount { get; set; }
        /// <summary>Factures des deux réglées par le même payeur (parent, conjoint, aidant).</summary>
        public int SamePayerCount { get; set; }
        /// <summary>Factures des deux sous le même NIR d'assuré (même carte Vitale lue à la consultation).</summary>
        public int SameBillingCard { get; set; }
        public DateTime? BillingCardFirst { get; set; }
        /// <summary>Dernière date où les deux étaient encore facturés sous cette carte.</summary>
        public DateTime? BillingCardLast { get; set; }
        /// <summary>Facturés récemment sous des cartes différentes : contredit un rattachement Vitale « actif » périmé.</summary>
        public bool BillingSeparateCards { get; set; }
        public DateTime? FirstPayment { get; set; }
        public DateTime? LastPayment { get; set; }
        /// <summary>Même foyer d'après l'agenda seul (avant la carte Vitale), pour comparer les deux sources.</summary>
        public bool AgendaHouseholdNow { get; set; }
        public string Reasons { get; set; } = string.Empty;
    }

    public sealed class PatientInfo
    {
        public int Id { get; init; }
        public string Name { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
        public HashSet<string> Phones { get; } = new();
        public HashSet<string> Emails { get; } = new();
        public string Address { get; set; } = string.Empty;
        public HashSet<string> SurnameTokens { get; } = new();
        public HashSet<string> FirstNameTokens { get; } = new();
    }

    public sealed class YearStats
    {
        public int Year { get; init; }
        public int Rows { get; set; }
        public int WithNoSS { get; set; }
        public int WithCreated { get; set; }
        public int WithArrival { get; set; }
        public int WithTime { get; set; }
    }

    public sealed class ScanFamily
    {
        public int Number { get; set; }
        public List<int> Members { get; } = new();
        public LinkStrength Weakest { get; set; } = LinkStrength.Forte;
        /// <summary>Membres reliés seulement par l'intermédiaire d'un autre (A–B, B–C, mais rien entre A et C).</summary>
        public bool HasChain { get; set; }
        public bool ToReview => HasChain || Members.Count > 6 || Weakest < LinkStrength.Moyenne;
    }

    public sealed class ScanResult
    {
        public DateTime? FirstDay { get; init; }
        public DateTime? LastDay { get; init; }
        public int Rows { get; init; }
        public int Patients { get; init; }
        public IReadOnlyList<YearStats> Years { get; init; } = Array.Empty<YearStats>();
        public IReadOnlyList<PatientLink> Links { get; init; } = Array.Empty<PatientLink>();
        public IReadOnlyList<ScanFamily> Families { get; init; } = Array.Empty<ScanFamily>();
        public IReadOnlyDictionary<int, PatientInfo> PatientsById { get; init; } = new Dictionary<int, PatientInfo>();
        public int CollectivePhones { get; init; }
        public int CollectiveEmails { get; init; }
        public VitaleMatch? Vitale { get; set; }
        public PaymentStats? Payments { get; init; }
        public int DeclaredLinks { get; init; }
        /// <summary>Liens Vitale entre patients absents de l'agenda lu (aucun RDV) : ignorés.</summary>
        public int DeclaredLinksWithoutAgenda { get; init; }
    }

    /// <summary>
    /// Reconstitue les familles à partir de tout l'historique de l'agenda, jour après jour (du plus ancien au plus récent).
    /// Indices : NoSS commun (années où les ayants droit partageaient le numéro de l'assuré), RDV pris à la même minute,
    /// arrivées ensemble, venues le même jour à des créneaux voisins, téléphone / mail communs, nom et adresse.
    /// Le NoSS n'est utilisé que pour comparer : il n'est jamais conservé dans le résultat.
    /// </summary>
    public sealed class FamilyScanner
    {
        public const int CovisitWindowMinutes = 45;
        public const int ArrivedTogetherMinutes = 10;
        /// <summary>Un téléphone ou un mail présent chez plus de patients que cela est collectif (EHPAD, foyer, taxi…).</summary>
        public const int CollectiveThreshold = 6;
        public static readonly TimeSpan RecentPeriod = TimeSpan.FromDays(3 * 365);
        /// <summary>
        /// Téléphone, mail et adresse ne sont « actuels » que si les deux patients sont venus depuis moins de 5 ans :
        /// sinon c'est la coordonnée d'un dossier ancien (patient parti, décédé), pas la preuve d'un foyer aujourd'hui.
        /// </summary>
        public static readonly TimeSpan CurrentContactPeriod = TimeSpan.FromDays(5 * 365);

        private readonly List<ScanRow> _day = new();
        private DateTime? _currentDay, _firstDay, _lastDay;
        private int _rows;
        private readonly Dictionary<int, YearStats> _years = new();
        private readonly Dictionary<(int, int), PatientLink> _links = new();
        private readonly Dictionary<string, Dictionary<int, (DateTime First, DateTime Last)>> _noss = new();
        private readonly Dictionary<string, HashSet<int>> _phoneOwners = new(), _emailOwners = new();
        private readonly Dictionary<int, PatientInfo> _patients = new();
        private readonly List<DeclaredLink> _declared = new();
        private readonly HashSet<(int, DateTime)> _visits = new();
        private readonly Dictionary<DateTime, List<int>> _visitorsByDay = new();
        private PaymentStats? _payments;
        /// <summary>Dernière carte de facturation de chaque patient, avec sa date.</summary>
        private readonly Dictionary<int, (string Card, DateTime Date)> _latestCard = new();

        /// <summary>Le patient avait-il un RDV (non annulé) ce jour-là ? Sert au rapprochement avec la carte Vitale.</summary>
        public bool Visited(int patientId, DateTime day) => _visits.Contains((patientId, day.Date));

        /// <summary>Liens déclarés par la carte Vitale (codes patients de l'agenda), pris en compte à la fin.</summary>
        public void AddDeclared(IEnumerable<DeclaredLink> links) => _declared.AddRange(links);

        /// <summary>Ajoute une ligne. Les lignes doivent arriver dans l'ordre chronologique (jour par jour).</summary>
        public void Add(ScanRow row)
        {
            if (row.PatientId <= 0 || row.Day == default) return;
            var day = row.Day.Date;
            if (_currentDay.HasValue && day < _currentDay.Value)
                throw new InvalidOperationException("Les rendez-vous doivent être lus dans l'ordre des dates.");
            if (_currentDay != day) { FlushDay(); _currentDay = day; }
            _day.Add(row);
            _rows++;
            if (!row.Cancelled && _visits.Add((row.PatientId, day)))
            {
                if (!_visitorsByDay.TryGetValue(day, out var visitors)) _visitorsByDay[day] = visitors = new List<int>();
                visitors.Add(row.PatientId);
            }
            _firstDay ??= day;
            _lastDay = day;

            if (!_years.TryGetValue(day.Year, out var stats)) _years[day.Year] = stats = new YearStats { Year = day.Year };
            stats.Rows++;
            var noss = NormalizeNoSS(row.NoSS);
            if (noss.Length > 0) stats.WithNoSS++;
            if (row.Created.HasValue) stats.WithCreated++;
            if (row.Arrival.HasValue) stats.WithArrival++;
            if (row.Time.HasValue) stats.WithTime++;

            if (noss.Length > 0)
            {
                if (!_noss.TryGetValue(noss, out var owners)) _noss[noss] = owners = new();
                owners[row.PatientId] = owners.TryGetValue(row.PatientId, out var seen) ? (seen.First, day) : (day, day);
            }

            // Coordonnées : on garde les plus récentes (la dernière ligne lue l'emporte), et on note tous les détenteurs.
            if (!_patients.TryGetValue(row.PatientId, out var patient)) _patients[row.PatientId] = patient = new PatientInfo { Id = row.PatientId };
            var phones = row.Phones.Select(NormalizePhone).Where(p => p.Length > 0).ToList();
            var emails = row.Emails.Select(NormalizeEmail).Where(e => e.Length > 0).ToList();
            foreach (var p in phones) Owners(_phoneOwners, p).Add(row.PatientId);
            foreach (var e in emails) Owners(_emailOwners, e).Add(row.PatientId);
            if (day >= patient.LastSeen)
            {
                patient.LastSeen = day;
                patient.Name = $"{row.FirstName} {row.Surname}".Trim();
                if (phones.Count > 0) { patient.Phones.Clear(); patient.Phones.UnionWith(phones); }
                if (emails.Count > 0) { patient.Emails.Clear(); patient.Emails.UnionWith(emails); }
                var address = NormalizeAddress(row.Address, row.City);
                if (address.Length > 0) patient.Address = address;
                var firstNames = FirstNameTokens(row.FirstName);
                if (firstNames.Count > 0) { patient.FirstNameTokens.Clear(); patient.FirstNameTokens.UnionWith(firstNames); }
                var tokens = SurnameTokens(row.Surname, row.MaidenName);
                if (tokens.Count > 0) { patient.SurnameTokens.Clear(); patient.SurnameTokens.UnionWith(tokens); }
            }
        }

        private static HashSet<int> Owners(Dictionary<string, HashSet<int>> map, string key)
            => map.TryGetValue(key, out var set) ? set : map[key] = new HashSet<int>();

        private PatientLink Link(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            return _links.TryGetValue(key, out var link) ? link : _links[key] = new PatientLink { A = key.Item1, B = key.Item2 };
        }

        // Une journée : paires de patients venus à des créneaux voisins, ou ayant pris leurs RDV à la même minute.
        private void FlushDay()
        {
            if (_day.Count == 0) return;
            var day = _currentDay!.Value;
            var visits = _day.Where(r => !r.Cancelled).GroupBy(r => r.PatientId)
                .Select(g => g.OrderBy(r => r.Time ?? DateTime.MaxValue).First()).ToList();
            _day.Clear();
            var timed = visits.Where(v => v.Time.HasValue).OrderBy(v => v.Time).ToList();
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < timed.Count; i++)
                for (int j = i + 1; j < timed.Count && (timed[j].Time!.Value - timed[i].Time!.Value).TotalMinutes <= CovisitWindowMinutes; j++)
                    pairs.Add(Key(timed[i].PatientId, timed[j].PatientId));
            foreach (var bucket in visits.Where(v => v.Created.HasValue).GroupBy(v => Minute(v.Created!.Value)).Where(g => g.Count() > 1))
            {
                var ids = bucket.Select(v => v.PatientId).ToList();
                for (int i = 0; i < ids.Count; i++) for (int j = i + 1; j < ids.Count; j++) pairs.Add(Key(ids[i], ids[j]));
            }
            var byId = visits.ToDictionary(v => v.PatientId);
            foreach (var (a, b) in pairs)
            {
                var va = byId[a]; var vb = byId[b];
                var booked = va.Created.HasValue && vb.Created.HasValue && Minute(va.Created.Value) == Minute(vb.Created.Value);
                var arrived = va.Arrival.HasValue && vb.Arrival.HasValue
                    && Math.Abs((va.Arrival.Value.TimeOfDay - vb.Arrival.Value.TimeOfDay).TotalMinutes) <= ArrivedTogetherMinutes;
                var near = va.Time.HasValue && vb.Time.HasValue && Math.Abs((va.Time.Value - vb.Time.Value).TotalMinutes) <= CovisitWindowMinutes;
                // Deux inconnus peuvent avoir des créneaux voisins par hasard : il faut en plus un indice d'identité ou de réservation.
                // L'heure d'arrivée seule n'en est pas un : dans un cabinet chargé, beaucoup de patients arrivent
                // dans les mêmes minutes (mesuré : 660 000 paires « arrivées ensemble »). Elle ne fait que renforcer.
                var clue = booked || SameNoSS(va, vb) || Shares(va.Phones, vb.Phones, NormalizePhone) || Shares(va.Emails, vb.Emails, NormalizeEmail)
                    || SurnameTokens(va.Surname, va.MaidenName).Overlaps(SurnameTokens(vb.Surname, vb.MaidenName))
                    || (NormalizeAddress(va.Address, va.City) is { Length: > 0 } ad && ad == NormalizeAddress(vb.Address, vb.City));
                if (!clue || !(near || booked)) continue;
                var link = Link(a, b);
                link.CovisitDays++;
                if (booked) link.BookedTogetherDays++;
                if (arrived) link.ArrivedTogetherDays++;
                link.FirstCovisit ??= day;
                link.LastCovisit = day;
            }
        }

        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
        private static DateTime Minute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
        private static bool SameNoSS(ScanRow a, ScanRow b) => NormalizeNoSS(a.NoSS) is { Length: > 0 } n && n == NormalizeNoSS(b.NoSS);
        private static bool Shares(IEnumerable<string> a, IEnumerable<string> b, Func<string, string> normalize)
            => a.Select(normalize).Where(x => x.Length > 0).Intersect(b.Select(normalize).Where(x => x.Length > 0)).Any();

        /// <summary>
        /// Règlements « qui paie pour qui » : le bénéficiaire est retrouvé par nom + prénom + RDV le jour de la facture ;
        /// le payeur aussi s'il était là ce jour-là, sinon par nom + prénom s'il est le seul patient de ce nom.
        /// À appeler après la lecture de l'agenda, avant <see cref="Finish"/>.
        /// </summary>
        public PaymentStats AddPayments(IEnumerable<PaymentRow> rows)
        {
            FlushDay();
            var stats = _payments ??= new PaymentStats();
            var bySurname = new Dictionary<string, List<int>>();
            foreach (var p in _patients.Values)
                foreach (var t in p.SurnameTokens) { if (!bySurname.TryGetValue(t, out var l)) bySurname[t] = l = new(); l.Add(p.Id); }
            bool Matches(int id, string surname, string firstName)
                => _patients.TryGetValue(id, out var p) && p.SurnameTokens.Overlaps(SurnameTokens(surname, null))
                   && p.FirstNameTokens.Contains(PaymentLinks.FirstToken(firstName));
            int? Unique(IEnumerable<int> ids) { var list = ids.Distinct().Take(2).ToList(); return list.Count == 1 ? list[0] : null; }
            int? Visitor(DateTime day, string surname, string firstName)
                => _visitorsByDay.TryGetValue(day.Date, out var v) ? Unique(v.Where(id => Matches(id, surname, firstName))) : null;
            int? ByName(string surname, string firstName)
                => Unique(SurnameTokens(surname, null).SelectMany(t => bySurname.TryGetValue(t, out var l) ? l : new List<int>()).Where(id => Matches(id, surname, firstName)));

            var payers = new Dictionary<string, Dictionary<int, (DateTime First, DateTime Last, int Count)>>();
            var cards = new Dictionary<string, Dictionary<int, (DateTime First, DateTime Last, int Count)>>();
            foreach (var row in rows)
            {
                stats.Lines++;
                // Carte de facturation : toutes les factures comptent, quel que soit le payeur.
                if (row.Card.Length > 0 && row.InvoiceDate is { } billed && Visitor(billed, row.BeneficiarySurname, row.BeneficiaryFirstName) is { } patient)
                {
                    stats.CardInvoices++;
                    if (!cards.TryGetValue(row.Card, out var onCard)) cards[row.Card] = onCard = new();
                    onCard[patient] = onCard.TryGetValue(patient, out var c)
                        ? (c.First < billed.Date ? c.First : billed.Date, c.Last > billed.Date ? c.Last : billed.Date, c.Count + 1)
                        : (billed.Date, billed.Date, 1);
                    if (!_latestCard.TryGetValue(patient, out var latest) || billed.Date >= latest.Date) _latestCard[patient] = (row.Card, billed.Date);
                }
                if (!PaymentLinks.IsPersonPayer(row) || row.InvoiceDate is not { } date) { stats.Organisms++; continue; }
                if (PaymentLinks.SamePerson(row)) { stats.SelfPaid++; continue; }
                stats.PaidForSomeoneElse++;
                var beneficiary = Visitor(date, row.BeneficiarySurname, row.BeneficiaryFirstName);
                if (beneficiary is not { } b) continue;
                stats.BeneficiaryFound++;
                var payer = Visitor(date, row.PayerSurname, row.PayerFirstName) ?? ByName(row.PayerSurname, row.PayerFirstName);
                if (payer is { } p && p != b)
                {
                    stats.PayerFound++;
                    var link = Link(p, b);
                    link.PaidForCount++;
                    if (link.FirstPayment is null || date < link.FirstPayment) link.FirstPayment = date.Date;
                    if (link.LastPayment is null || date > link.LastPayment) link.LastPayment = date.Date;
                }
                var key = row.PayerId.Length > 0 ? "id:" + row.PayerId
                    : "nom:" + string.Join(" ", SurnameTokens(row.PayerSurname, null).OrderBy(x => x)) + "|" + PaymentLinks.FirstToken(row.PayerFirstName);
                if (!payers.TryGetValue(key, out var benefs)) payers[key] = benefs = new();
                benefs[b] = benefs.TryGetValue(b, out var seen)
                    ? (seen.First < date.Date ? seen.First : date.Date, seen.Last > date.Date ? seen.Last : date.Date, seen.Count + 1)
                    : (date.Date, date.Date, 1);
            }
            foreach (var onCard in cards.Values.Where(x => x.Count >= 2))
            {
                if (onCard.Count > VitaleLinks.MaxPatientsPerCard) continue;
                stats.SharedCards++;
                var ids = onCard.Keys.ToList();
                for (int i = 0; i < ids.Count; i++)
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        var a = onCard[ids[i]]; var c = onCard[ids[j]];
                        var link = Link(ids[i], ids[j]);
                        link.SameBillingCard += Math.Min(a.Count, c.Count);
                        var first = a.First < c.First ? a.First : c.First;
                        var last = a.Last < c.Last ? a.Last : c.Last; // les deux étaient encore sur la carte à cette date
                        if (link.BillingCardFirst is null || first < link.BillingCardFirst) link.BillingCardFirst = first;
                        if (link.BillingCardLast is null || last > link.BillingCardLast) link.BillingCardLast = last;
                    }
            }
            foreach (var benefs in payers.Values.Where(x => x.Count >= 2))
            {
                if (benefs.Count > PaymentLinks.MaxBeneficiariesPerPayer) { stats.CollectivePayers++; continue; }
                var ids = benefs.Keys.ToList();
                for (int i = 0; i < ids.Count; i++)
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        var a = benefs[ids[i]]; var c = benefs[ids[j]];
                        var link = Link(ids[i], ids[j]);
                        link.SamePayerCount += Math.Min(a.Count, c.Count);
                        var first = a.First > c.First ? a.First : c.First;
                        var last = a.Last < c.Last ? a.Last : c.Last;
                        if (link.FirstPayment is null || first < link.FirstPayment) link.FirstPayment = first;
                        if (link.LastPayment is null || last > link.LastPayment) link.LastPayment = last;
                    }
            }
            return stats;
        }

        /// <summary>Date de référence pour « récent » : aujourd'hui (l'agenda contient des RDV futurs).</summary>
        public DateTime Today { get; set; } = DateTime.Today;

        public ScanResult Finish()
        {
            FlushDay();
            var end = _lastDay is { } lastDay && lastDay < Today ? lastDay : Today;

            // NoSS commun : lien, avec la période où il a été constaté. Le numéro lui-même n'est pas conservé.
            foreach (var owners in _noss.Values.Where(o => o.Count is > 1 and <= CollectiveThreshold))
            {
                var ids = owners.Keys.ToList();
                for (int i = 0; i < ids.Count; i++)
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        var link = Link(ids[i], ids[j]);
                        var first = owners[ids[i]].First > owners[ids[j]].First ? owners[ids[i]].First : owners[ids[j]].First;
                        var last = owners[ids[i]].Last < owners[ids[j]].Last ? owners[ids[i]].Last : owners[ids[j]].Last;
                        if (last < first) (first, last) = (last, first);
                        link.NoSSFirst = link.NoSSFirst is { } f && f < first ? f : first;
                        link.NoSSLast = link.NoSSLast is { } l && l > last ? l : last;
                    }
            }
            _noss.Clear();

            int collectivePhones = 0, collectiveEmails = 0;
            void SharedContacts(Dictionary<string, HashSet<int>> owners, bool phone, ref int collective)
            {
                foreach (var (value, ids) in owners)
                {
                    if (ids.Count < 2) continue;
                    if (ids.Count > CollectiveThreshold) { collective++; continue; }
                    var list = ids.ToList();
                    for (int i = 0; i < list.Count; i++)
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            var link = Link(list[i], list[j]);
                            if (phone) link.SharedPhoneEver = true; else link.SharedEmailEver = true;
                            var pa = _patients[list[i]]; var pb = _patients[list[j]];
                            if ((phone ? pa.Phones : pa.Emails).Contains(value) && (phone ? pb.Phones : pb.Emails).Contains(value)
                                && SeenRecently(pa, end) && SeenRecently(pb, end)) link.SharedContactNow = true;
                        }
                }
            }
            SharedContacts(_phoneOwners, true, ref collectivePhones);
            SharedContacts(_emailOwners, false, ref collectiveEmails);

            int declared = 0, withoutAgenda = 0;
            foreach (var d in _declared)
            {
                if (!_patients.ContainsKey(d.A) || !_patients.ContainsKey(d.B)) { withoutAgenda++; continue; }
                var link = Link(d.A, d.B);
                link.VitaleRelation = d.Relation;
                link.VitaleCurrent = d.Current;
                link.VitaleSeparated = d.Separated;
                link.VitaleHousehold = d.Household;
                link.VitaleChild = d.Child;
                // Facturés récemment chacun sous sa propre carte : le rattachement Vitale « actif » d'un enfant est périmé
                // (carte à lui, ou carte de l'autre parent). Pour des conjoints, chacun sa carte est normal : rien n'est contredit.
                if (d.Current && !d.Relation.StartsWith("conjoints") && _latestCard.TryGetValue(d.A, out var ca) && _latestCard.TryGetValue(d.B, out var cb) && ca.Card != cb.Card
                    && end - ca.Date <= RecentPeriod && end - cb.Date <= RecentPeriod)
                    link.BillingSeparateCards = true;
                declared++;
            }

            foreach (var link in _links.Values)
            {
                var pa = _patients[link.A]; var pb = _patients[link.B];
                link.SameSurname = pa.SurnameTokens.Overlaps(pb.SurnameTokens);
                link.SameAddressNow = pa.Address.Length > 0 && pa.Address == pb.Address && SeenRecently(pa, end) && SeenRecently(pb, end);
                Classify(link, end);
            }
            SplitFoundedHouseholds(_links.Values.Where(l => l.SameHouseholdNow).ToList());
            var links = _links.Values.Where(l => l.Strength > 0).OrderByDescending(l => l.Strength).ThenBy(l => l.A).ThenBy(l => l.B).ToList();
            return new ScanResult
            {
                FirstDay = _firstDay, LastDay = _lastDay, Rows = _rows, Patients = _patients.Count,
                Years = _years.Values.OrderBy(y => y.Year).ToList(), Links = links, Families = BuildFamilies(links),
                PatientsById = _patients, CollectivePhones = collectivePhones, CollectiveEmails = collectiveEmails,
                DeclaredLinks = declared, DeclaredLinksWithoutAgenda = withoutAgenda, Payments = _payments
            };
        }

        /// <summary>
        /// Un adulte qui a fondé son foyer (conjoint, ou enfants mineurs, d'après la carte Vitale) n'est plus dans celui de
        /// ses parents ou de ses frères et sœurs : le lien qui ne passe que par lui devient une parenté entre deux foyers,
        /// sauf s'ils habitent à la même adresse aujourd'hui.
        /// </summary>
        private static void SplitFoundedHouseholds(List<PatientLink> household)
        {
            var nucleus = new Dictionary<int, HashSet<int>>();
            void Add(int founder, int member)
            {
                if (!nucleus.TryGetValue(founder, out var set)) nucleus[founder] = set = new HashSet<int> { founder };
                set.Add(member);
            }
            foreach (var l in household)
            {
                if (l.VitaleRelation == "conjoints" && l.VitaleCurrent) { Add(l.A, l.B); Add(l.B, l.A); }
                else if (l.VitaleRelation == "parent et enfant" && l.VitaleCurrent && l.VitaleHousehold && l.VitaleChild is { } child)
                    Add(child == l.A ? l.B : l.A, child);
            }
            if (nucleus.Count == 0) return;
            var neighbours = household.SelectMany(l => new[] { (l.A, l.B), (l.B, l.A) }).ToLookup(x => x.Item1, x => x.Item2);
            var cut = new List<PatientLink>();
            foreach (var l in household.Where(l => !l.SameAddressNow))
                foreach (var (founder, other) in new[] { (l.A, l.B), (l.B, l.A) })
                {
                    if (!nucleus.TryGetValue(founder, out var own) || own.Contains(other)) continue;
                    // L'autre n'est relié à personne du foyer fondé, sauf au fondateur…
                    if (neighbours[other].Any(n => n != founder && own.Contains(n))) continue;
                    // …et il a son propre entourage (parent, frère ou sœur) : deux foyers reliés par une seule personne.
                    if (!neighbours[other].Any(n => n != founder && !own.Contains(n))) continue;
                    cut.Add(l);
                    break;
                }
            foreach (var l in cut)
            {
                l.SameHouseholdNow = false;
                l.OtherHousehold = true;
                l.Reasons += ", a fondé son propre foyer";
            }
        }

        private static bool SeenRecently(PatientInfo p, DateTime end) => end - p.LastSeen <= CurrentContactPeriod;

        /// <summary>Force du lien et « même foyer aujourd'hui ». Règles volontairement simples et expliquées.</summary>
        public static void Classify(PatientLink l, DateTime end)
        {
            var contactEver = l.SharedPhoneEver || l.SharedEmailEver;
            // RDV pris à la même minute : fort s'il se répète, ou s'il s'ajoute à un nom, une adresse ou un contact commun.
            // Une seule fois sans rien d'autre peut venir d'un report de RDV en série (mesuré : familles de 21 « personnes »).
            var identity = l.SameSurname || l.SameAddressNow || contactEver;
            if (l.SharedNoSS || l.BookedTogetherDays >= 2 || (l.BookedTogetherDays >= 1 && l.CovisitDays >= 1 && identity)
                || (l.ArrivedTogetherDays >= 2 && l.CovisitDays >= 2)
                || (l.CovisitDays >= 2 && contactEver) || (l.SharedContactNow && (l.SameSurname || l.SameAddressNow)))
                l.Strength = LinkStrength.Forte;
            else if (l.CovisitDays >= 2 || (l.CovisitDays >= 1 && contactEver) || (contactEver && l.SameSurname))
                l.Strength = LinkStrength.Moyenne;
            else if (l.CovisitDays >= 1 || contactEver)
                l.Strength = LinkStrength.Faible;
            else
                l.Strength = 0;

            var recent = l.LastCovisit is { } last && end - last <= RecentPeriod;
            // Un téléphone ou mail actuel commun, sans même adresse ni aucune venue commune, est souvent celui d'un
            // grand-parent noté sur les fiches de plusieurs foyers (mesuré : groupes de 13 à 17 personnes). Il lie, mais
            // ne fait pas un foyer ; les rappels du jour regroupent de toute façon les personnes d'un même contact.
            var contactHousehold = l.SharedContactNow && (l.SameAddressNow || l.CovisitDays > 0);
            // Confidentialité : regrouper deux personnes, c'est annoncer le RDV de l'une au contact de l'autre.
            // Sans aucun indice d'identité commun (nom, adresse, contact, NoSS, carte Vitale), des personnes qui viennent
            // et réservent ensemble (résidents d'un EHPAD, voisins, amis) restent liées mais ne forment pas un foyer.
            l.AgendaHouseholdNow = (identity || l.SharedNoSS) && l.Strength >= LinkStrength.Moyenne && (contactHousehold || recent);

            // Règlements : payer pour quelqu'un (parent, conjoint, aidant) est un lien déclaré par un acte réel.
            var payments = l.PaidForCount + l.SamePayerCount;
            if (payments > 0)
            {
                var strong = l.PaidForCount >= 2 || l.SamePayerCount >= 2 || identity;
                if (strong) l.Strength = LinkStrength.Forte;
                else if (l.Strength < LinkStrength.Moyenne) l.Strength = LinkStrength.Moyenne;
            }
            var recentPayment = l.LastPayment is { } paid && end - paid <= RecentPeriod;
            // Même carte Vitale à la facturation (lue à chaque consultation) : lien déclaré et daté.
            var recentCard = l.SameBillingCard > 0 && l.BillingCardLast is { } cardDate && end - cardDate <= RecentPeriod;
            if (l.SameBillingCard > 0) l.Strength = LinkStrength.Forte;
            var anyIdentity = identity || l.SharedNoSS || l.VitaleRelation != null || payments > 0 || l.SameBillingCard > 0;
            l.SameHouseholdNow = anyIdentity && l.Strength >= LinkStrength.Moyenne && (contactHousehold || recent || recentPayment || recentCard);

            // Carte Vitale : lien déclaré, donc fort. Rattachement en cours de conjoints ou d'enfants de moins de 20 ans
            // = même foyer ; conjoints séparés, divorcés ou veufs = jamais regroupés. Sinon (ancien rattachement, enfant
            // adulte encore « actif » sur la carte) : le foyer dépend des autres indices.
            if (l.VitaleRelation != null)
            {
                l.Strength = LinkStrength.Forte;
                if (l.VitaleSeparated) l.SameHouseholdNow = false;
                else if (l.VitaleCurrent && l.VitaleHousehold && !l.BillingSeparateCards) l.SameHouseholdNow = true;
                else l.SameHouseholdNow = contactHousehold || recent || recentPayment || recentCard;
            }

            var reasons = new List<string>();
            if (l.VitaleRelation != null)
                reasons.Add($"carte Vitale : {l.VitaleRelation}{(!l.VitaleCurrent ? " (ancien rattachement)" : !l.VitaleHousehold && !l.VitaleSeparated && l.VitaleRelation != "conjoints" ? " (enfant de 20 ans ou plus)" : "")}");
            if (l.SharedNoSS) reasons.Add($"même numéro d'assuré {l.NoSSFirst:yyyy}" + (l.NoSSLast?.Year != l.NoSSFirst?.Year ? $"-{l.NoSSLast:yyyy}" : ""));
            if (l.CovisitDays > 0) reasons.Add($"venus ensemble {l.CovisitDays} fois ({l.FirstCovisit:yyyy}" + (l.LastCovisit?.Year != l.FirstCovisit?.Year ? $"-{l.LastCovisit:yyyy}" : "") + ")");
            if (l.BookedTogetherDays > 0) reasons.Add($"RDV pris à la même minute {l.BookedTogetherDays} fois");
            if (l.ArrivedTogetherDays > 0) reasons.Add($"arrivés ensemble {l.ArrivedTogetherDays} fois");
            if (l.SameBillingCard > 0) reasons.Add($"facturés sous la même carte Vitale {l.BillingCardFirst:yyyy}" + (l.BillingCardLast?.Year != l.BillingCardFirst?.Year ? $"-{l.BillingCardLast:yyyy}" : ""));
            if (l.BillingSeparateCards) reasons.Add("chacun facturé sous sa propre carte récemment");
            if (l.PaidForCount > 0) reasons.Add($"l'un règle les soins de l'autre ({l.PaidForCount} factures)");
            if (l.SamePayerCount > 0) reasons.Add($"soins réglés par le même payeur ({l.SamePayerCount} fois)");
            if (payments > 0) reasons[^1] += $" {l.FirstPayment:yyyy}" + (l.LastPayment?.Year != l.FirstPayment?.Year ? $"-{l.LastPayment:yyyy}" : "");
            if (l.SharedContactNow) reasons.Add("même téléphone ou mail actuel");
            else if (contactEver) reasons.Add("téléphone ou mail commun par le passé");
            if (l.SameSurname) reasons.Add("même nom");
            if (l.SameAddressNow) reasons.Add("même adresse");
            l.Reasons = string.Join(", ", reasons);
        }

        private static List<ScanFamily> BuildFamilies(IReadOnlyList<PatientLink> links)
        {
            var household = links.Where(l => l.SameHouseholdNow).ToList();
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            foreach (var l in household)
            {
                parent.TryAdd(l.A, l.A); parent.TryAdd(l.B, l.B);
                var ra = Find(l.A); var rb = Find(l.B);
                if (ra != rb) parent[ra] = rb;
            }
            var families = new List<ScanFamily>();
            foreach (var group in parent.Keys.GroupBy(Find))
            {
                var family = new ScanFamily();
                family.Members.AddRange(group.OrderBy(x => x));
                var internalLinks = household.Where(l => group.Contains(l.A) && group.Contains(l.B)).ToList();
                family.Weakest = internalLinks.Min(l => l.Strength);
                // Chaîne : deux membres à trois liens ou plus l'un de l'autre (A–B, B–C, C–D, rien entre A et D).
                // Un parent relié à chacun de ses enfants (distance 2) est une famille normale.
                family.HasChain = Diameter(family.Members, internalLinks) >= 3;
                families.Add(family);
            }
            families = families.OrderByDescending(f => f.Members.Count).ThenBy(f => f.Members[0]).ToList();
            for (int i = 0; i < families.Count; i++) families[i].Number = i + 1;
            return families;
        }

        private static int Diameter(IReadOnlyList<int> members, IReadOnlyList<PatientLink> links)
        {
            if (members.Count < 4) return members.Count - 1;
            var neighbours = members.ToDictionary(m => m, _ => new List<int>());
            foreach (var l in links) { neighbours[l.A].Add(l.B); neighbours[l.B].Add(l.A); }
            int diameter = 0;
            foreach (var start in members)
            {
                var distance = new Dictionary<int, int> { [start] = 0 };
                var queue = new Queue<int>(new[] { start });
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var next in neighbours[current].Where(n => !distance.ContainsKey(n)))
                    {
                        distance[next] = distance[current] + 1;
                        queue.Enqueue(next);
                    }
                }
                diameter = Math.Max(diameter, distance.Values.Max());
            }
            return diameter;
        }

        // ---------- Normalisations ----------

        /// <summary>NoSS : 13 chiffres (clé retirée) ; vide s'il est incomplet ou manifestement factice.</summary>
        public static string NormalizeNoSS(string? value)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (digits.Length < 13) return string.Empty;
            digits = digits[..13];
            return digits.Distinct().Count() <= 2 ? string.Empty : digits;
        }

        public static string NormalizePhone(string? value)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0033")) digits = "0" + digits[4..];
            else if (digits.StartsWith("33") && digits.Length == 11) digits = "0" + digits[2..];
            if (digits.Length < 9 || digits.Distinct().Count() <= 2) return string.Empty;
            return digits;
        }

        public static string NormalizeEmail(string? value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            var at = text.IndexOf('@');
            if (at <= 0) return string.Empty;
            // « conjointe : x@y.fr » → x@y.fr
            var start = text.LastIndexOfAny(new[] { ' ', ':', ';', ',', '<', '(' }, at) + 1;
            var end = text.IndexOfAny(new[] { ' ', ';', ',', '>', ')' }, at);
            var email = text[start..(end < 0 ? text.Length : end)];
            return email.Contains('.') ? email : string.Empty;
        }

        public static string NormalizeAddress(string? address, string? city)
        {
            var a = Simplify(address); var c = Simplify(city);
            return a.Length == 0 || c.Length == 0 ? string.Empty : a + "|" + c;
        }

        /// <summary>Prénoms : tous les mots, même courts (« Jo », « Lou »), contrairement aux noms.</summary>
        public static HashSet<string> FirstNameTokens(string? firstNames)
            => Simplify(firstNames).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        public static HashSet<string> SurnameTokens(string? surname, string? maidenName)
            => Simplify(surname + " " + maidenName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3).ToHashSet();

        private static string Simplify(string? value)
        {
            var text = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
            var chars = text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ').ToArray();
            return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
