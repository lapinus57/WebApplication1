using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EyeChat.SmsMail
{
    /// <summary>Message à réserver avant envoi : la clé du message et les clés de garde (famille, téléphones…).</summary>
    public sealed class HistoryReservation
    {
        public string Key { get; set; } = string.Empty;
        public List<string> ReservationKeys { get; set; } = new();
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string ProviderService { get; set; } = string.Empty;
    }

    /// <summary>Ligne de l'historique des SMS et mails. CreatedAtUtc est au format ISO 8601 (« O »).</summary>
    public sealed class HistoryRecord
    {
        public string Key { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CreatedAtUtc { get; set; } = string.Empty;
        public long? ProviderId { get; set; }
        public string ProviderService { get; set; } = string.Empty;
        /// <summary>Lien « Confirmer / Annuler » : vide si le message n'en a pas, « Attente », « Confirme » ou « Annule ».</summary>
        public string ReplyChoice { get; set; } = string.Empty;
        public string ReplyAtUtc { get; set; } = string.Empty;
        /// <summary>Heure locale du rendez-vous du lien (yyyy-MM-ddTHH:mm:ss), pour savoir si le lien a expiré.</summary>
        public string ReplyAppointment { get; set; } = string.Empty;
    }

    public sealed class HistoryReservationKey
    {
        public string ReservationKey { get; set; } = string.Empty;
        public string MessageKey { get; set; } = string.Empty;
    }

    /// <summary>Demande d'un poste : créer le lien « Confirmer / Annuler » d'un rappel avant son envoi.</summary>
    public sealed class ReplyLinkRequest
    {
        public string MessageKey { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        /// <summary>Personnes concernées (affichées dans le mail au cabinet, jamais envoyées au service externe).</summary>
        public string People { get; set; } = string.Empty;
        /// <summary>Heure locale du rendez-vous (heure de Paris).</summary>
        public DateTime AppointmentStart { get; set; }
        public string Cabinet { get; set; } = string.Empty;
        public string CabinetAddress { get; set; } = string.Empty;
        /// <summary>Adresse qui reçoit les réponses des patients, choisie lors de l'envoi.</summary>
        public string ReplyEmail { get; set; } = string.Empty;
    }

    /// <summary>Lien de réponse d'un rappel et dernière réponse du patient.</summary>
    public sealed class ReplyLinkRecord
    {
        public string Token { get; set; } = string.Empty;
        public string MessageKey { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string People { get; set; } = string.Empty;
        public string AppointmentStart { get; set; } = string.Empty;
        public string ReplyEmail { get; set; } = string.Empty;
        public string CreatedAtUtc { get; set; } = string.Empty;
        /// <summary>« Confirme », « Annule » ou vide.</summary>
        public string Choice { get; set; } = string.Empty;
        public string AnsweredAtUtc { get; set; } = string.Empty;
        /// <summary>Le mail au cabinet pour la dernière réponse reste à envoyer.</summary>
        public bool MailPending { get; set; }
    }

    /// <summary>
    /// Historique des SMS et mails et réservations anti-doublon, dans une base SQLite.
    /// Sur le serveur (data\sms-mail-history.db), il est commun à tous les postes : un rappel envoyé
    /// depuis un poste est visible partout et ne peut pas être renvoyé depuis un autre poste.
    /// </summary>
    public sealed class SmsMailHistoryStore
    {
        private readonly string _connectionString;

        public SmsMailHistoryStore(string path)
            => _connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 30 }.ToString();

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS MessageHistory (MessageKey TEXT PRIMARY KEY, Channel TEXT NOT NULL, Contact TEXT NOT NULL, Body TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, ProviderId INTEGER NULL, ProviderService TEXT NOT NULL); CREATE TABLE IF NOT EXISTS MessageReservations (ReservationKey TEXT PRIMARY KEY, MessageKey TEXT NOT NULL); "
                    + "CREATE TABLE IF NOT EXISTS ReplyLinks (Token TEXT PRIMARY KEY, MessageKey TEXT NOT NULL, Channel TEXT NOT NULL, Contact TEXT NOT NULL, People TEXT NOT NULL, AppointmentStart TEXT NOT NULL, ReplyEmail TEXT NOT NULL, CreatedAt TEXT NOT NULL, Choice TEXT NOT NULL DEFAULT '', AnsweredAt TEXT NOT NULL DEFAULT '', MailPending INTEGER NOT NULL DEFAULT 0); "
                    + "CREATE TABLE IF NOT EXISTS ReplyState (Name TEXT PRIMARY KEY, Value TEXT NOT NULL)";
                await command.ExecuteNonQueryAsync();
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }

        // Une réservation unique survit aux plantages et protège contre deux envois simultanés.
        // Un résultat inconnu impose une vérification manuelle.
        public async Task<bool> ReserveAsync(HistoryReservation message)
        {
            await using var connection = await OpenAsync();
            // Transaction immédiate : deux postes ne peuvent pas réserver en même temps des numéros
            // différents pour la même famille. Toute garde est annulée en cas de conflit.
            using var transaction = connection.BeginTransaction(deferred: false);
            var keys = message.ReservationKeys.Append(message.Key).Distinct().ToList();
            foreach (var key in keys)
            {
                using var guard = connection.CreateCommand();
                guard.Transaction = transaction;
                guard.CommandText = "SELECT 1 FROM MessageHistory WHERE MessageKey=$key UNION ALL SELECT 1 FROM MessageReservations WHERE ReservationKey=$key LIMIT 1";
                guard.Parameters.AddWithValue("$key", key);
                if (await guard.ExecuteScalarAsync() != null) return false;
                guard.CommandText = "INSERT INTO MessageReservations (ReservationKey,MessageKey) VALUES ($key,$message)";
                guard.Parameters.AddWithValue("$message", message.Key);
                await guard.ExecuteNonQueryAsync();
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO MessageHistory VALUES ($key, $channel, $contact, $body, $status, $date, NULL, $service)";
            command.Parameters.AddWithValue("$key", message.Key);
            command.Parameters.AddWithValue("$channel", message.Channel);
            command.Parameters.AddWithValue("$contact", message.Contact);
            command.Parameters.AddWithValue("$body", message.Body);
            command.Parameters.AddWithValue("$status", ReservedStatus);
            command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$service", message.ProviderService);
            if (await command.ExecuteNonQueryAsync() != 1) return false;
            transaction.Commit();
            return true;
        }

        public async Task<List<string>> LoadReservedKeysAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MessageKey FROM MessageHistory UNION SELECT ReservationKey FROM MessageReservations";
            using var reader = await command.ExecuteReaderAsync();
            var keys = new List<string>();
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
            return keys;
        }

        public async Task<List<HistoryReservationKey>> LoadReservationsAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ReservationKey, MessageKey FROM MessageReservations";
            using var reader = await command.ExecuteReaderAsync();
            var keys = new List<HistoryReservationKey>();
            while (await reader.ReadAsync()) keys.Add(new HistoryReservationKey { ReservationKey = reader.GetString(0), MessageKey = reader.GetString(1) });
            return keys;
        }

        public async Task UpdateAsync(string key, string status, long? providerId = null)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE MessageHistory SET Status=$status, ProviderId=COALESCE($id, ProviderId) WHERE MessageKey=$key";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$id", (object?)providerId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> ApplyReportAsync(string messageKey, string recipient, string status, string smtpHost)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE MessageHistory SET Status=$status WHERE MessageKey=$key AND Channel='Mail' AND lower(Contact)=$recipient AND ProviderService=$host";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$key", messageKey);
            command.Parameters.AddWithValue("$recipient", recipient.ToLowerInvariant());
            command.Parameters.AddWithValue("$host", smtpHost);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<DateTimeOffset?> EarliestMailAsync(string smtpHost)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MIN(CreatedAt) FROM MessageHistory WHERE Channel='Mail' AND ProviderService=$host";
            command.Parameters.AddWithValue("$host", smtpHost);
            return DateTimeOffset.TryParse(Convert.ToString(await command.ExecuteScalarAsync()), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }

        /// <summary>Ajoute des lignes et réservations venant d'ailleurs (ancien historique d'un poste, archive EyeEmail). Les clés déjà connues sont ignorées.</summary>
        public async Task<int> ImportAsync(IEnumerable<HistoryRecord> records, IEnumerable<HistoryReservationKey>? reservations = null)
        {
            await using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            int count = 0;
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Key) || !DateTimeOffset.TryParse(record.CreatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new InvalidDataException("Ligne d'historique sans clé ou sans date valide.");
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO MessageHistory VALUES ($key,$channel,$contact,$body,$status,$date,$id,$service)";
                insert.Parameters.AddWithValue("$key", record.Key);
                insert.Parameters.AddWithValue("$channel", record.Channel);
                insert.Parameters.AddWithValue("$contact", record.Contact);
                insert.Parameters.AddWithValue("$body", record.Body);
                insert.Parameters.AddWithValue("$status", record.Status);
                insert.Parameters.AddWithValue("$date", date.ToUniversalTime().ToString("O"));
                insert.Parameters.AddWithValue("$id", (object?)record.ProviderId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$service", record.ProviderService);
                count += await insert.ExecuteNonQueryAsync();
            }
            foreach (var reservation in reservations ?? Enumerable.Empty<HistoryReservationKey>())
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO MessageReservations (ReservationKey,MessageKey) VALUES ($key,$message)";
                insert.Parameters.AddWithValue("$key", reservation.ReservationKey);
                insert.Parameters.AddWithValue("$message", reservation.MessageKey);
                await insert.ExecuteNonQueryAsync();
            }
            transaction.Commit();
            return count;
        }

        /// <summary>Un message de l'historique (sans le corps), ou null.</summary>
        public async Task<HistoryRecord?> GetAsync(string key)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MessageKey, Channel, Contact, Status, CreatedAt, ProviderId, ProviderService FROM MessageHistory WHERE MessageKey=$key";
            command.Parameters.AddWithValue("$key", key);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new HistoryRecord
            {
                Key = reader.GetString(0), Channel = reader.GetString(1), Contact = reader.GetString(2), Status = reader.GetString(3),
                CreatedAtUtc = reader.GetString(4), ProviderId = reader.IsDBNull(5) ? null : reader.GetInt64(5), ProviderService = reader.GetString(6)
            };
        }

        /// <summary>Statut posé par <see cref="ReserveAsync"/> : le message est réservé, son envoi n'a pas encore de résultat.</summary>
        public const string ReservedStatus = "Envoi commencé — résultat à vérifier";

        public async Task<List<HistoryRecord>> LoadAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT h.MessageKey, h.Channel, h.Contact, h.Body, h.Status, h.CreatedAt, h.ProviderId, h.ProviderService, "
                + "CASE WHEN r.Token IS NULL THEN '' WHEN r.Choice = '' THEN 'Attente' ELSE r.Choice END, COALESCE(r.AnsweredAt, ''), COALESCE(r.AppointmentStart, '') "
                + "FROM MessageHistory h LEFT JOIN ReplyLinks r ON r.MessageKey = h.MessageKey ORDER BY h.CreatedAt DESC";
            using var reader = await command.ExecuteReaderAsync();
            var records = new List<HistoryRecord>();
            while (await reader.ReadAsync()) records.Add(new HistoryRecord
            {
                Key = reader.GetString(0), Channel = reader.GetString(1), Contact = reader.GetString(2), Body = reader.GetString(3),
                Status = reader.GetString(4), CreatedAtUtc = reader.GetString(5),
                ProviderId = reader.IsDBNull(6) ? null : reader.GetInt64(6), ProviderService = reader.GetString(7),
                ReplyChoice = reader.GetString(8), ReplyAtUtc = reader.GetString(9), ReplyAppointment = reader.GetString(10)
            });
            return records;
        }

        // ---------- Liens de réponse « Confirmer / Annuler » ----------

        public async Task AddReplyLinkAsync(ReplyLinkRecord link)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ReplyLinks (Token, MessageKey, Channel, Contact, People, AppointmentStart, ReplyEmail, CreatedAt) VALUES ($token,$key,$channel,$contact,$people,$start,$email,$created)";
            command.Parameters.AddWithValue("$token", link.Token);
            command.Parameters.AddWithValue("$key", link.MessageKey);
            command.Parameters.AddWithValue("$channel", link.Channel);
            command.Parameters.AddWithValue("$contact", link.Contact);
            command.Parameters.AddWithValue("$people", link.People);
            command.Parameters.AddWithValue("$start", link.AppointmentStart);
            command.Parameters.AddWithValue("$email", link.ReplyEmail);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Enregistre la réponse d'un patient (dernière réponse du lien, mail au cabinet à envoyer).
        /// Le statut d'envoi du message n'est pas touché : la réponse s'affiche à part dans l'historique,
        /// et une actualisation des statuts OVH ou des retours mail ne peut plus l'effacer.
        /// Retourne false si le jeton est inconnu.
        /// </summary>
        public async Task<bool> RecordReplyAsync(string token, string choice, DateTimeOffset answeredAt)
        {
            await using var connection = await OpenAsync();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE ReplyLinks SET Choice=$choice, AnsweredAt=$at, MailPending=1 WHERE Token=$token";
            update.Parameters.AddWithValue("$choice", choice);
            update.Parameters.AddWithValue("$at", answeredAt.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$token", token);
            return await update.ExecuteNonQueryAsync() > 0;
        }

        public async Task<List<ReplyLinkRecord>> PendingReplyMailsAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Token, MessageKey, Channel, Contact, People, AppointmentStart, ReplyEmail, CreatedAt, Choice, AnsweredAt, MailPending FROM ReplyLinks WHERE MailPending=1 ORDER BY AnsweredAt";
            using var reader = await command.ExecuteReaderAsync();
            var links = new List<ReplyLinkRecord>();
            while (await reader.ReadAsync()) links.Add(new ReplyLinkRecord
            {
                Token = reader.GetString(0), MessageKey = reader.GetString(1), Channel = reader.GetString(2), Contact = reader.GetString(3),
                People = reader.GetString(4), AppointmentStart = reader.GetString(5), ReplyEmail = reader.GetString(6), CreatedAtUtc = reader.GetString(7),
                Choice = reader.GetString(8), AnsweredAtUtc = reader.GetString(9), MailPending = reader.GetInt64(10) == 1
            });
            return links;
        }

        /// <summary>Mail envoyé pour cette réponse ; une réponse arrivée entre-temps garde son mail à envoyer.</summary>
        public async Task MarkReplyMailSentAsync(string token, string answeredAtUtc)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ReplyLinks SET MailPending=0 WHERE Token=$token AND AnsweredAt=$at";
            command.Parameters.AddWithValue("$token", token);
            command.Parameters.AddWithValue("$at", answeredAtUtc);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> GetStateAsync(string name)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM ReplyState WHERE Name=$name";
            command.Parameters.AddWithValue("$name", name);
            return await command.ExecuteScalarAsync() as string;
        }

        public async Task SetStateAsync(string name, string value)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ReplyState (Name, Value) VALUES ($name,$value) ON CONFLICT(Name) DO UPDATE SET Value=$value";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Lit une archive EyeEmail (tables MailLog et SmsOutgoing), sans la modifier.</summary>
        public static async Task<List<HistoryRecord>> ReadEyeEmailAsync(string path)
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await source.OpenAsync();
            var records = new List<HistoryRecord>();
            int sources = 0;
            foreach (var kind in new[] { "Mail", "SMS" })
            {
                var table = kind == "Mail" ? "MailLog" : "SmsOutgoing";
                using var exists = source.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                exists.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) continue;
                sources++;
                using var select = source.CreateCommand();
                select.CommandText = kind == "Mail" ? "SELECT Id, ToAddress, Body, Status, SentAt FROM MailLog" : "SELECT Id, Receiver, Message, Status, CreationDatetime FROM SmsOutgoing";
                using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string Text(int column) => reader.IsDBNull(column) ? string.Empty : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture) ?? string.Empty;
                    var rawDate = Text(4);
                    if (!DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        throw new InvalidDataException("Date invalide dans l'historique EyeEmail (" + table + ", ligne " + Text(0) + ").");
                    records.Add(new HistoryRecord
                    {
                        Key = "eyeemail-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + Text(0) + "|" + Text(1) + "|" + rawDate + "|" + Text(2)))),
                        Channel = kind, Contact = Text(1), Body = Text(2), Status = "[EyeEmail] " + Text(3),
                        CreatedAtUtc = date.ToUniversalTime().ToString("O"), ProviderService = "EyeEmail archive"
                    });
                }
            }
            if (sources == 0) throw new InvalidDataException("Aucune table MailLog ou SmsOutgoing dans cet historique EyeEmail.");
            return records;
        }
    }
}
