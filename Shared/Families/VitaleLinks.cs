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
        string Card, int? Quality, bool? Active, DateTime? Created, DateTime? ValidUntil);

    /// <summary>Identité d'un patient de l'agenda (Code Patient), pour le rapprochement.</summary>
    public sealed record AgendaIdentity(int Code, string Surname, string MaidenName, string FirstName, DateTime? BirthDate);

    /// <summary>Lien déclaré : deux patients de l'agenda sur la même carte Vitale.</summary>
    public sealed record DeclaredLink(int A, int B, string Relation, bool Current, bool Separated, DateTime? Since);

    public sealed class VitaleMatch
    {
        public Dictionary<int, int> CodeByVitaleId { get; } = new();
        public int VitalePatients { get; set; }
        public int Matched { get; set; }
        public int Ambiguous { get; set; }
        public int WithoutBirthDate { get; set; }
        public int NotFound { get; set; }
    }

    public static class VitaleLinks
    {
        /// <summary>Codes « qualité » SESAM-Vitale : séparé, divorcé, veuf. Ils empêchent le regroupement des rappels.</summary>
        public static readonly int[] SeparatedQualities = { 3, 5, 8 };
        /// <summary>Une carte portée par plus de patients que cela est une anomalie de saisie : ignorée.</summary>
        public const int MaxPatientsPerCard = 10;

        /// <summary>
        /// Rapproche chaque patient Vitale d'un Code Patient de l'agenda : même date de naissance, même premier prénom,
        /// et un nom commun (nom d'usage ou de naissance). Plusieurs candidats : ambigu, non rapproché.
        /// </summary>
        public static VitaleMatch Match(IEnumerable<VitaleRow> vitale, IEnumerable<AgendaIdentity> agenda)
        {
            var byBirth = agenda.Where(a => a.BirthDate.HasValue).GroupBy(a => a.BirthDate!.Value.Date).ToDictionary(g => g.Key, g => g.ToList());
            var match = new VitaleMatch();
            foreach (var person in vitale.GroupBy(v => v.PatientId).Select(g => g.First()))
            {
                match.VitalePatients++;
                if (person.BirthDate is not { } birth) { match.WithoutBirthDate++; continue; }
                var first = FirstToken(person.FirstName);
                var surnames = FamilyScanner.SurnameTokens(person.Surname, person.BirthName);
                var candidates = byBirth.TryGetValue(birth.Date, out var list)
                    ? list.Where(a => FirstToken(a.FirstName) == first && first.Length > 0 && FamilyScanner.SurnameTokens(a.Surname, a.MaidenName).Overlaps(surnames))
                        .Select(a => a.Code).Distinct().ToList()
                    : new List<int>();
                if (candidates.Count == 1) { match.CodeByVitaleId[person.PatientId] = candidates[0]; match.Matched++; }
                else if (candidates.Count > 1) match.Ambiguous++;
                else match.NotFound++;
            }
            return match;
        }

        private static string FirstToken(string? firstName)
            => FamilyScanner.SurnameTokens(firstName, null).FirstOrDefault() is { } token ? token
                : (firstName ?? string.Empty).Trim().ToUpperInvariant();

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
                        var link = new DeclaredLink(key.Item1, key.Item2, Relation(a.Quality, b.Quality), current, separated, since == default ? null : since);
                        // Deux cartes communes : on garde la plus parlante (actuelle d'abord).
                        if (!links.TryGetValue(key, out var existing) || (link.Current && !existing.Current)) links[key] = link;
                    }
            }
            return links.Values.ToList();
        }

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
        /// ACTIVE, CREATION, FIN_VALIDITE). Séparateur « , » ou « ; », guillemets acceptés.
        /// </summary>
        public static List<VitaleRow> ReadCsv(TextReader reader)
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("Export Vitale vide.");
            var separator = header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
            var names = SplitCsv(header, separator).Select(n => n.Trim().Trim('﻿').ToUpperInvariant()).ToList();
            int Col(string name) => names.IndexOf(name) is var i and >= 0 ? i
                : throw new InvalidDataException($"Colonne « {name} » absente de l'export Vitale. Colonnes : {string.Join(", ", names)}. Utilisez la requête fournie.");
            int id = Col("PAT_ID"), nom = Col("NOM"), naissanceNom = Col("NOM_NAISSANCE"), prenom = Col("PRENOM"), naissance = Col("NAISSANCE"),
                carte = Col("CARTE"), qualite = Col("QUALITE"), active = Col("ACTIVE"), creation = Col("CREATION"), fin = Col("FIN_VALIDITE");
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
                    Bool(f[active]), Date(f[creation]), Date(f[fin])));
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
