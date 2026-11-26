using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace EyeChat.Families
{
    public sealed class FamilyPipelineOptions
    {
        public string Table { get; set; } = "AG_rdv";
        /// <summary>Table des patients (date de naissance) si l'agenda n'en a pas.</summary>
        public string? PatientsTable { get; set; }
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<VitaleRow>? Vitale { get; set; }
        public IReadOnlyList<PaymentRow>? Payments { get; set; }
        public IReadOnlyList<FamilyDecision>? Decisions { get; set; }
    }

    /// <summary>
    /// Calcul complet des familles : tout l'agenda jour après jour, puis règlements, carte Vitale et décisions manuelles.
    /// Le même code sert à l'outil FamilyScan (au cabinet) et au recalcul nocturne du serveur. Lecture seule.
    /// </summary>
    public static class FamilyPipeline
    {
        public static ScanResult Run(OleDbConnection connection, FamilyPipelineOptions options, Action<string>? log = null, CancellationToken ct = default)
        {
            var table = AgendaSchema.FindTable(connection, options.Table);
            var columns = AgendaSchema.Columns(connection, table);
            var map = AgendaSchema.Map(columns, options.Overrides);
            log?.Invoke($"Table : {table} ({columns.Count} colonnes), pilote {connection.Provider}");
            foreach (var (role, column) in map.OrderBy(m => m.Key)) log?.Invoke($"  {role,-10} → {column ?? "(absente)"}");
            foreach (var required in new[] { "patient", "date" })
                if (map[required] == null)
                    throw new InvalidOperationException($"Colonne « {required} » introuvable dans {table}. Colonnes : {string.Join(", ", columns)}");

            var (first, last) = AgendaSchema.DateRange(connection, table, map["date"]!);
            log?.Invoke($"Rendez-vous du {first:dd/MM/yyyy} au {last:dd/MM/yyyy}.");
            var scanner = new FamilyScanner();
            var clock = Stopwatch.StartNew();
            for (var year = first.Year; year <= last.Year; year++)
            {
                int count = 0;
                using var command = connection.CreateCommand();
                var order = map["heure"] is { } h ? $"{AgendaSchema.Q(map["date"]!)}, {AgendaSchema.Q(h)}" : AgendaSchema.Q(map["date"]!);
                command.CommandText = $"SELECT * FROM {AgendaSchema.Q(table)} WHERE {AgendaSchema.Q(map["date"]!)} >= ? AND {AgendaSchema.Q(map["date"]!)} < ? ORDER BY {order}";
                command.Parameters.Add("?", OleDbType.Date).Value = new DateTime(year, 1, 1);
                command.Parameters.Add("?", OleDbType.Date).Value = new DateTime(year + 1, 1, 1);
                using var reader = command.ExecuteReader()!;
                var read = new AgendaRowReader(reader, map);
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var row = read.Row();
                    if (row == null) continue;
                    scanner.Add(row);
                    count++;
                }
                log?.Invoke($"  {year} : {count,7:N0} rendez-vous lus ({clock.Elapsed:mm\\:ss})");
            }

            if (options.Payments is { Count: > 0 } payments)
            {
                var stats = scanner.AddPayments(payments);
                log?.Invoke($"Règlements : {stats.Lines:N0} factures, {stats.CardInvoices:N0} rattachées à un patient par la carte.");
            }

            VitaleMatch? vitaleMatch = null;
            if (options.Vitale is { Count: > 0 } vitale)
            {
                var identitiesTable = options.PatientsTable ?? (map["naissance"] != null ? table : AgendaSchema.FindTable(connection, "Patients"));
                var identitiesMap = identitiesTable == table ? map : AgendaSchema.Map(AgendaSchema.Columns(connection, identitiesTable), options.Overrides);
                var identities = AgendaSchema.ReadIdentities(connection, identitiesTable, identitiesMap);
                vitaleMatch = VitaleLinks.Match(vitale, identities, scanner.Visited);
                var declared = VitaleLinks.Links(vitale, vitaleMatch, scanner.Today);
                scanner.AddDeclared(declared);
                log?.Invoke($"Carte Vitale : {vitaleMatch.VitalePatients:N0} patients, {vitaleMatch.Matched:N0} rapprochés, {declared.Count:N0} liens déclarés.");
            }

            if (options.Decisions is { Count: > 0 } decisions) scanner.AddDecisions(decisions);
            ct.ThrowIfCancellationRequested();
            log?.Invoke("Calcul des liens et des familles…");
            var result = scanner.Finish();
            result.Vitale = vitaleMatch;
            return result;
        }
    }
}
