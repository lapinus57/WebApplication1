using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;

namespace Client.Services
{
    public class AppointmentSearchService
    {
        private readonly AppointmentSearchConfig _config;
        private readonly MachineConfig _machineConfig;

        public AppointmentSearchService(AppointmentSearchConfig config, MachineConfig machineConfig)
        {
            _config = config;
            _machineConfig = machineConfig;
        }

        public async Task<IReadOnlyList<AppointmentSlotInfo>> FindAvailableSlotsAsync(
            DateTime anchorDate,
            SearchMode mode,
            bool canClimb,
            bool isFo,
            bool allowOverload,
            CancellationToken cancellationToken)
        {
            if (!_config.IsValid())
            {
                throw new InvalidOperationException("La configuration de recherche des rendez-vous est incomplète.");
            }

            var (startDate, endDate) = GetSearchRange(anchorDate, mode);
            var existingAppointments = await LoadExistingAppointmentsAsync(startDate, endDate, cancellationToken).ConfigureAwait(false);
            var excludedDates = BuildExcludedDateSet(existingAppointments.Where(e => e.IsExcludedDayMarker));
            var groupedAppointments = GroupAppointments(existingAppointments.Where(e => !e.IsExcludedDayMarker));

            var slots = new List<AppointmentSlotInfo>();
            var baseLimit = Math.Max(1, _config.MaxAppointmentsPerSlot);
            var overloadLimit = allowOverload
                ? baseLimit + Math.Max(1, _config.OverloadExtraAppointments)
                : baseLimit;

            var slotLength = TimeSpan.FromMinutes(Math.Max(1, _config.SlotLengthMinutes));
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                if (excludedDates.Contains(date))
                    continue;

                foreach (var slotTime in EnumerateDailySlots(slotLength))
                {
                    var slotDateTime = date + slotTime;
                    if (slotDateTime < startDate || slotDateTime > endDate)
                        continue;

                    if (!IsWithinWorkingHours(slotTime, isFo))
                        continue;

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();

                    groupedAppointments.TryGetValue(slotDateTime, out var colorsForSlot);
                    var colorList = colorsForSlot ?? new List<int>();
                    var currentCount = colorList.Count;
                    var redCount = colorList.Count(color => color == 255);
                    var effectiveCount = Math.Max(0, currentCount - redCount);

                    if (!IsSlotAllowed(colorList, effectiveCount, baseLimit, overloadLimit, canClimb))
                        continue;

                    var redRelaxation = currentCount >= baseLimit && redCount > 0;
                    var overloadUsed = allowOverload && effectiveCount >= baseLimit;

                    slots.Add(new AppointmentSlotInfo
                    {
                        SlotStart = slotDateTime,
                        ExistingAppointments = currentCount,
                        ColorCounts = colorList
                            .GroupBy(c => c)
                            .ToDictionary(g => g.Key, g => g.Count()),
                        UsesOverloadCapacity = overloadUsed,
                        RedRelaxationApplied = redRelaxation
                    });
                }
            }

            return slots
                .OrderBy(s => s.SlotStart)
                .ToList();
        }

        private (DateTime start, DateTime end) GetSearchRange(DateTime anchor, SearchMode mode)
        {
            var start = anchor;
            var end = anchor;
            if (mode == SearchMode.Around)
            {
                start = anchor.Date.AddDays(-7);
                end = anchor.Date.AddDays(14).AddHours(23).AddMinutes(59);
            }
            else
            {
                start = anchor.Date;
                end = anchor.Date.AddDays(21).AddHours(23).AddMinutes(59);
            }

            return (start, end);
        }

        private async Task<List<AppointmentEntry>> LoadExistingAppointmentsAsync(
            DateTime start,
            DateTime end,
            CancellationToken cancellationToken)
        {
            var entries = new List<AppointmentEntry>();
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT [{_config.DateColumn}], [{_config.TimeColumn}], [{_config.ColorColumn}] FROM [{_config.TableName}] WHERE [{_config.DateColumn}] BETWEEN ? AND ?";
            command.Parameters.Add(new OleDbParameter("@start", OleDbType.Date) { Value = start.Date });
            command.Parameters.Add(new OleDbParameter("@end", OleDbType.Date) { Value = end.Date });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var dateValue = reader.GetValue(0);
                var timeValue = reader.GetValue(1);
                var colorValue = reader.GetValue(2);
                var isExcludedMarker = IsExcludedDayMarker(colorValue);

                if (!TryGetDate(dateValue, out var date))
                    continue;
                if (!TryGetTime(timeValue, out var time))
                    continue;

                var slotDateTime = date.Date + time;
                slotDateTime = NormalizeToSlot(slotDateTime);
                var colorCode = ConvertToColor(colorValue);
                entries.Add(new AppointmentEntry(slotDateTime, colorCode, isExcludedMarker));
            }

