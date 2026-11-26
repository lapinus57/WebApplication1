using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EyeChat.Families
{
    /// <summary>
    /// Une facture de l'export « suivi des paiements » du logiciel Vitale (une ligne par facture).
    /// Seules les colonnes utiles sont lues : jamais le NIR du bénéficiaire, ni les numéros de chèque ou de banque.
    /// </summary>
    public sealed record PaymentRow(string PayerId, string PayerSurname, string PayerFirstName,
        string BeneficiarySurname, string BeneficiaryFirstName, DateTime? InvoiceDate);

    public sealed class PaymentStats
    {
        public int Lines { get; set; }
        public int Organisms { get; set; }
        public int SelfPaid { get; set; }
        public int PaidForSomeoneElse { get; set; }
        public int BeneficiaryFound { get; set; }
        public int PayerFound { get; set; }
        public int CollectivePayers { get; set; }
    }

    public static class PaymentLinks
    {
        /// <summary>Un payeur qui règle pour plus de bénéficiaires que cela (tutelle, foyer, association) ne fait pas une famille.</summary>
        public const int MaxBeneficiariesPerPayer = 8;

        // Un organisme n'a pas de prénom ; son nom contient souvent l'un de ces mots.
        private static readonly string[] OrganismWords =
        {
            "CPAM", "CAISSE", "MUTUELLE", "MUTUELLES", "MUTUALITE", "MSA", "ASSURANCE", "ASSURANCES", "SECURITE", "PREVOYANCE", "AMC", "AMO",
            "CMU", "CSS", "CNMSS", "RSI", "ENIM", "CRPCEN", "HARMONIE", "MGEN", "MAAF", "MACIF", "MATMUT", "GROUPAMA", "AXA", "ALLIANZ",
            "MALAKOFF", "HUMANIS", "AG2R", "APRIL", "SWISSLIFE", "GENERALI", "ALMERYS", "VIAMEDIS", "SANTECLAIR", "KALIXIA", "ITELIS",
            "TIERS", "ORGANISME", "CENTRE", "REGIME", "SANTE", "SAS", "SARL", "SA", "ASSOCIATION", "CONSEIL", "DEPARTEMENT"
        };

        public static bool IsPersonPayer(PaymentRow row)
        {
            if (row.PayerFirstName.Trim().Length == 0 || row.PayerSurname.Trim().Length == 0) return false;
            var words = Simplify(row.PayerSurname + " " + row.PayerFirstName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return !words.Any(w => OrganismWords.Contains(w));
        }

        public static bool SamePerson(PaymentRow row)
            => FamilyScanner.SurnameTokens(row.PayerSurname, null).Overlaps(FamilyScanner.SurnameTokens(row.BeneficiarySurname, null))
               && FirstToken(row.PayerFirstName) == FirstToken(row.BeneficiaryFirstName);

        public static string FirstToken(string? firstName)
            => Simplify(firstName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        private static string Simplify(string? value)
        {
            var text = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
            var chars = text.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ').ToArray();
            return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Lit l'export (séparateur « ; » ou « , »), en ignorant les colonnes sensibles (NIR, chèque, banque).</summary>
        public static List<PaymentRow> ReadCsv(TextReader reader)
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("Export des règlements vide.");
            var separator = header.Count(c => c == ';') >= header.Count(c => c == ',') ? ';' : ',';
            var names = VitaleLinks.SplitCsv(header, separator).Select(n => n.Trim().Trim('﻿').ToUpperInvariant()).ToList();
            int Col(string name) => names.IndexOf(name.ToUpperInvariant()) is var i and >= 0 ? i
                : throw new InvalidDataException($"Colonne « {name} » absente de l'export des règlements. Colonnes : {string.Join(", ", names)}.");
            int payerId = names.IndexOf("IDPAYEUR"), payerSurname = Col("NomPayeur"), payerFirst = Col("PrenomPayeur"),
                benefSurname = Col("NomBeneficiaire"), benefFirst = Col("PrenomBeneficiaire"), date = Col("DateFacture");
            var rows = new List<PaymentRow>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                var f = VitaleLinks.SplitCsv(line, separator);
                string Get(int i) => i >= 0 && i < f.Count ? f[i].Trim() : "";
                rows.Add(new PaymentRow(Get(payerId), Get(payerSurname), Get(payerFirst), Get(benefSurname), Get(benefFirst), VitaleLinks.Date(Get(date))));
            }
            return rows;
        }

        /// <summary>UTF-8 si le fichier est valide en UTF-8, sinon Latin-1 (exports Windows).</summary>
        public static TextReader Open(string path)
        {
            var bytes = File.ReadAllBytes(path);
            try { return new StringReader(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿')); }
            catch (DecoderFallbackException) { return new StringReader(Encoding.Latin1.GetString(bytes)); }
        }
    }
}
