using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Client.SmsMail.Services
{
    public sealed class MessageHistoryEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public long? ProviderId { get; set; }
        public string ProviderService { get; set; } = string.Empty;
        public string Workstation { get; set; } = string.Empty;
    }

    public sealed class MessageHistory
    {
        private readonly string _connectionString;
        public MessageHistory(string path) => _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        public async Task InitializeAsync() { await using var connection = await OpenAsync(); }

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS MessageHistory (MessageKey TEXT PRIMARY KEY, Channel TEXT NOT NULL, Contact TEXT NOT NULL, Body TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, ProviderId INTEGER NULL, ProviderService TEXT NOT NULL); CREATE TABLE IF NOT EXISTS MessageReservations (ReservationKey TEXT PRIMARY KEY, MessageKey TEXT NOT NULL)";
                await command.ExecuteNonQueryAsync();
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }

        // A unique reservation survives crashes and also protects against two open
        // clients on this workstation. Unknown outcomes require manual checking.
        public async Task<bool> ReserveAsync(PreparedMessage message)
        {
            await using var connection = await OpenAsync();
            // Immediate transaction: two processes cannot reserve different phones
            // for the same family concurrently. Roll back every guard on a conflict.
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
            command.Parameters.AddWithValue("$status", "Envoi commencé — résultat à vérifier");
            command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$service", message.ProviderService);
            if (await command.ExecuteNonQueryAsync() != 1) return false;
            transaction.Commit();
            return true;
        }

        public async Task<HashSet<string>> LoadReservedKeysAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MessageKey FROM MessageHistory UNION SELECT ReservationKey FROM MessageReservations";
            using var reader = await command.ExecuteReaderAsync();
            var keys = new HashSet<string>();
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
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

        public async Task<int> ApplyReportAsync(MailDeliveryReport report, string smtpHost)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE MessageHistory SET Status=$status WHERE MessageKey=$key AND Channel='Mail' AND lower(Contact)=$recipient AND ProviderService=$host";
            command.Parameters.AddWithValue("$status", report.Status);
            command.Parameters.AddWithValue("$key", report.MessageKey);
            command.Parameters.AddWithValue("$recipient", report.Recipient.ToLowerInvariant());
            command.Parameters.AddWithValue("$host", smtpHost);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<DateTimeOffset?> EarliestMailAsync(string smtpHost)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MIN(CreatedAt) FROM MessageHistory WHERE Channel='Mail' AND ProviderService=$host";
            command.Parameters.AddWithValue("$host", smtpHost);
            return DateTimeOffset.TryParse(Convert.ToString(await command.ExecuteScalarAsync()), out var date) ? date : null;
        }

        public async Task<int> ImportEyeEmailAsync(string path)
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await source.OpenAsync();
            await using var destination = await OpenAsync();
            using var transaction = destination.BeginTransaction();
            int count = 0, sources = 0;
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
                    var key = "eyeemail-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + Text(0) + "|" + Text(1) + "|" + rawDate + "|" + Text(2))));
                    using var insert = destination.CreateCommand(); insert.Transaction = transaction;
                    insert.CommandText = "INSERT OR IGNORE INTO MessageHistory VALUES ($key,$kind,$contact,$body,$status,$date,NULL,'EyeEmail archive')";
                    insert.Parameters.AddWithValue("$key", key); insert.Parameters.AddWithValue("$kind", kind);
                    insert.Parameters.AddWithValue("$contact", Text(1)); insert.Parameters.AddWithValue("$body", Text(2));
                    insert.Parameters.AddWithValue("$status", "[EyeEmail] " + Text(3)); insert.Parameters.AddWithValue("$date", date.ToUniversalTime().ToString("O"));
                    count += await insert.ExecuteNonQueryAsync();
                }
            }
            if (sources == 0) throw new InvalidDataException("Aucune table MailLog ou SmsOutgoing dans cet historique EyeEmail.");
            transaction.Commit();
            return count;
        }

        public async Task<List<MessageHistoryEntry>> LoadAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MessageKey, Channel, Contact, Body, Status, CreatedAt, ProviderId, ProviderService FROM MessageHistory ORDER BY CreatedAt DESC";
            using var reader = await command.ExecuteReaderAsync();
            var entries = new List<MessageHistoryEntry>();
            while (await reader.ReadAsync()) entries.Add(new MessageHistoryEntry
            {
                Key = reader.GetString(0), Channel = reader.GetString(1), Contact = reader.GetString(2), Body = reader.GetString(3),
                Status = reader.GetString(4), CreatedAt = DateTimeOffset.Parse(reader.GetString(5)).ToLocalTime().ToString("g"),
                ProviderId = reader.IsDBNull(6) ? null : reader.GetInt64(6), ProviderService = reader.GetString(7)
            });
            return entries;
        }
    }
}

