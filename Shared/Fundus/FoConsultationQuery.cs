using System;
using System.Collections.Generic;
using System.Linq;

namespace EyeChat.Fundus;

// Jet/ACE compatible SELECT. LEFT JOIN retains visits without a coded/matched act.
public sealed record FoConsultationQuery(string Sql, bool HasActs)
{
    public static FoConsultationQuery Create(FoMapping mapping, IReadOnlyList<FoField> schema,
        bool single = false, bool includeActs = true)
    {
        string? Field(string table, string name) => schema.FirstOrDefault(f =>
            f.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name;
        var tariff = schema.FirstOrDefault(f => f.Table.Equals("Tarif", StringComparison.OrdinalIgnoreCase))?.Table;
        var local = Field(mapping.ConsultationsTable, "Code acte");
        var key = tariff == null ? null : Field(tariff, "Code acte");
        var ccam = tariff == null ? null : Field(tariff, "ACTE");
        bool hasActs = includeActs && local != null && key != null && ccam != null;
        string C(string column) => "c." + Q(column);
        var columns = new[] { mapping.ConsultationId, mapping.ConsultationDate }.Concat(mapping.ObservationFields).Select(C);
        var sql = "SELECT " + string.Join(", ", columns);
        if (hasActs) sql += ", " + C(local!) + ", t." + Q(ccam!);
        sql += " FROM " + Q(mapping.ConsultationsTable) + " AS c";
        if (hasActs) sql += " LEFT JOIN " + Q(tariff!) + " AS t ON " + C(local!) + " = t." + Q(key!);
        sql += " WHERE " + C(mapping.ConsultationPatientId) + " = ?";
        if (single) sql += " AND " + C(mapping.ConsultationId) + " = ?";
        sql += " ORDER BY " + C(mapping.ConsultationDate) + ", " + C(mapping.ConsultationId);
        return new(sql, hasActs);
    }

    private static string Q(string name) => "[" + name.Replace("]", "]]") + "]";
}
