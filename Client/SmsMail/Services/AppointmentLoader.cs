using System;
using System.Collections.Generic;
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
            command.CommandText = $@"SELECT r.*, r.[Code Patient] AS Code, r.{Quote(config.DateColumn)} AS EyeChatDate,
                r.{Quote(config.TimeColumn)} AS EyeChatTime, p.Email AS PatientEmail
                FROM {Quote(config.TableName)} r INNER JOIN patients p ON r.[Code Patient] = p.[Code Patient]
                WHERE r.{Quote(config.DateColumn)} >= ? AND r.{Quote(config.DateColumn)} < ? ORDER BY r.{Quote(config.TimeColumn)}";
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
            command.CommandText = $"SELECT r.* FROM {Quote(config.TableName)} r WHERE r.{Quote(config.DateColumn)} >= ? AND r.{Quote(config.DateColumn)} < ?";
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

        private static string Quote(string value) => "[" + value.Replace("]", "]]") + "]";
        private static OleDbConnection Open(AppointmentSearchConfig config, MachineConfig machine) =>
            EyeChat.Data.ReadOnlyAccess.Open(new(config.DatabasePath, machine.AccessOleDbProvider,
                machine.AccessWorkgroupPath, machine.AccessUserName, machine.AccessPassword));
    }
}
