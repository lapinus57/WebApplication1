using System.Data;
using System.Data.OleDb;
using EyeChat.Data;

namespace MdbCompare;

/// <summary>Base .mdb/.accdb ouverte en lecture seule (aucune écriture).</summary>
public sealed class AccessDatabase : IDatabase, IDisposable
{
    private readonly OleDbConnection connection;
    private readonly List<TableSchema> tables = new();

    public string Path { get; }
    public string Provider => connection.Provider;
    public IReadOnlyList<TableSchema> Tables => tables;

    private AccessDatabase(OleDbConnection connection, string path)
    {
        this.connection = connection;
        Path = path;
    }

    public static AccessDatabase Open(AccessReadOptions options)
    {
        var database = new AccessDatabase(ReadOnlyAccess.Open(options), options.DatabasePath);
        try { database.ReadSchema(); }
        catch { database.Dispose(); throw; }
        return database;
    }

    private void ReadSchema()
    {
        var names = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, null })!.Rows.Cast<DataRow>()
            .Where(r => string.Equals(Convert.ToString(r["TABLE_TYPE"]), "TABLE", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Convert.ToString(r["TABLE_TYPE"]), "LINK", StringComparison.OrdinalIgnoreCase))
            .Select(r => Convert.ToString(r["TABLE_NAME"]) ?? "")
            .Where(n => n.Length > 0 && !n.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("~", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in names)
        {
            var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, name, null })!.Rows.Cast<DataRow>()
                .OrderBy(r => Convert.ToInt64(r["ORDINAL_POSITION"]))
                .Select(r => new ColumnInfo(Convert.ToString(r["COLUMN_NAME"]) ?? "", DescribeType(r)))
                .ToList();

            var key = new List<string>();
            try
            {
                key = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, new object?[] { null, null, name })!.Rows.Cast<DataRow>()
                    .OrderBy(r => Convert.ToInt64(r["ORDINAL"]))
                    .Select(r => Convert.ToString(r["COLUMN_NAME"]) ?? "")
                    .Where(n => n.Length > 0).ToList();
            }
            catch (OleDbException) { /* Table liée ou pilote sans clés : comparaison ligne entière. */ }

            tables.Add(new TableSchema { Name = name, Columns = columns, PrimaryKey = key });
        }
    }

    public int CountRows(TableSchema table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + Quote(table.Name);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IEnumerable<object?[]> ReadRows(TableSchema table, IReadOnlyList<string> columns, CancellationToken ct)
    {
        if (columns.Count == 0) yield break;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + string.Join(", ", columns.Select(Quote)) + " FROM " + Quote(table.Name);
        using var reader = command.ExecuteReader()!;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var values = new object?[reader.FieldCount];
            for (int i = 0; i < values.Length; i++)
            {
                var value = reader.GetValue(i);
                values[i] = value is DBNull ? null : value;
            }
            yield return values;
        }
    }

    public void Dispose() => connection.Dispose();

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static string DescribeType(DataRow column)
    {
        var type = column["DATA_TYPE"] is int code ? ((OleDbType)code).ToString() : Convert.ToString(column["DATA_TYPE"]) ?? "?";
        if (column["CHARACTER_MAXIMUM_LENGTH"] is not DBNull and not null)
        {
            var length = Convert.ToInt64(column["CHARACTER_MAXIMUM_LENGTH"]);
            if (length > 0 && length < 1_000_000) type += $"({length})";
        }
        return type;
    }
}
