using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    // Adapted from EyeEmail/MainViewModel at eb49ce0; no database writes.
    public static class EyeEmailRecordMapper
    {
        private static readonly CompareInfo FrenchCompareInfo = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
        private static readonly Regex PrintedAppointmentTimeRegex = new("imprim[eé]\\s*[aà]\\s*(?<hour>\\d{1,2})h\\s*(?<minutes>\\d{2})?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static bool ShouldIgnoreAppointment(RdvInfo appointment)
        {
            if (appointment.Nom is null)
            {
                return false;
            }

            return string.Equals(appointment.Nom, "ABSENCE", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(appointment.Nom, "//////////////////////", StringComparison.Ordinal);
        }

        public static RdvInfo? MapAppointment(IDataRecord reader, IReadOnlyDictionary<string, int> ordinals, DateTime targetDate)
        {
            var isCancelled = GetBoolean(reader, ordinals, false, "Annulé", "Annule", "Canceled", "Cancelled");
            if (isCancelled)
            {
                return null;
            }

            var patientId = GetInt(reader, ordinals, "Code Patient", "CodePatient", "Id", "IDPatient", "Code");
            if (patientId == 0)
            {
                return null;
            }

            var adresse = GetString(reader, ordinals, "Adresse", "Adr");
            var ville = GetString(reader, ordinals, "Ville", "Commune", "CP Ville", "CPVille");
            var tel1Raw = GetString(reader, ordinals, "Tel 1", "Téléphone 1", "Téléphone", "Telephone", "Tel");
            var tel1Info = GetString(reader, ordinals, "Tel 1 Info", "Tel1 Info", "Tel1Info");
            var tel2Raw = GetString(reader, ordinals, "Tel 2", "Téléphone 2", "Portable", "Mobile", "Tel Bureau", "TelBureau", "Tel2");
            var tel2Info = GetString(reader, ordinals, "Tel 2 Info", "Tel2 Info", "Tel2Info");

            var rdv = new RdvInfo
            {
                Id = patientId,
                Titre = GetString(reader, ordinals, "Titre", "Civilité", "Civilite"),
                Nom = GetString(reader, ordinals, "Nom"),
                NomJeuneFille = GetString(reader, ordinals, "Nom jeune fille", "NomJeuneFille", "NomJF"),
                Prenom = GetString(reader, ordinals, "Prénom", "Prenom"),
                Adresse = adresse,
                Ville = ville,
                Tel1 = tel1Raw,
                Tel1Info = tel1Info,
                SMSSendTel1 = GetBoolean(reader, ordinals, false, "SMSSendTel1", "SmsTel1Envoye"),
                SMSSendTel1Date = GetDateTime(reader, ordinals, "SMSSendTel1Date", "SmsTel1Date"),
                Tel2 = tel2Raw,
                Tel2Info = tel2Info,
                SMSSendTel2 = GetBoolean(reader, ordinals, false, "SMSSendTel2", "SmsTel2Envoye"),
                SMSSendTel2Date = GetDateTime(reader, ordinals, "SMSSendTel2Date", "SmsTel2Date"),
                EmailInfo = GetString(reader, ordinals, "Email Info", "EmailInfo"),
                EmailSend = GetBoolean(reader, ordinals, false, "EmailSend", "MailEnvoye"),
                EmailSendDate = GetDateTime(reader, ordinals, "EmailSendDate", "EmailDate"),
                Remarque = GetString(reader, ordinals, "Remarque", "Commentaire", "Commentaires"),
                IsCancelled = false
            };

            var prise = GetDateTime(reader, ordinals, "Pris le", "Créé le", "Date création", "DateCreation", "DatePriseRDV");
            rdv.DatePriseRDV = prise ?? targetDate;

            var dateRdv = GetDateTime(reader, ordinals, "EyeChatDate", "Date", "Jour", "Date RDV");
            rdv.DateRDV = dateRdv ?? targetDate;

            var heure = GetDateTime(reader, ordinals, "EyeChatTime", "Heure", "Heure RDV", "HeureRdv");
            rdv.HeureRDV = CombineDateAndTime(rdv.DateRDV, heure);

            var arrivee = GetDateTime(reader, ordinals, "Arrivé à", "Arrivee", "Heure arrivée", "HeureArrivee");
            rdv.HeureArrivee = arrivee is null ? null : CombineDateAndTime(rdv.DateRDV, arrivee);

            ApplyRemarkBasedAdjustments(rdv);
            NormalizeContactDetails(rdv);

            var emailPrincipal = GetString(reader, ordinals, "E-mail", "Email", "PatientEmail");
            AssignEmails(rdv, emailPrincipal);
            AssignEmails(rdv, GetString(reader, ordinals, "E-mail 2", "Email 2", "EmailCopie", "Email copie"));

            return rdv;
        }

        public static List<RdvInfo> ConsolidateDuplicateAppointments(List<RdvInfo> appointments)
        {
            appointments = appointments.Where(rdv => !rdv.IsCancelled).ToList();
            if (appointments.Count <= 1)
            {
                return appointments;
            }

            var consolidated = appointments
                .Where(rdv => rdv.Id > 0)
                .GroupBy(static rdv => (rdv.Id, Date: rdv.DateRDV.Date))
                .Select(MergeAppointmentGroup)
                .Concat(appointments.Where(rdv => rdv.Id <= 0))
                .OrderBy(static rdv => rdv.HeureRDV == default ? DateTime.MaxValue : rdv.HeureRDV)
                .ThenBy(static rdv => rdv.Nom ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static rdv => rdv.Prenom ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return consolidated;
        }

        private static RdvInfo MergeAppointmentGroup(IGrouping<(int Id, DateTime Date), RdvInfo> group)
        {
            var orderedGroup = group
                .OrderBy(static rdv => rdv.HeureRDV == default ? DateTime.MaxValue : rdv.HeureRDV)
                .ThenBy(static rdv => rdv.DatePriseRDV == default ? DateTime.MaxValue : rdv.DatePriseRDV)
                .ThenBy(static rdv => rdv.Nom ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static rdv => rdv.Prenom ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var primary = orderedGroup[0];

            foreach (var duplicate in orderedGroup.Skip(1))
            {
                MergeAppointmentDetails(primary, duplicate);
            }

            return primary;
        }

        public static void MergeAppointmentDetails(RdvInfo target, RdvInfo source)
        {
            if (string.IsNullOrWhiteSpace(target.Titre) && !string.IsNullOrWhiteSpace(source.Titre))
            {
                target.Titre = source.Titre;
            }

            if (string.IsNullOrWhiteSpace(target.Nom) && !string.IsNullOrWhiteSpace(source.Nom))
            {
                target.Nom = source.Nom;
            }

            if (string.IsNullOrWhiteSpace(target.NomJeuneFille) && !string.IsNullOrWhiteSpace(source.NomJeuneFille))
            {
                target.NomJeuneFille = source.NomJeuneFille;
            }

            if (string.IsNullOrWhiteSpace(target.Prenom) && !string.IsNullOrWhiteSpace(source.Prenom))
            {
                target.Prenom = source.Prenom;
            }

            if (string.IsNullOrWhiteSpace(target.Adresse) && !string.IsNullOrWhiteSpace(source.Adresse))
            {
                target.Adresse = source.Adresse;
            }

            if (string.IsNullOrWhiteSpace(target.Ville) && !string.IsNullOrWhiteSpace(source.Ville))
            {
                target.Ville = source.Ville;
            }

            if (string.IsNullOrWhiteSpace(target.Remarque) && !string.IsNullOrWhiteSpace(source.Remarque))
            {
                target.Remarque = source.Remarque;
            }
            else if (!string.IsNullOrWhiteSpace(source.Remarque)
                     && !string.Equals(target.Remarque, source.Remarque, StringComparison.CurrentCultureIgnoreCase))
            {
                target.Remarque = string.IsNullOrWhiteSpace(target.Remarque)
                    ? source.Remarque
                    : $"{target.Remarque} / {source.Remarque}";
            }

            if (string.IsNullOrWhiteSpace(target.Tel1) && !string.IsNullOrWhiteSpace(source.Tel1))
            {
                target.Tel1 = source.Tel1;
                target.Tel1Info = string.IsNullOrWhiteSpace(target.Tel1Info) ? source.Tel1Info : target.Tel1Info;
            }
            else if (!string.IsNullOrWhiteSpace(source.Tel1)
                     && !string.Equals(target.Tel1, source.Tel1, StringComparison.OrdinalIgnoreCase)
                     && string.IsNullOrWhiteSpace(target.Tel2))
            {
                target.Tel2 = source.Tel1;
                if (string.IsNullOrWhiteSpace(target.Tel2Info))
                {
                    target.Tel2Info = source.Tel1Info;
                }
            }

            if (string.IsNullOrWhiteSpace(target.Tel2) && !string.IsNullOrWhiteSpace(source.Tel2))
            {
                target.Tel2 = source.Tel2;
                target.Tel2Info = string.IsNullOrWhiteSpace(target.Tel2Info) ? source.Tel2Info : target.Tel2Info;
            }

            target.SMSSendTel1 |= source.SMSSendTel1;
            if (source.SMSSendTel1Date.HasValue
                && (!target.SMSSendTel1Date.HasValue || source.SMSSendTel1Date < target.SMSSendTel1Date))
            {
                target.SMSSendTel1Date = source.SMSSendTel1Date;
            }

            target.SMSSendTel2 |= source.SMSSendTel2;
            if (source.SMSSendTel2Date.HasValue
                && (!target.SMSSendTel2Date.HasValue || source.SMSSendTel2Date < target.SMSSendTel2Date))
            {
                target.SMSSendTel2Date = source.SMSSendTel2Date;
            }

            if (string.IsNullOrWhiteSpace(target.Email) && !string.IsNullOrWhiteSpace(source.Email))
            {
                target.Email = source.Email;
            }
            else if (!string.IsNullOrWhiteSpace(source.Email))
            {
                AddAdditionalEmail(target, source.Email);
            }

            if (!string.IsNullOrWhiteSpace(source.EmailInfo) && string.IsNullOrWhiteSpace(target.EmailInfo))
            {
                target.EmailInfo = source.EmailInfo;
            }

            foreach (var email in source.AdditionalEmails)
            {
                AddAdditionalEmail(target, email);
            }

            target.EmailSend |= source.EmailSend;
            if (source.EmailSendDate.HasValue
                && (!target.EmailSendDate.HasValue || source.EmailSendDate < target.EmailSendDate))
            {
                target.EmailSendDate = source.EmailSendDate;
            }

            if (source.HeureArrivee.HasValue
                && (!target.HeureArrivee.HasValue || source.HeureArrivee < target.HeureArrivee))
            {
                target.HeureArrivee = source.HeureArrivee;
            }

            if (source.DatePriseRDV != default && (target.DatePriseRDV == default || source.DatePriseRDV < target.DatePriseRDV))
            {
                target.DatePriseRDV = source.DatePriseRDV;
            }

            target.Istried |= source.Istried;


            if (!string.IsNullOrWhiteSpace(source.Media) && string.IsNullOrWhiteSpace(target.Media))
            {
                target.Media = source.Media;
            }

            target.IsCancelled |= source.IsCancelled;
        }

        private static void AddAdditionalEmail(RdvInfo target, string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            if (!string.Equals(target.Email, email, StringComparison.OrdinalIgnoreCase)
                && !target.AdditionalEmails.Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase)))
            {
                target.AdditionalEmails.Add(email);
            }
        }

        public static void NormalizeContactDetails(RdvInfo rdv)
        {
            rdv.Adresse = NormalizeMultilineText(rdv.Adresse);
            rdv.Ville = NormalizeMultilineText(rdv.Ville);

            var (tel1Number, tel1Info) = SplitPhoneNumberAndInfo(rdv.Tel1);
            rdv.Tel1 = tel1Number;
            rdv.Tel1Info = SelectPhoneInfo(rdv.Tel1Info, tel1Info);

            var (tel2Number, tel2Info) = SplitPhoneNumberAndInfo(rdv.Tel2);
            rdv.Tel2 = tel2Number;
            rdv.Tel2Info = SelectPhoneInfo(rdv.Tel2Info, tel2Info);
        }

        public static void ApplyRemarkBasedAdjustments(RdvInfo rdv)
        {
            if (rdv is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(rdv.Remarque))
            {
                // Keep an explicit cancellation flag.
                return;
            }

            if (ContainsCancellationKeyword(rdv.Remarque))
            {
                rdv.IsCancelled = true;
                return;
            }

            // Keep an explicit cancellation flag.

            if (TryGetPrintedTimeFromRemark(rdv.Remarque, rdv.DateRDV, out var overrideTime))
            {
                rdv.HeureRDV = overrideTime;
            }
        }

        private static bool ContainsCancellationKeyword(string remark)
        {
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            return FrenchCompareInfo.IndexOf(remark, "annule", options) >= 0;
        }

        private static bool TryGetPrintedTimeFromRemark(string remark, DateTime dateRdv, out DateTime overrideTime)
        {
            var match = PrintedAppointmentTimeRegex.Match(remark);
            if (!match.Success)
            {
                overrideTime = default;
                return false;
            }

            if (!int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            {
                overrideTime = default;
                return false;
            }

            if (hour < 0 || hour > 23)
            {
                overrideTime = default;
                return false;
            }

            var minutes = 0;
            var minutesGroup = match.Groups["minutes"];
            if (minutesGroup.Success
                && !string.IsNullOrWhiteSpace(minutesGroup.Value)
                && int.TryParse(minutesGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMinutes))
            {
                minutes = Math.Max(0, Math.Min(59, parsedMinutes));
            }

            var baseDate = dateRdv == default ? DateTime.Today : dateRdv;
            var kind = baseDate.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : baseDate.Kind;
            overrideTime = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hour, minutes, 0, kind);
            return true;
        }

        private static string SelectPhoneInfo(string? explicitInfo, string extractedInfo)
        {
            if (!string.IsNullOrWhiteSpace(explicitInfo))
            {
                return NormalizeMultilineText(explicitInfo);
            }

            return NormalizeMultilineText(extractedInfo);
        }

        private static (string Number, string Info) SplitPhoneNumberAndInfo(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return (string.Empty, string.Empty);
            }

            var normalized = NormalizeMultilineText(rawValue);
            var index = normalized.IndexOf('(');

            if (index >= 0)
            {
                var number = NormalizeMultilineText(normalized[..index]);
                var info = NormalizeMultilineText(normalized[(index + 1)..].TrimEnd(')'));
                return (number, info);
            }

            return (normalized, string.Empty);
        }

        private static string NormalizeMultilineText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ');

            var builder = new StringBuilder();
            var previousWasSpace = false;

            foreach (var ch in normalized)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
                else
                {
                    builder.Append(ch);
                    previousWasSpace = false;
                }
            }

            return builder.ToString().Trim();
        }

        public static void AssignEmails(RdvInfo rdv, string? rawEmails)
        {
            if (string.IsNullOrWhiteSpace(rawEmails))
            {
                return;
            }

            foreach (var email in ExtractEmails(rawEmails))
            {
                if (string.IsNullOrWhiteSpace(rdv.Email))
                {
                    rdv.Email = email;
                }
                else if (!string.Equals(rdv.Email, email, StringComparison.OrdinalIgnoreCase) &&
                         !rdv.AdditionalEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
                {
                    rdv.AdditionalEmails.Add(email);
                }
            }
        }

        private static List<string> ExtractEmails(string? rawValue)
        {
            var emails = new List<string>();

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return emails;
            }

            foreach (var email in ParseEmails(rawValue))
            {
                if (IsValidEmail(email) && !emails.Contains(email, StringComparer.OrdinalIgnoreCase))
                {
                    emails.Add(email);
                }
            }

            return emails;
        }

        private static IEnumerable<string> ParseEmails(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Empty<string>();
            }

            return value
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            return System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _);
        }

        private static DateTime CombineDateAndTime(DateTime date, DateTime? time)
        {
            if (time is null || time.Value == default)
            {
                return date;
            }

            var baseDate = date;
            var kind = baseDate.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : baseDate.Kind;
            var t = time.Value.TimeOfDay;
            return new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, t.Hours, t.Minutes, t.Seconds, kind);
        }

        public static Dictionary<string, int> BuildOrdinalLookup(IDataRecord reader)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!lookup.ContainsKey(name))
                {
                    lookup[name] = i;
                }
            }

            return lookup;
        }

        public static int GetInt(IDataRecord reader, IReadOnlyDictionary<string, int> ordinals, params string[] names)
        {
            foreach (var name in names)
            {
                if (ordinals.TryGetValue(name, out var index) && !reader.IsDBNull(index))
                {
                    var value = reader.GetValue(index);
                    if (value is int i)
                    {
                        return i;
                    }

                    if (value is long l)
                    {
                        return (int)l;
                    }

                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                }
            }

            return 0;
        }

        public static DateTime? GetDateTime(IDataRecord reader, IReadOnlyDictionary<string, int> ordinals, params string[] names)
        {
            foreach (var name in names)
            {
                if (ordinals.TryGetValue(name, out var index) && !reader.IsDBNull(index))
                {
                    var value = reader.GetValue(index);
                    switch (value)
                    {
                        case DateTime dt:
                            return dt;
                        case double oa:
                            return DateTime.FromOADate(oa);
                        default:
                            var text = Convert.ToString(value, CultureInfo.CurrentCulture);
                            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
                            {
                                return parsed;
                            }

                            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                            {
                                return parsed;
                            }

                            break;
                    }
                }
            }

            return null;
        }

        public static bool GetBoolean(IDataRecord reader, IReadOnlyDictionary<string, int> ordinals, bool defaultValue, params string[] names)
        {
            foreach (var name in names)
            {
                if (ordinals.TryGetValue(name, out var index) && !reader.IsDBNull(index))
                {
                    var value = reader.GetValue(index);
                    switch (value)
                    {
                        case bool b:
                            return b;
                        case int i:
                            return i != 0;
                        case long l:
                            return l != 0;
                        default:
                            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                            if (bool.TryParse(text, out var parsedBool))
                            {
                                return parsedBool;
                            }

                            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                            {
                                return parsedInt != 0;
                            }

                            break;
                    }
                }
            }

            return defaultValue;
        }

        public static string GetString(IDataRecord reader, IReadOnlyDictionary<string, int> ordinals, params string[] names)
        {
            foreach (var name in names)
            {
                if (ordinals.TryGetValue(name, out var index) && !reader.IsDBNull(index))
                {
                    var value = Convert.ToString(reader.GetValue(index), CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
                    if (value.Length > 0) return value;
                }
            }

            return string.Empty;
        }

    }
}
