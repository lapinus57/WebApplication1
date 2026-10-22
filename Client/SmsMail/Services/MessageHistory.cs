using System;
using System.Collections.Generic;
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
    }

    public sealed class MessageHistory
    {
        private readonly string _connectionString;
        public MessageHistory(string path) => _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS MessageHistory (MessageKey TEXT PRIMARY KEY, Channel TEXT NOT NULL, Contact TEXT NOT NULL, Body TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, ProviderId INTEGER NULL, ProviderService TEXT NOT NULL)";
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
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO MessageHistory VALUES ($key, $channel, $contact, $body, $status, $date, NULL, $service)";
            command.Parameters.AddWithValue("$key", message.Key);
            command.Parameters.AddWithValue("$channel", message.Channel);
            command.Parameters.AddWithValue("$contact", message.Contact);
            command.Parameters.AddWithValue("$body", message.Body);
            command.Parameters.AddWithValue("$status", "Envoi commencé — résultat à vérifier");
            command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$service", message.ProviderService);
            return await command.ExecuteNonQueryAsync() == 1;
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
