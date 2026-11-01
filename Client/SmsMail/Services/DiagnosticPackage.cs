using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Utilities;

namespace Client.SmsMail.Services;

// Encrypted snapshot of one preparation (appointments, families, messages) so a problem
// can be analysed away from the cabinet. Secrets (OVH keys, SMTP password) are never included.
public static class DiagnosticPackage
{
    public const string Format = "eyechat-sms-diagnostic";
    public const string Extension = ".eyediag";
    public const int MinimumPasswordLength = 10;
    private const int Iterations = 600_000;
    private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("EYECHAT-DIAG-1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public sealed class Input
    {
        public DateTime Day { get; init; }
        public IReadOnlyList<RdvInfo> Appointments { get; init; } = Array.Empty<RdvInfo>();
        public IReadOnlyList<RdvFamille> Families { get; init; } = Array.Empty<RdvFamille>();
        public IReadOnlyList<PreparedMessage> Messages { get; init; } = Array.Empty<PreparedMessage>();
        public IReadOnlyDictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>> History { get; init; }
            = new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>();
        public SmsMailSettings Settings { get; init; } = new();
        public bool Pseudonymize { get; init; } = true;
        public string Note { get; init; } = string.Empty;
    }

    public static string BuildJson(Input input)
    {
        var p = input.Pseudonymize ? new Pseudonymizer(input.Appointments) : null;
        string T(string? text) => p?.Text(text) ?? text ?? string.Empty;

        var rows = input.Appointments.ToList();
        var line = new Dictionary<RdvInfo, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rows.Count; i++) line[rows[i]] = i + 1;
        int IdOf(int id) => p?.Id(id) ?? id;

        var snapshot = new Dictionary<string, object?>
        {
            ["format"] = Format,
            ["version"] = 1,
            ["createdAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["appointmentDay"] = input.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["pseudonymized"] = input.Pseudonymize,
            ["note"] = T(input.Note),
            ["settings"] = new
            {
                input.Settings.SmsMinimumFamilySize, input.Settings.SmsOnlyWhenNoEmail, input.Settings.UseEyeEmailFamilies,
                FamilyDatabaseConfigured = !string.IsNullOrWhiteSpace(input.Settings.FamilyDatabasePath),
                input.Settings.AdjustReminderTimes,
                ReminderTimeRules = input.Settings.ReminderTimeRules.Select(r => new { From = r.From.ToString(), To = r.To.ToString() }),
                input.Settings.AppointmentDurationMinutes, input.Settings.SendCalendarInvites, input.Settings.SendCalendarAlarms,
                input.Settings.Subject, input.Settings.SmsTemplate, input.Settings.Cabinet,
                SmsServiceConfigured = !string.IsNullOrWhiteSpace(input.Settings.Sms.ServiceName),
                SmtpConfigured = !string.IsNullOrWhiteSpace(input.Settings.Email.SmtpHost)
            },
            ["appointments"] = rows.Select(r => new
            {
                Ligne = line[r], Id = IdOf(r.Id), r.Titre,
                Nom = p?.Name(r.Nom) ?? r.Nom, NomJeuneFille = p?.Name(r.NomJeuneFille) ?? r.NomJeuneFille, Prenom = p?.Name(r.Prenom) ?? r.Prenom,
                DatePriseRDV = Stamp(r.DatePriseRDV), DateRDV = Stamp(r.DateRDV), HeureRDV = Stamp(r.HeureRDV),
                HeureArrivee = r.HeureArrivee.HasValue ? Stamp(r.HeureArrivee.Value) : null,
                Remarque = T(r.Remarque),
                Adresse = p?.Address(r.Adresse) ?? r.Adresse, Ville = p?.City(r.Ville) ?? r.Ville,
                Tel1 = p?.Phone(r.Tel1) ?? r.Tel1, Tel1Info = T(r.Tel1Info),
                Tel2 = p?.Phone(r.Tel2) ?? r.Tel2, Tel2Info = T(r.Tel2Info),
                Email = p?.EmailField(r.Email) ?? r.Email, EmailInfo = T(r.EmailInfo),
                AdditionalEmails = r.AdditionalEmails.Select(e => p?.EmailField(e) ?? e),
                r.IsCancelled, r.ReferenceFamilyNumber, r.NumeroFamille,
                Methodes = new[] { r.TriedMethodRound1, r.TriedMethodRound2, r.TriedMethodRound3, r.TriedMethodRound4, r.TriedMethodRound5, r.TriedMethodRound6 }
                    .Where(m => !string.IsNullOrWhiteSpace(m))
            }),
            ["families"] = input.Families.Select(f => new
            {
                f.Numero, f.PersonCount, Regroupement = f.GroupingDisplay,
                Lignes = f.RDVs.Select(r => line.TryGetValue(r, out var n) ? n : 0)
            }),
            ["messages"] = input.Messages.Select(m => new
            {
                m.Channel, Contact = p?.Contact(m.Contact) ?? m.Contact,
                Personnes = T(m.PeopleDisplay), RegroupesPar = T(m.GroupingReason), m.Status, m.CanSend, m.Selected,
                Rdv = Stamp(m.AppointmentStart), VenueAnnoncee = m.CalendarStart == default ? null : Stamp(m.CalendarStart),
                HorairesAjustes = T(m.TimeExplanation),
                Message = T(m.Channel == "Mail" ? MailTextPreview.Convert(m.Body) : m.Body),
                ClesAntiDoublon = m.ReservationKeys.Count
            }),
            ["history"] = input.History.ToDictionary(
                pair => IdOf(pair.Key).ToString(CultureInfo.InvariantCulture),
                pair => pair.Value.Select(v => new
                {
                    Jour = v.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Rdv = v.RdvTime.HasValue ? Stamp(v.RdvTime.Value) : null,
                    Arrivee = v.ArrivalTime.HasValue ? Stamp(v.ArrivalTime.Value) : null
                }))
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static byte[] Encrypt(string json, string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
            throw new InvalidOperationException($"Le mot de passe doit contenir au moins {MinimumPasswordLength} caractères.");
        byte[] plain;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(Encoding.UTF8.GetBytes(json));
            plain = buffer.ToArray();
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
        CryptographicOperations.ZeroMemory(key);
        var envelope = new
        {
            format = Format, version = 1, cipher = "AES-256-GCM", kdf = "PBKDF2-SHA256", iterations = Iterations,
            aad = Encoding.ASCII.GetString(AssociatedData), compression = "gzip",
            salt = Convert.ToBase64String(salt), nonce = Convert.ToBase64String(nonce),
            tag = Convert.ToBase64String(tag), data = Convert.ToBase64String(cipher)
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Decrypt(byte[] file, string password)
    {
        using var document = JsonDocument.Parse(file);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != Format) throw new InvalidDataException("Ce fichier n'est pas un diagnostic SMS/mail.");
        byte[] Get(string name) => Convert.FromBase64String(root.GetProperty(name).GetString()!);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Get("salt"), root.GetProperty("iterations").GetInt32(), HashAlgorithmName.SHA256, 32);
        var cipher = Get("data");
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Get("nonce"), cipher, Get("tag"), plain, Encoding.ASCII.GetBytes(root.GetProperty("aad").GetString()!));
        }
        catch (CryptographicException) { throw new InvalidOperationException("Mot de passe incorrect ou fichier modifié."); }
        finally { CryptographicOperations.ZeroMemory(key); }
        using var gzip = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Stamp(DateTime value) => value == default ? string.Empty : value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    // Replaces identities with stable fake values: the same real value always gives the
    // same fake value inside one export, so equalities used by the grouping are kept.
    // The key is random and discarded, so the real values cannot be recovered from the file.
    private sealed class Pseudonymizer
    {
        private static readonly string[] CommonDomains =
        {
            "gmail.com", "hotmail.fr", "hotmail.com", "outlook.fr", "outlook.com", "live.fr", "yahoo.fr", "yahoo.com",
            "orange.fr", "wanadoo.fr", "free.fr", "sfr.fr", "neuf.fr", "laposte.net", "icloud.com", "bbox.fr", "aol.com"
        };
        private static readonly Regex EmailPattern = new(@"[^\s<>;,()""']+@[^\s<>;,()""']+\.[^\s<>;,()""']+", RegexOptions.Compiled);
        private static readonly Regex PhonePattern = new(@"(?<!\d)(?:\+|00)?\d(?:[\s.\-]?\d){7,13}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex WordPattern = new(@"[\p{L}]+", RegexOptions.Compiled);
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        private readonly HashSet<string> _nameWords = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _ids = new();
        private List<(string Real, string Fake)> _places = new();
        // Name particles and short words also used in ordinary sentences ("dit de venir").
        private static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
        {
            "DE", "DU", "DES", "LA", "LE", "LES", "DI", "DA", "DO", "DOS", "DAS", "DEL", "VAN", "VON", "DER", "DEN", "EL", "AL", "ET", "Y"
        };

        public Pseudonymizer(IEnumerable<RdvInfo> rows)
        {
            foreach (var row in rows)
            {
                foreach (var value in new[] { row.Nom, row.Prenom, row.NomJeuneFille })
                    foreach (Match word in WordPattern.Matches(value ?? string.Empty))
                        if (word.Value.Length >= 2 && !Particles.Contains(Fold(word.Value))) _nameWords.Add(Fold(word.Value));
                if (row.Id > 0 && !_ids.ContainsKey(row.Id)) _ids[row.Id] = _ids.Count + 1;
            }
            // Addresses and cities quoted in free text (remarks, "Même adresse : …") are replaced too, longest first.
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Adresse)) _places.Add((row.Adresse.Trim(), Address(row.Adresse)));
                if (!string.IsNullOrWhiteSpace(row.Ville)) _places.Add((row.Ville.Trim(), City(row.Ville)));
            }
            _places = _places.Where(p => p.Real.Length >= 3).DistinctBy(p => p.Real.ToUpperInvariant()).OrderByDescending(p => p.Real.Length).ToList();
        }

        public int Id(int id)
        {
            if (id <= 0) return id;
            if (!_ids.TryGetValue(id, out var mapped)) _ids[id] = mapped = _ids.Count + 1;
            return mapped;
        }

        public string Name(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty
            : WordPattern.Replace(value, m => m.Value.Length >= 2 && !Particles.Contains(Fold(m.Value)) ? Word(m.Value) : m.Value);

        public string City(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : "Ville-" + Word(AddressNormalizer.NormalizeAddress(value), "city");

        // Keeps the house number (12 / 2 / 12 bis...) because the grouping compares it.
        public string Address(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var match = Regex.Match(value.Trim(), @"^(\d+\s*(?:bis|ter|b|t)?\b)?[\s,]*(.*)$", RegexOptions.IgnoreCase);
            var number = match.Groups[1].Value.Trim();
            var street = AddressNormalizer.NormalizeAddress(match.Groups[2].Value);
            var fake = street.Length == 0 ? string.Empty : "voie " + Word(street, "street");
            return (number + " " + fake).Trim();
        }

        public string Phone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var digits = FamilyUtilities.NormalizePhoneNumber(value);
            if (digits.Length == 0) return Text(value);
            // Placeholder numbers stay visible: they are part of what the grouping checks.
            if (!FamilyUtilities.IsUsableGroupingPhone(digits)) return digits.Length >= 9 ? digits : $"[numéro incomplet : {digits.Length} chiffre(s)]";
            return FakePhone(digits, value);
        }

        public string Contact(string value)
        {
            if (value.Contains('@')) return EmailField(value);
            var digits = FamilyUtilities.NormalizePhoneNumber(value);
            if (!FamilyUtilities.IsUsableGroupingPhone(digits)) return value;
            var fake = FakePhone(digits, value);
            return value.StartsWith("+33", StringComparison.Ordinal) && fake.StartsWith('0') ? "+33" + fake.Substring(1) : fake;
        }

        public string EmailField(string? value) => string.IsNullOrEmpty(value) ? string.Empty : Text(value);

        public string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var result = value;
            foreach (var (real, fake) in _places)
                result = Regex.Replace(result, @"(?<![\p{L}\d])" + Regex.Escape(real) + @"(?![\p{L}\d])", fake.Replace("$", "$$"), RegexOptions.IgnoreCase);
            result = EmailPattern.Replace(result, m => FakeEmail(m.Value));
            result = PhonePattern.Replace(result, m =>
            {
                var digits = FamilyUtilities.NormalizePhoneNumber(m.Value);
                return FamilyUtilities.IsUsableGroupingPhone(digits) ? FakePhone(digits, m.Value) : m.Value;
            });
            return WordPattern.Replace(result, m => m.Value.Length >= 2 && _nameWords.Contains(Fold(m.Value)) ? Word(m.Value) : m.Value);
        }

        private string FakeEmail(string email)
        {
            var at = email.LastIndexOf('@');
            var local = email.Substring(0, at).ToLowerInvariant();
            var domain = email.Substring(at + 1).ToLowerInvariant().TrimEnd('.');
            var fakeDomain = CommonDomains.Contains(domain) ? domain : "domaine-" + Word(domain, "domain").ToLowerInvariant() + ".fr";
            return Word(local, "mail").ToLowerInvariant() + "@" + fakeDomain;
        }

        // French numbers keep 0 + their first digit (06, 07, 03…). Foreign numbers keep the
        // country code and their written form (+352… or 00352…), so the SMS rules still apply.
        private string FakePhone(string digits, string original)
        {
            var written = original.TrimStart();
            bool international = digits.StartsWith("00", StringComparison.Ordinal) || (digits[0] != '0' && written.StartsWith('+'));
            var canonical = digits.StartsWith("00", StringComparison.Ordinal) ? digits.Substring(2) : digits;
            int keep = international ? Math.Min(3, canonical.Length) : Math.Min(2, canonical.Length);
            var fake = new StringBuilder(canonical.Substring(0, keep));
            var hash = Hash("phone", canonical);
            for (int i = keep; i < canonical.Length; i++) fake.Append((char)('0' + hash[i % hash.Length] % 10));
            if (!international) return fake.ToString();
            return (written.StartsWith('+') ? "+" : "00") + fake;
        }

        // Same folded word -> same fake word. Letters only, so name normalisation keeps it whole.
        private string Word(string value, string kind = "name")
        {
            var hash = Hash(kind, Fold(value));
            const string consonants = "bcdfgjklmnprstvz", vowels = "aeiou";
            var builder = new StringBuilder();
            for (int i = 0; i < 6; i++) builder.Append(i % 2 == 0 ? consonants[hash[i] % consonants.Length] : vowels[hash[i] % vowels.Length]);
            builder[0] = char.ToUpperInvariant(builder[0]);
            return builder.ToString();
        }

        private byte[] Hash(string kind, string value)
        {
            using var hmac = new HMACSHA256(_key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(kind + "\n" + value));
        }

        private static string Fold(string value)
        {
            var builder = new StringBuilder();
            foreach (var ch in value.ToUpperInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
            return builder.ToString();
        }
    }
}
