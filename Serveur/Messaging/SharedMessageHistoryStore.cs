using System.Globalization;
using EyeChat.Messaging;
using Microsoft.Data.Sqlite;

namespace ChatServeur.Messaging;

public sealed class SharedMessageHistoryStore
{
    private readonly string connectionString;
    public SharedMessageHistoryStore(string path) => connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SharedMessageHistory (
                  MessageKey TEXT PRIMARY KEY, Channel TEXT NOT NULL, Contact TEXT NOT NULL,
                  Body TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                  ProviderId INTEGER NULL, ProviderService TEXT NOT NULL, Version INTEGER NOT NULL,
                  Workstation TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS SharedMessageReservations (ReservationKey TEXT PRIMARY KEY, MessageKey TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS SharedMessageUpdates (OperationId TEXT PRIMARY KEY, Applied INTEGER NOT NULL, Payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS SharedMessageIdentity (Id INTEGER PRIMARY KEY CHECK(Id=1), ServerId TEXT NOT NULL);
                INSERT OR IGNORE INTO SharedMessageIdentity VALUES (1, $id);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            await command.ExecuteNonQueryAsync();
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<string> ServerIdAsync()
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServerId FROM SharedMessageIdentity WHERE Id=1";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void Text(string? value, int max, bool empty = false)
    {
        if (value == null || value.Length > max || (!empty && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Données d’historique invalides ou trop volumineuses.");
    }

    private static List<string> Validate(HistoryTransfer transfer, bool import)
    {
        if (transfer?.Entry == null || transfer.ReservationKeys == null || transfer.ReservationKeys.Count > 500)
            throw new ArgumentException("Entrée d’historique invalide.");
        var e = transfer.Entry;
        Text(e.Key, 160); Text(e.Contact, 1024, import); Text(e.Body, 200000, true);
        Text(e.ProviderService, 512, true); Text(e.Status, 2000, true);
        if (e.Channel != "Mail" && e.Channel != "SMS") throw new ArgumentException("Canal d’historique invalide.");
        if (import && !DateTimeOffset.TryParse(e.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Date d’historique invalide.");
        foreach (var key in transfer.ReservationKeys) Text(key, 160);
        return transfer.ReservationKeys.Append(e.Key).Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<bool> ReserveAsync(HistoryTransfer transfer, string workstation, bool import = false)
    {
        var keys = Validate(transfer, import);
        Text(workstation, 256, true);
        await using var connection = await OpenAsync();
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var key in keys)
        {
            using var guard = connection.CreateCommand(); guard.Transaction = transaction;
            guard.CommandText = "SELECT 1 FROM SharedMessageHistory WHERE MessageKey=$key UNION ALL SELECT 1 FROM SharedMessageReservations WHERE ReservationKey=$key LIMIT 1";
            guard.Parameters.AddWithValue("$key", key);
            if (!import && await guard.ExecuteScalarAsync() != null) return false;
            guard.CommandText = "INSERT OR IGNORE INTO SharedMessageReservations VALUES ($key,$message)";
            guard.Parameters.AddWithValue("$message", transfer.Entry.Key);
            await guard.ExecuteNonQueryAsync();
        }
        var e = transfer.Entry;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        // Re-importing an archive never overwrites a current delivery status.
        command.CommandText = "INSERT OR IGNORE INTO SharedMessageHistory VALUES ($key,$channel,$contact,$body,$status,$date,$provider,$service,1,$station)";
        command.Parameters.AddWithValue("$key", e.Key); command.Parameters.AddWithValue("$channel", e.Channel);
        command.Parameters.AddWithValue("$contact", e.Contact); command.Parameters.AddWithValue("$body", e.Body);
        command.Parameters.AddWithValue("$status", import ? e.Status : "Envoi commencé — résultat à vérifier");
        command.Parameters.AddWithValue("$date", (import ? DateTimeOffset.Parse(e.CreatedAt, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$provider", import ? (object?)e.ProviderId ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("$service", e.ProviderService); command.Parameters.AddWithValue("$station", workstation);
        var added = await command.ExecuteNonQueryAsync() == 1;
        transaction.Commit();
        return added;
    }

    public async Task<List<string>> FindReservedAsync(List<string> keys)
    {
        if (keys == null || keys.Count > 500) throw new ArgumentException("Trop de réservations demandées.");
        foreach (var key in keys) Text(key, 160);
        await using var connection = await OpenAsync();
        var found = new List<string>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM SharedMessageHistory WHERE MessageKey=$key UNION ALL SELECT 1 FROM SharedMessageReservations WHERE ReservationKey=$key LIMIT 1";
            command.Parameters.AddWithValue("$key", key);
            if (await command.ExecuteScalarAsync() != null) found.Add(key);
        }
        return found;
    }

    private static HistoryRecord Read(SqliteDataReader reader) => new()
    {
        Key = reader.GetString(0), Channel = reader.GetString(1), Contact = reader.GetString(2), Body = reader.GetString(3),
        Status = reader.GetString(4), CreatedAt = reader.GetString(5), ProviderId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
        ProviderService = reader.GetString(7), Version = reader.GetInt64(8), Workstation = reader.GetString(9)
    };

    public async Task<HistoryRecord?> GetAsync(string key)
    {
        Text(key, 160);
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SharedMessageHistory WHERE MessageKey=$key";
        command.Parameters.AddWithValue("$key", key);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task<HistoryPage> LoadPageAsync(long cursor)
    {
        if (cursor < 0) throw new ArgumentException("Page d’historique invalide.");
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        // Bodies are fetched on demand, keeping each SignalR response bounded.
        command.CommandText = "SELECT MessageKey,Channel,Contact,'',Status,CreatedAt,ProviderId,ProviderService,Version,Workstation,rowid FROM SharedMessageHistory WHERE rowid>$cursor ORDER BY rowid LIMIT 50";
        command.Parameters.AddWithValue("$cursor", cursor);
        using var reader = await command.ExecuteReaderAsync(); var page = new HistoryPage(); long last = 0;
        while (await reader.ReadAsync()) { page.Entries.Add(Read(reader)); last = reader.GetInt64(10); }
        page.NextCursor = page.Entries.Count == 50 ? last : 0;
        return page;
    }

    public async Task<bool> UpdateAsync(HistoryUpdate update)
    {
        if (update == null || !Guid.TryParse(update.OperationId, out _) || update.ExpectedVersion < 1)
            throw new ArgumentException("Mise à jour d’historique invalide.");
        Text(update.Key, 160); Text(update.Status, 2000);
        var payload = System.Text.Json.JsonSerializer.Serialize(update);
        await using var connection = await OpenAsync(); using var transaction = connection.BeginTransaction(deferred: false);
        using var receipt = connection.CreateCommand(); receipt.Transaction = transaction;
        receipt.CommandText = "SELECT Applied,Payload FROM SharedMessageUpdates WHERE OperationId=$id";
        receipt.Parameters.AddWithValue("$id", update.OperationId);
        using (var reader = await receipt.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                if (reader.GetString(1) != payload) throw new ArgumentException("Identifiant de mise à jour déjà utilisé.");
                return reader.GetInt64(0) == 1;
            }
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE SharedMessageHistory SET Status=$status,ProviderId=COALESCE($provider,ProviderId),Version=Version+1 WHERE MessageKey=$key AND Version=$version";
        command.Parameters.AddWithValue("$key", update.Key); command.Parameters.AddWithValue("$status", update.Status);
        command.Parameters.AddWithValue("$provider", (object?)update.ProviderId ?? DBNull.Value); command.Parameters.AddWithValue("$version", update.ExpectedVersion);
        bool applied = await command.ExecuteNonQueryAsync() == 1;
        receipt.CommandText = "INSERT INTO SharedMessageUpdates VALUES ($id,$applied,$payload)";
        receipt.Parameters.AddWithValue("$applied", applied ? 1 : 0); receipt.Parameters.AddWithValue("$payload", payload);
        await receipt.ExecuteNonQueryAsync(); transaction.Commit(); return applied;
    }
}
