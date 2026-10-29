using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public static class AppointmentLoader
    {
        public static Task<List<RdvInfo>> LoadAsync(AppointmentSearchConfig config, MachineConfig machine, DateTime day) => Task.Run(() =>
        {
            using var connection = Open(config, machine);
            using var command = connection.CreateCommand();
            // Field names are checked against the real schema: a wrong name would otherwise surface as
            // Access' "Aucune valeur donnée pour un ou plusieurs des paramètres requis".
            command.CommandText = AppointmentQuery.BuildDay(config.TableName, config.DateColumn, config.TimeColumn,
                Tables(connection), table => Columns(connection, table));
            command.Parameters.Add("?", OleDbType.Date).Value = day.Date;
            command.Parameters.Add("?", OleDbType.Date).Value = day.Date.AddDays(1);
            using var reader = command.ExecuteReader();
            var result = new List<RdvInfo>();
            if (reader != null)
            {
                var fields = EyeEmailRecordMapper.BuildOrdinalLookup(reader);
                while (reader.Read())
                {
                    var row = EyeEmailRecordMapper.MapAppointment(reader, fields, day);
                    if (row != null && !row.IsCancelled && !string.Equals(row.Nom, "ABSENCE", StringComparison.OrdinalIgnoreCase)
                        && row.Nom != "//////////////////////") result.Add(row);
                }
            }
            return EyeEmailRecordMapper.ConsolidateDuplicateAppointments(result);
        });

        public static Task<Dictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>>> LoadHistoryAsync(
            AppointmentSearchConfig config, MachineConfig machine, DateTime today) => Task.Run(() =>
        {
            using var connection = Open(config, machine);
            using var command = connection.CreateCommand();
            command.CommandText = AppointmentQuery.BuildHistory(config.TableName, config.DateColumn,
                Tables(connection), table => Columns(connection, table));
            command.Parameters.Add("?", OleDbType.Date).Value = today.Date.AddYears(-8);
            command.Parameters.Add("?", OleDbType.Date).Value = new DateTime(today.Year, 1, 1);
            using var reader = command.ExecuteReader();
            var result = new Dictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>>();
            if (reader == null) return result;
            var fields = EyeEmailRecordMapper.BuildOrdinalLookup(reader);
            while (reader.Read())
            {
                int id = EyeEmailRecordMapper.GetInt(reader, fields, "Code Patient", "CodePatient", "Id");
                var date = EyeEmailRecordMapper.GetDateTime(reader, fields, config.DateColumn);
                if (id <= 0 || !date.HasValue || EyeEmailRecordMapper.GetBoolean(reader, fields, false, "Annulé", "Annule", "Cancelled")) continue;
                if (!result.TryGetValue(id, out var list)) result[id] = list = new();
                list.Add((date.Value, EyeEmailRecordMapper.GetDateTime(reader, fields, config.TimeColumn),
                    EyeEmailRecordMapper.GetDateTime(reader, fields, "Arrivé à", "Arrivee", "HeureArrivee")));
            }
            return result;
        });

        private static IReadOnlyCollection<string> Tables(OleDbConnection connection) =>
            connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" })!
                .Rows.Cast<DataRow>().Select(r => (string)r["TABLE_NAME"]).ToList();

        private static IReadOnlyCollection<string> Columns(OleDbConnection connection, string table) =>
            connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!
                .Rows.Cast<DataRow>().Select(r => (string)r["COLUMN_NAME"]).ToList();
        private static OleDbConnection Open(AppointmentSearchConfig config, MachineConfig machine) =>
            EyeChat.Data.ReadOnlyAccess.Open(new(config.DatabasePath, machine.AccessOleDbProvider,
                machine.AccessWorkgroupPath, machine.AccessUserName, machine.AccessPassword));
    }
}
