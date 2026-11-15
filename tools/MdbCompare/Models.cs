namespace MdbCompare;

public sealed record ColumnInfo(string Name, string Type);

/// <summary>Contenu d'une table : colonnes, clé primaire et lignes (valeurs déjà converties en texte comparable).</summary>
public sealed class TableData
{
    public required string Name { get; init; }
    public required List<ColumnInfo> Columns { get; init; }
    public required List<string> PrimaryKey { get; init; }
    public List<object?[]> Rows { get; } = new();
    public bool RowsLoaded { get; set; }
}

public sealed class DatabaseSnapshot
{
    public required string Path { get; init; }
    public required string Provider { get; init; }
    public List<TableData> Tables { get; } = new();
}
