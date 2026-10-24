using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using EyeChat.Messaging;
using Microsoft.Data.Sqlite;

namespace Client.SmsMail.Services;

// Migration receipts and a durable status outbox live beside the unchanged local archive.
internal sealed class LocalHistorySync
{
    private readonly string connectionString;
    public LocalHistorySync(string path) => connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS SharedHistoryImported (ServerId TEXT NOT NULL,MessageKey TEXT NOT NULL,PRIMARY KEY(ServerId,MessageKey)); CREATE TABLE IF NOT EXISTS SharedHistoryOutbox (OperationId TEXT PRIMARY KEY,ServerId TEXT NOT NULL,Payload TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync(); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<List<HistoryTransfer>> UnimportedAsync(string server)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT MessageKey,Channel,Contact,Body,Status,CreatedAt,ProviderId,ProviderService FROM MessageHistory h WHERE NOT EXISTS (SELECT 1 FROM SharedHistoryImported i WHERE i.ServerId=$server AND i.MessageKey=h.MessageKey) ORDER BY h.rowid LIMIT 50";
        command.Parameters.AddWithValue("$server", server); var entries = new List<HistoryTransfer>();
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) entries.Add(new HistoryTransfer { Entry = new HistoryRecord
            {
                Key = reader.GetString(0), Channel = reader.GetString(1), Contact = reader.GetString(2), Body = reader.GetString(3),
                Status = reader.GetString(4), CreatedAt = reader.GetString(5), ProviderId = reader.IsDBNull(6) ? null : reader.GetInt64(6), ProviderService = reader.GetString(7)
            }});
        foreach (var entry in entries)
        {
            using var keys = connection.CreateCommand();
            keys.CommandText = "SELECT ReservationKey FROM MessageReservations WHERE MessageKey=$key";
            keys.Parameters.AddWithValue("$key", entry.Entry.Key);
            using var reader = await keys.ExecuteReaderAsync();
            while (await reader.ReadAsync()) entry.ReservationKeys.Add(reader.GetString(0));
        }
        return entries;
    }

    public async Task MarkImportedAsync(string server, string key)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO SharedHistoryImported VALUES ($server,$key)";
        command.Parameters.AddWithValue("$server", server); command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync();
    }

    public async Task EnqueueAsync(string server, HistoryUpdate update)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SharedHistoryOutbox VALUES ($id,$server,$payload)";
        command.Parameters.AddWithValue("$id", update.OperationId); command.Parameters.AddWithValue("$server", server);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(update)); await command.ExecuteNonQueryAsync();
    }

    public async Task<List<HistoryUpdate>> PendingAsync(string server)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM SharedHistoryOutbox WHERE ServerId=$server ORDER BY rowid";
        command.Parameters.AddWithValue("$server", server); using var reader = await command.ExecuteReaderAsync();
        var updates = new List<HistoryUpdate>();
        while (await reader.ReadAsync()) updates.Add(JsonSerializer.Deserialize<HistoryUpdate>(reader.GetString(0)) ?? throw new InvalidOperationException("Statut local en attente illisible."));
        return updates;
    }

    public async Task AcknowledgeAsync(string operation)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SharedHistoryOutbox WHERE OperationId=$id";
        command.Parameters.AddWithValue("$id", operation); await command.ExecuteNonQueryAsync();
    }
}