            return entries;
        }

        private async Task<OleDbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            var triedProviders = new List<string>();
            Exception? lastProviderError = null;

            foreach (var provider in EnumerateCandidateProviders())
            {
                triedProviders.Add(provider);
                var connectionString = BuildConnectionString(provider);
                var connection = new OleDbConnection(connectionString);

                try
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return connection;
                }
                catch (InvalidOperationException ex) when (IsProviderNotRegistered(ex))
                {
                    lastProviderError = ex;
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            var message = "Aucun fournisseur OLE DB Access compatible n'a été trouvé. Installez Microsoft Access Database Engine 2016 (ACE OLE DB 16.0) ou configurez un fournisseur valide dans les paramètres de machine.";
            if (triedProviders.Count > 0)
            {
                message += $" Fournisseurs testés : {string.Join(", ", triedProviders)}.";
            }

            if (Environment.Is64BitProcess && IsMdbDatabase())
            {
                message += " La base de données cible est un fichier .mdb. Le fournisseur Microsoft Jet intégré à Windows n'est disponible que pour les applications 32 bits : lancez la version x86 du client pour éviter d'installer un fournisseur supplémentaire.";
            }

            throw new InvalidOperationException(message, lastProviderError);
        }

        private IEnumerable<string> EnumerateCandidateProviders()
        {
            var configured = (_machineConfig.AccessOleDbProvider ?? string.Empty).Trim();
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (yielded.Add(configured))
                    yield return configured;
            }

            foreach (var fallback in EnumerateFallbackProviders())
            {
                if (yielded.Add(fallback))
                    yield return fallback;
            }
        }

        private IEnumerable<string> EnumerateFallbackProviders()
        {
            if (IsMdbDatabase())
            {
                yield return "Microsoft.Jet.OLEDB.4.0";
            }

            yield return "Microsoft.ACE.OLEDB.16.0";
            yield return "Microsoft.ACE.OLEDB.12.0";

            if (!IsMdbDatabase())
            {
                yield return "Microsoft.Jet.OLEDB.4.0";
            }
        }

        private bool IsMdbDatabase()
        {
            var extension = Path.GetExtension(_config.DatabasePath);
            return !string.IsNullOrWhiteSpace(extension)
                   && extension.Equals(".mdb", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProviderNotRegistered(Exception exception)
        {
            while (exception != null)
            {
                if (exception is InvalidOperationException &&
                    exception.Message.IndexOf("is not registered", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        private string BuildConnectionString(string provider)
        {
            var builder = new OleDbConnectionStringBuilder
            {
                Provider = provider,
                ["Data Source"] = _config.DatabasePath,
                ["Persist Security Info"] = true
            };

            if (!string.IsNullOrWhiteSpace(_machineConfig.AccessWorkgroupPath))
            {
                builder["Jet OLEDB:System Database"] = _machineConfig.AccessWorkgroupPath;
            }

            if (!string.IsNullOrWhiteSpace(_machineConfig.AccessUserName))
            {
                builder["User ID"] = _machineConfig.AccessUserName;
            }

            if (!string.IsNullOrWhiteSpace(_machineConfig.AccessPassword))
            {
                builder["Password"] = _machineConfig.AccessPassword;
            }

            return builder.ConnectionString;
        }

        private Dictionary<DateTime, List<int>> GroupAppointments(IEnumerable<AppointmentEntry> entries)
        {
            var dict = new Dictionary<DateTime, List<int>>();
            foreach (var entry in entries)
            {
                if (!dict.TryGetValue(entry.SlotStart, out var list))
                {
                    list = new List<int>();
                    dict[entry.SlotStart] = list;
                }
                list.Add(entry.ColorCode);
            }
            return dict;
        }

        private HashSet<DateTime> BuildExcludedDateSet(IEnumerable<AppointmentEntry> excludedEntries)
        {
            var releaseMonths = Math.Max(0, _config.ExcludedDayReleaseMonths);
            var releaseThreshold = releaseMonths > 0 ? DateTime.Today.AddMonths(releaseMonths) : (DateTime?)null;

            var dayStates = new Dictionary<DateTime, (bool Morning, bool Afternoon)>();

            foreach (var entry in excludedEntries)
            {
                var date = entry.SlotStart.Date;
                if (!dayStates.TryGetValue(date, out var state))
                {
                    state = (false, false);
                }

                var time = entry.SlotStart.TimeOfDay;
                if (time < _config.AfternoonStart)
                    state.Morning = true;
                if (time >= _config.AfternoonStart)
                    state.Afternoon = true;

                dayStates[date] = state;
            }

            var excludedDates = new HashSet<DateTime>();
            foreach (var (date, state) in dayStates)
            {
                if (state.Morning && state.Afternoon)
                {
                    excludedDates.Add(date);
                    continue;
                }

                if (releaseThreshold.HasValue && date > releaseThreshold.Value && (state.Morning || state.Afternoon))
                    excludedDates.Add(date);
            }

            return excludedDates;
        }

        private IEnumerable<TimeSpan> EnumerateDailySlots(TimeSpan slotLength)
        {
            foreach (var slot in EnumerateRange(_config.MorningStart, _config.MorningEnd, slotLength))
                yield return slot;
            foreach (var slot in EnumerateRange(_config.AfternoonStart, _config.AfternoonEnd, slotLength))
                yield return slot;
        }

        private static IEnumerable<TimeSpan> EnumerateRange(TimeSpan start, TimeSpan end, TimeSpan step)
        {
            if (step <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(step));

            for (var current = start; current < end; current += step)
                yield return current;
        }

        private bool IsWithinWorkingHours(TimeSpan time, bool isFo)
        {
            var inMorning = time >= _config.MorningStart && time < _config.MorningEnd;
            var inAfternoon = time >= _config.AfternoonStart && time < _config.AfternoonEnd;
            if (!inMorning && !inAfternoon)
                return false;

            if (isFo)
            {
                if (inMorning && time > _config.FoMorningLimit)
                    return false;
                if (inAfternoon && time > _config.FoAfternoonLimit)
                    return false;
            }

            return true;
        }

        private bool IsSlotAllowed(
            IReadOnlyCollection<int> colors,
            int effectiveCount,
            int baseLimit,
            int overloadLimit,
            bool canClimb)
        {
            if (!canClimb)
            {
                var pinkCount = colors.Count(c => c == 16711935);
                if (pinkCount >= 1)
                    return false;
            }

            var overloadAllowed = overloadLimit > baseLimit;
            if (effectiveCount < baseLimit)
                return true;

            var hasRed = colors.Contains(255);

            if (effectiveCount == baseLimit)
            {
                if (hasRed)
                    return true;
                if (overloadAllowed)
                    return true;
                return false;
            }

            if (!overloadAllowed)
                return false;

            if (effectiveCount < overloadLimit)
                return true;

            if (effectiveCount == overloadLimit && hasRed)
                return true;

            return false;
        }

        private DateTime NormalizeToSlot(DateTime value)
        {
            var slotMinutes = Math.Max(1, _config.SlotLengthMinutes);
            var minutes = value.Minute - (value.Minute % slotMinutes);
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, minutes, 0);
        }

        private static bool TryGetDate(object? value, out DateTime date)
        {
            if (value is DateTime dt)
            {
                date = dt;
                return true;
            }

            if (value is string s && DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                date = dt;
                return true;
            }

            date = default;
            return false;
        }

        private static bool TryGetTime(object? value, out TimeSpan time)
        {
            if (value is TimeSpan ts)
            {
                time = ts;
                return true;
            }

            if (value is DateTime dt)
            {
                time = dt.TimeOfDay;
                return true;
            }

            if (value is string s && TimeSpan.TryParse(s, CultureInfo.CurrentCulture, out ts))
            {
                time = ts;
                return true;
            }

            time = default;
            return false;
        }

        private static int ConvertToColor(object? value)
        {
            try
            {
                if (value == null || value == DBNull.Value)
                    return 0;

                if (value is int i)
                    return i;

                if (value is short s)
                    return s;

                if (value is long l)
                    return (int)l;

                if (value is byte b)
                    return b;

                if (value is string str && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                    return i;
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppointmentSearch] ConvertToColor failed", ex, "CLI40");
            }

            return 0;
        }

        private static bool IsExcludedDayMarker(object? value)
        {
            if (value is string str)
            {
                return str.Contains("~$0$", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private readonly record struct AppointmentEntry(DateTime SlotStart, int ColorCode, bool IsExcludedDayMarker);
    }

    public enum SearchMode
    {
        Around,
        From
    }
}
