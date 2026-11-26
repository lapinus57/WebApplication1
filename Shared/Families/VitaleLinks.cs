using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EyeChat.Families
{
    /// <summary>
    /// Une fiche bénéficiaire du logiciel Vitale (tables PATIENT + BENEFICIAIRE), exportée en CSV.
    /// <see cref="Card"/> est une empreinte du numéro de l'assuré (HASH dans la requête d'export) : jamais le numéro lui-même.
    /// </summary>
    public sealed record VitaleRow(int PatientId, string Surname, string BirthName, string FirstName, DateTime? BirthDate,
        string Card, int? Quality, bool? Active, DateTime? Created, DateTime? ValidUntil)
    {
        /// <summary>Nom d'usage (PAT_NOMUSUEL), souvent le nom d'épouse.</summary>
        public string UsualName { get; init; } = "";
        /// <summary>Tous les prénoms (PAT_LISTEPRENOM).</summary>
        public string AllFirstNames { get; init; } = "";
        /// <summary>« M », « F » ou vide.</summary>
        public string Sex { get; init; } = "";
        /// <summary>Date de la dernière consultation (PAT_DATDERNCONSULT) : le bon dossier de l'agenda a un RDV ce jour-là.</summary>
        public DateTime? LastConsultation { get; init; }
    }

    /// <summary>Identité d'un patient de l'agenda (Code Patient), pour le rapprochement.</summary>
    public sealed record AgendaIdentity(int Code, string Surname, string MaidenName, string FirstName, DateTime? BirthDate)
    {
        /// <summary>« M », « F » ou vide, d'après la civilité (Monsieur, Madame…).</summary>
        public string Sex { get; init; } = "";
    }

    /// <summary>Lien déclaré : deux patients de l'agenda sur la même carte Vitale.</summary>
    /// <param name="Household">Le rattachement suffit à dire « même foyer » : conjoints, ou enfant(s) de moins de 20 ans.</param>
    public sealed record DeclaredLink(int A, int B, string Relation, bool Current, bool Separated, DateTime? Since, bool Household = true)
    {
        /// <summary>Pour « parent et enfant » : le Code Patient de l'enfant (qualité 6).</summary>
        public int? Child { get; init; }
    }

    public sealed class VitaleMatch
    {
        public Dictionary<int, int> CodeByVitaleId { get; } = new();
        public int VitalePatients { get; set; }
        public int Matched { get; set; }
        public int Ambiguous { get; set; }
        public int WithoutBirthDate { get; set; }
        public int NotFound { get; set; }
        /// <summary>Dont rapprochés grâce à la dernière consultation (homonymes départagés, nom ou prénom écrit autrement).</summary>
        public int MatchedByConsultation { get; set; }
    }

    public static class VitaleLinks
    {
        /// <summary>Codes « qualité » SESAM-Vitale : séparé, divorcé, veuf. Ils empêchent le regroupement des rappels.</summary>
        public static readonly int[] SeparatedQualities = { 3, 5, 8 };
        /// <summary>Une carte portée par plus de patients que cela est une anomalie de saisie : ignorée.</summary>
        public const int MaxPatientsPerCard = 10;
        /// <summary>
        /// Au-delà, un enfant encore « actif » sur la carte de ses parents n'est plus présumé vivre avec eux : les fiches
        /// Vitale restent souvent actives des années après le départ (mesuré : familles de 7 personnes ou plus × 3,5).
        /// </summary>
        public const int ChildHouseholdMaxAge = 20;

        /// <summary>
        /// Rapproche chaque patient Vitale d'un Code Patient de l'agenda : même date de naissance, même premier prénom,
        /// et un nom commun (nom d'usage ou de naissance). Plusieurs candidats : ambigu, non rapproché.
        /// </summary>
        /// <param name="visitedOn">Le patient (Code Patient) avait-il un RDV ce jour-là dans l'agenda ?</param>
        public static VitaleMatch Match(IEnumerable<VitaleRow> vitale, IEnumerable<AgendaIdentity> agenda, Func<int, DateTime, bool>? visitedOn = null)
        {
            var byBirth = agenda.Where(a => a.BirthDate.HasValue).GroupBy(a => a.BirthDate!.Value.Date).ToDictionary(g => g.Key, g => g.ToList());
            var match = new VitaleMatch();
            foreach (var person in vitale.GroupBy(v => v.PatientId).Select(g => g.First()))
            {
                match.VitalePatients++;
                if (person.BirthDate is not { } birth) { match.WithoutBirthDate++; continue; }
                // Noms : nom, nom de naissance et nom d'usage (nom d'épouse). Prénoms : premier prénom ou n'importe lequel
                // de la liste des prénoms (prénoms composés, second prénom d'usage).
                var surnames = FamilyScanner.SurnameTokens(person.Surname + " " + person.UsualName, person.BirthName);
                var firstNames = FirstNameTokens(person.FirstName + " " + person.AllFirstNames);
                var candidates = byBirth.TryGetValue(birth.Date, out var list)
                    ? list.Where(a => FirstToken(a.FirstName) is { Length: > 0 } f && firstNames.Contains(f)
                            && FamilyScanner.SurnameTokens(a.Surname, a.MaidenName).Overlaps(surnames))
                        .GroupBy(a => a.Code).Select(g => g.First()).ToList()
                    : new List<AgendaIdentity>();
                // Homonymes : le sexe (civilité de l'agenda) peut les départager.
                if (candidates.Count > 1 && person.Sex.Length > 0)
                {
                    var sameSex = candidates.Where(c => c.Sex == person.Sex).ToList();
                    if (sameSex.Count == 1) candidates = sameSex;
                }
                // Dernière consultation : le bon dossier avait un RDV ce jour-là.
                bool SeenThatDay(AgendaIdentity a) => visitedOn != null && person.LastConsultation is { } day && visitedOn(a.Code, day.Date);
                var byConsultation = false;
                if (candidates.Count > 1 && candidates.Count(SeenThatDay) == 1) { candidates = candidates.Where(SeenThatDay).ToList(); byConsultation = true; }
                if (candidates.Count == 0 && list != null)
                {
                    // Nom OU prénom écrit autrement : même naissance, l'un des deux concorde, et venu le jour de la dernière consultation.
                    var relaxed = list.Where(a => SeenThatDay(a)
                            && ((FirstToken(a.FirstName) is { Length: > 0 } f && firstNames.Contains(f)) || FamilyScanner.SurnameTokens(a.Surname, a.MaidenName).Overlaps(surnames)))
                        .GroupBy(a => a.Code).Select(g => g.First()).ToList();
                    if (relaxed.Count == 1) { candidates = relaxed; byConsultation = true; }
                }
                if (candidates.Count == 1)
                {
                    match.CodeByVitaleId[person.PatientId] = candidates[0].Code; match.Matched++;
                    if (byConsultation) match.MatchedByConsultation++;
                }
                else if (candidates.Count > 1) match.Ambiguous++;
                else match.NotFound++;
            }
            return match;
        }

        private static HashSet<string> FirstNameTokens(string? names) => FamilyScanner.FirstNameTokens(names);

        /// <summary>Sexe d'après un code (1/2, M/F, H/F) ou une civilité (Monsieur, Madame, Mademoiselle…).</summary>
        public static string Sex(string? value)
        {
            var v = (value ?? string.Empty).Trim().ToUpperInvariant().TrimEnd('.');
            if (v is "1" or "M" or "H" or "MASCULIN" or "HOMME" or "MONSIEUR" or "MR") return "M";
            if (v is "2" or "F" or "FEMININ" or "FÉMININ" or "FEMME" or "MADAME" or "MME" or "MADEMOISELLE" or "MLLE") return "F";
            return "";
        }

        /// <summary>Premier prénom, même court (« Jo »).</summary>
        private static string FirstToken(string? firstName) => PaymentLinks.FirstToken(firstName);

        /// <summary>Liens entre patients de l'agenda portés par la même carte Vitale, avec la nature du lien.</summary>
        public static List<DeclaredLink> Links(IEnumerable<VitaleRow> rows, VitaleMatch match, DateTime today)
        {
            var links = new Dictionary<(int, int), DeclaredLink>();
            foreach (var card in rows.Where(r => r.Card.Length > 0 && match.CodeByVitaleId.ContainsKey(r.PatientId)).GroupBy(r => r.Card))
            {
                // Une fiche par patient sur cette carte : la plus récente encore valable, sinon la plus récente.
                var members = card.GroupBy(r => match.CodeByVitaleId[r.PatientId])
                    .Select(g => g.OrderByDescending(r => IsCurrent(r, today)).ThenByDescending(r => r.Created ?? DateTime.MinValue).First() with { PatientId = g.Key })
                    .ToList();
                if (members.Count < 2 || members.Count > MaxPatientsPerCard) continue;
                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        var a = members[i]; var b = members[j];
                        if (a.PatientId == b.PatientId) continue;
                        var current = IsCurrent(a, today) && IsCurrent(b, today);
                        var separated = current && (SeparatedQualities.Contains(a.Quality ?? -1) || SeparatedQualities.Contains(b.Quality ?? -1));
                        var since = new[] { a.Created, b.Created }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();
                        var key = a.PatientId < b.PatientId ? (a.PatientId, b.PatientId) : (b.PatientId, a.PatientId);
                        var relation = Relation(a.Quality, b.Quality);
                        var link = new DeclaredLink(key.Item1, key.Item2, relation, current, separated, since == default ? null : since,
                            Household: PresumedHousehold(relation, a, b, today))
                        {
                            Child = relation == "parent et enfant" ? (a.Quality == 6 ? a.PatientId : b.Quality == 6 ? b.PatientId : null) : null
                        };
                        // Deux cartes communes : on garde la plus parlante (actuelle d'abord).
                        if (!links.TryGetValue(key, out var existing) || (link.Current && !existing.Current)) links[key] = link;
                    }
            }
            return links.Values.ToList();
        }

        /// <summary>Conjoints : oui. Parent et enfant, enfants de la même carte : seulement si les enfants ont moins de 20 ans.</summary>
        public static bool PresumedHousehold(string relation, VitaleRow a, VitaleRow b, DateTime today)
        {
            bool Young(VitaleRow r) => r.BirthDate is { } birth && Age(birth, today) < ChildHouseholdMaxAge;
            return relation switch
            {
                "conjoints" => true,
                "parent et enfant" => (a.Quality == 6 && Young(a)) || (b.Quality == 6 && Young(b)),
                "enfants de la même carte" => Young(a) && Young(b),
                _ => false
            };
        }

        public static int Age(DateTime birth, DateTime today)
            => today.Year - birth.Year - (today.Date < birth.Date.AddYears(today.Year - birth.Year) ? 1 : 0);

        public static bool IsCurrent(VitaleRow row, DateTime today)
            => row.Active != false && (row.ValidUntil is null || row.ValidUntil.Value.Date >= today.Date);

        /// <summary>Nature du lien d'après les qualités SESAM-Vitale (0 assuré, 2 conjoint, 4 concubin, 6 enfant…).</summary>
        public static string Relation(int? qa, int? qb)
        {
            var q = new[] { qa ?? -1, qb ?? -1 }.OrderBy(x => x).ToArray();
            bool Has(int x) => q.Contains(x);
            if (q.Any(x => SeparatedQualities.Contains(x))) return "conjoints séparés, divorcés ou veufs";
            if (Has(0) && Has(6)) return "parent et enfant";
            if (q[0] == 6 && q[1] == 6) return "enfants de la même carte";
            if (Has(0) && (Has(2) || Has(4))) return "conjoints";
            if ((Has(2) || Has(4)) && Has(6)) return "parent et enfant";
            if (Has(1)) return "ascendant ou descendant";
            return "même carte Vitale";
        }

        // ---------- Export CSV de DBeaver ----------

        /// <summary>
        /// Lit l'export CSV de la requête Vitale (en-tête : PAT_ID, NOM, NOM_NAISSANCE, PRENOM, NAISSANCE, CARTE, QUALITE,
        /// ACTIF, CREATION, FIN_VALIDITE ; « ACTIVE » est un mot réservé de Firebird). Séparateur « , » ou « ; », guillemets acceptés.
        /// </summary>
        public static List<VitaleRow> ReadCsv(TextReader reader)
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("Export Vitale vide.");
            var separator = header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
            var names = SplitCsv(header, separator).Select(n => n.Trim().Trim('﻿').ToUpperInvariant()).ToList();
            int Col(params string[] accepted) => accepted.Select(a => names.IndexOf(a)).FirstOrDefault(i => i >= 0, -1) is var i and >= 0 ? i
                : throw new InvalidDataException($"Colonne « {accepted[0]} » absente de l'export Vitale. Colonnes : {string.Join(", ", names)}. Utilisez la requête fournie.");
            int Optional(string name) => names.IndexOf(name);
            int usuel = Optional("NOM_USUEL"), prenoms = Optional("PRENOMS"), sexe = Optional("SEXE"), consult = Optional("DERNIERE_CONSULT");
            string Get(List<string> f, int i) => i >= 0 && i < f.Count ? f[i] : "";
            int id = Col("PAT_ID"), nom = Col("NOM"), naissanceNom = Col("NOM_NAISSANCE"), prenom = Col("PRENOM"), naissance = Col("NAISSANCE"),
                carte = Col("CARTE"), qualite = Col("QUALITE"), active = Col("ACTIF", "ACTIVE"), creation = Col("CREATION"), fin = Col("FIN_VALIDITE");
            var rows = new List<VitaleRow>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                var f = SplitCsv(line, separator);
                if (f.Count < names.Count) continue;
                if (!int.TryParse(f[id], NumberStyles.Integer, CultureInfo.InvariantCulture, out var patientId)) continue;
                rows.Add(new VitaleRow(patientId, f[nom], f[naissanceNom], f[prenom], Date(f[naissance]), f[carte].Trim(),
                    int.TryParse(f[qualite], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : null,
                    Bool(f[active]), Date(f[creation]), Date(f[fin]))
                {
                    UsualName = Get(f, usuel), AllFirstNames = Get(f, prenoms), Sex = Sex(Get(f, sexe)), LastConsultation = Date(Get(f, consult))
                });
            }
            return rows;
        }

        public static List<string> SplitCsv(string line, char separator)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == separator) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static readonly string[] DateFormats =
            { "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy", "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss" };

        public static DateTime? Date(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || text.Equals("[NULL]", StringComparison.OrdinalIgnoreCase)) return null;
            return DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
                : DateTime.TryParse(text, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out d) ? d : null;
        }

        public static bool? Bool(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "1" or "-1" or "T" or "TRUE" or "O" or "OUI" or "Y" or "YES" or "V" or "VRAI" => true,
            "0" or "F" or "FALSE" or "N" or "NON" or "NO" or "FAUX" => false,
            _ => null
        };
    }
}
