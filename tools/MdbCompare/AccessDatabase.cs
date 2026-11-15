using System.Data;
using System.Data.OleDb;
using EyeChat.Data;

namespace MdbCompare;

/// <summary>Lit une base .mdb/.accdb en lecture seule (aucune écriture).</summary>
public static class AccessDatabase
{
    public static DatabaseSnapshot Read(AccessReadOptions options, bool readRows, Action<string>? progress, CancellationToken ct)
    {
        using var connection = ReadOnlyAccess.Open(options);
        var snapshot = new DatabaseSnapshot { Path = options.DatabasePath, Provider = connection.Provider };

        var tableNames = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, null })!.Rows.Cast<DataRow>()
            .Where(r => string.Equals(Convert.ToString(r["TABLE_TYPE"]), "TABLE", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Convert.ToString(r["TABLE_TYPE"]), "LINK", StringComparison.OrdinalIgnoreCase))
            .Select(r => Convert.ToString(r["TABLE_NAME"]) ?? "")
            .Where(n => n.Length > 0 && !n.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("~", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in tableNames)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke($"{System.IO.Path.GetFileName(options.DatabasePath)} : {name}");

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

            var table = new TableData { Name = name, Columns = columns, PrimaryKey = key };
            if (readRows && columns.Count > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT " + string.Join(", ", columns.Select(c => Quote(c.Name))) + " FROM " + Quote(name);
                using var reader = command.ExecuteReader()!;
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var values = new object?[reader.FieldCount];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    table.Rows.Add(values);
                }
                table.RowsLoaded = true;
            }
            snapshot.Tables.Add(table);
        }
        return snapshot;
    }

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
