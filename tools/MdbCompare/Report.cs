using System.Text;

namespace MdbCompare;

public static class Report
{
    public static string Label(TableStatus status) => status switch
    {
        TableStatus.Identique => "Identique",
        TableStatus.SeulementDans1 => "Seulement dans la base 1",
        TableStatus.SeulementDans2 => "Seulement dans la base 2",
        _ => "Différente",
    };

    public static string Label(RowChange change) => change switch
    {
        RowChange.SeulementDans1 => "Seulement dans 1",
        RowChange.SeulementDans2 => "Seulement dans 2",
        _ => "Modifiée",
    };

    public static string Summary(IReadOnlyList<TableResult> results)
    {
        int same = results.Count(r => r.Status == TableStatus.Identique);
        int only1 = results.Count(r => r.Status == TableStatus.SeulementDans1);
        int only2 = results.Count(r => r.Status == TableStatus.SeulementDans2);
        int diff = results.Count(r => r.Status == TableStatus.Differente);
        return $"{results.Count} table(s) : {same} identique(s), {diff} différente(s), {only1} seulement dans la base 1, {only2} seulement dans la base 2.";
    }

    public static string ToText(string path1, string path2, IReadOnlyList<TableResult> results, CompareOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Comparaison de bases Access");
        sb.AppendLine($"Date    : {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Base 1  : {path1}");
        sb.AppendLine($"Base 2  : {path2}");
        sb.AppendLine($"Données : {(options.CompareData ? "comparées" : "non comparées (structure seulement)")}"
            + (options.IgnoreCaseAndTrailingSpaces ? ", sans tenir compte des majuscules ni des espaces de fin" : ""));
        sb.AppendLine();
        sb.AppendLine(Summary(results));
        foreach (var r in results.Where(r => r.Status != TableStatus.Identique))
        {
            sb.AppendLine();
            sb.AppendLine($"=== {r.Name} : {Label(r.Status)} ===");
            if (r.Status != TableStatus.Differente) continue;
            sb.AppendLine($"Lignes : {r.Rows1} dans la base 1, {r.Rows2} dans la base 2"
                + (r.ComparedBy.Length > 0 ? $" (comparées par {r.ComparedBy})" : ""));
            foreach (var note in r.SchemaNotes) sb.AppendLine("  Structure : " + note);
            if (r.DifferenceCount > 0)
                sb.AppendLine($"  {r.OnlyIn1} seulement dans 1, {r.OnlyIn2} seulement dans 2, {r.Modified} modifiée(s)");
            foreach (var d in r.Details)
                sb.AppendLine($"  [{Label(d.Kind)}]{(d.Key.Length > 0 ? " clé " + d.Key : "")} : {d.Detail}");
            if (r.DetailsTruncated) sb.AppendLine($"  … liste limitée aux {options.MaxDetailsPerTable} premières différences.");
        }
        return sb.ToString();
    }

    public static string ToCsv(IReadOnlyList<TableResult> results)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Table;Type;Clé;Détail");
        foreach (var r in results)
        {
            if (r.Status is TableStatus.SeulementDans1 or TableStatus.SeulementDans2)
                sb.AppendLine(string.Join(";", Cell(r.Name), Cell(Label(r.Status)), Cell(""), Cell("")));
            foreach (var note in r.SchemaNotes)
                sb.AppendLine(string.Join(";", Cell(r.Name), Cell("Structure"), Cell(""), Cell(note)));
            foreach (var d in r.Details)
                sb.AppendLine(string.Join(";", Cell(r.Name), Cell(Label(d.Kind)), Cell(d.Key), Cell(d.Detail)));
        }
        return sb.ToString();
    }
}
