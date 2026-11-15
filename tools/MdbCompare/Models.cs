namespace MdbCompare;

public sealed record ColumnInfo(string Name, string Type);

/// <summary>Structure d'une table : colonnes et clé primaire (sans les lignes).</summary>
public sealed class TableSchema
{
    public required string Name { get; init; }
    public required List<ColumnInfo> Columns { get; init; }
    public required List<string> PrimaryKey { get; init; }
}

/// <summary>
/// Base ouverte. Les lignes sont lues en flux, table par table, et jamais gardées toutes en mémoire :
/// une grosse base (champs Mémo, images) ne doit pas épuiser la mémoire du programme 32 bits.
/// </summary>
public interface IDatabase
{
    string Path { get; }
    IReadOnlyList<TableSchema> Tables { get; }
    int CountRows(TableSchema table);
    /// <summary>Lit les colonnes demandées (dans cet ordre), une ligne à la fois.</summary>
    IEnumerable<object?[]> ReadRows(TableSchema table, IReadOnlyList<string> columns, CancellationToken ct);
}
