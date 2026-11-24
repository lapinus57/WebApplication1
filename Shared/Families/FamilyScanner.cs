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
        public bool SharedNoSS => NoSSFirst.HasValue;
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

        private readonly List<ScanRow> _day = new();
        private DateTime? _currentDay, _firstDay, _lastDay;
        private int _rows;
        private readonly Dictionary<int, YearStats> _years = new();
        private readonly Dictionary<(int, int), PatientLink> _links = new();
        private readonly Dictionary<string, Dictionary<int, (DateTime First, DateTime Last)>> _noss = new();
        private readonly Dictionary<string, HashSet<int>> _phoneOwners = new(), _emailOwners = new();
        private readonly Dictionary<int, PatientInfo> _patients = new();

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
                            if ((phone ? pa.Phones : pa.Emails).Contains(value) && (phone ? pb.Phones : pb.Emails).Contains(value)) link.SharedContactNow = true;
                        }
                }
            }
            SharedContacts(_phoneOwners, true, ref collectivePhones);
            SharedContacts(_emailOwners, false, ref collectiveEmails);

            foreach (var link in _links.Values)
            {
                var pa = _patients[link.A]; var pb = _patients[link.B];
                link.SameSurname = pa.SurnameTokens.Overlaps(pb.SurnameTokens);
                link.SameAddressNow = pa.Address.Length > 0 && pa.Address == pb.Address;
                Classify(link, end);
            }
            var links = _links.Values.Where(l => l.Strength > 0).OrderByDescending(l => l.Strength).ThenBy(l => l.A).ThenBy(l => l.B).ToList();
            return new ScanResult
            {
                FirstDay = _firstDay, LastDay = _lastDay, Rows = _rows, Patients = _patients.Count,
                Years = _years.Values.OrderBy(y => y.Year).ToList(), Links = links, Families = BuildFamilies(links),
                PatientsById = _patients, CollectivePhones = collectivePhones, CollectiveEmails = collectiveEmails
            };
        }

        /// <summary>Force du lien et « même foyer aujourd'hui ». Règles volontairement simples et expliquées.</summary>
        public static void Classify(PatientLink l, DateTime end)
        {
            var contactEver = l.SharedPhoneEver || l.SharedEmailEver;
            if (l.SharedNoSS || (l.BookedTogetherDays >= 1 && l.CovisitDays >= 1) || (l.ArrivedTogetherDays >= 2 && l.CovisitDays >= 2)
                || (l.CovisitDays >= 2 && contactEver) || (l.SharedContactNow && (l.SameSurname || l.SameAddressNow)))
                l.Strength = LinkStrength.Forte;
            else if (l.CovisitDays >= 2 || (l.CovisitDays >= 1 && contactEver) || (contactEver && l.SameSurname))
                l.Strength = LinkStrength.Moyenne;
            else if (l.CovisitDays >= 1 || contactEver)
                l.Strength = LinkStrength.Faible;
            else
                l.Strength = 0;

            var recent = l.LastCovisit is { } last && end - last <= RecentPeriod;
            l.SameHouseholdNow = l.Strength >= LinkStrength.Moyenne && (l.SharedContactNow || recent);

            var reasons = new List<string>();
            if (l.SharedNoSS) reasons.Add($"même numéro d'assuré {l.NoSSFirst:yyyy}" + (l.NoSSLast?.Year != l.NoSSFirst?.Year ? $"-{l.NoSSLast:yyyy}" : ""));
            if (l.CovisitDays > 0) reasons.Add($"venus ensemble {l.CovisitDays} fois ({l.FirstCovisit:yyyy}" + (l.LastCovisit?.Year != l.FirstCovisit?.Year ? $"-{l.LastCovisit:yyyy}" : "") + ")");
            if (l.BookedTogetherDays > 0) reasons.Add($"RDV pris à la même minute {l.BookedTogetherDays} fois");
            if (l.ArrivedTogetherDays > 0) reasons.Add($"arrivés ensemble {l.ArrivedTogetherDays} fois");
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
                var n = family.Members.Count;
                family.HasChain = n >= 3 && internalLinks.Count < n * (n - 1) / 2;
                families.Add(family);
            }
            families = families.OrderByDescending(f => f.Members.Count).ThenBy(f => f.Members[0]).ToList();
            for (int i = 0; i < families.Count; i++) families[i].Number = i + 1;
            return families;
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
