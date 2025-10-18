using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Services
{
    public class SchoolHolidayService : ISchoolHolidayService
    {
        private static readonly Uri ApiUri = new("https://data.education.gouv.fr/api/records/1.0/search/?dataset=fr-en-calendrier-scolaire&rows=500&sort=-end_date");
        private static readonly HttpClient HttpClient = new();

        private readonly object _syncRoot = new();
        private readonly Dictionary<SchoolHolidayZone, List<HolidayPeriod>> _periodsByZone = new();
        private readonly object _loadLock = new();
        private Task? _loadTask;

        public bool IsSchoolHoliday(DateTime date, SchoolHolidayZone zone)
        {
            EnsureLoaded();

            var day = date.Date;
            foreach (var targetZone in EnumerateTargetZones(zone))
            {
                List<HolidayPeriod>? periods;
                lock (_syncRoot)
                {
                    _periodsByZone.TryGetValue(targetZone, out periods);
                }

                if (periods == null || periods.Count == 0)
                    continue;

                foreach (var period in periods)
                {
                    if (day >= period.Start && day <= period.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureLoaded()
        {
            var task = Volatile.Read(ref _loadTask);
            if (task == null)
            {
                lock (_loadLock)
                {
                    task = _loadTask;
                    if (task == null)
                    {
                        task = LoadFromApiAsync();
                        Volatile.Write(ref _loadTask, task);
                    }
                }
            }

            try
            {
                task!.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.LogException("[SchoolHolidayService] LoadFromApi", ex, "CLI62");

                lock (_loadLock)
                {
                    if (ReferenceEquals(task, _loadTask))
                    {
                        Volatile.Write(ref _loadTask, null);
                    }
                }
            }
        }

        private async Task LoadFromApiAsync()
        {
            using var response = await HttpClient.GetAsync(ApiUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var document = JsonConvert.DeserializeObject<ApiResponse>(payload);
            var records = document?.Records;

            if (records == null || records.Count == 0)
            {
                lock (_syncRoot)
                {
                    _periodsByZone.Clear();
                }
                return;
            }

            var map = new Dictionary<SchoolHolidayZone, List<HolidayPeriod>>
            {
                { SchoolHolidayZone.ZoneA, new List<HolidayPeriod>() },
                { SchoolHolidayZone.ZoneB, new List<HolidayPeriod>() },
                { SchoolHolidayZone.ZoneC, new List<HolidayPeriod>() }
            };

            foreach (var record in records)
            {
                var fields = record.Fields;
                if (fields == null || fields.StartDate == null || fields.EndDate == null)
                    continue;

                var zones = ExtractZones(fields).ToList();
                if (zones.Count == 0)
                {
                    zones.AddRange(new[]
                    {
                        SchoolHolidayZone.ZoneA,
                        SchoolHolidayZone.ZoneB,
                        SchoolHolidayZone.ZoneC
                    });
                }

                foreach (var zone in zones)
                {
                    if (!map.TryGetValue(zone, out var list))
                        continue;

                    list.Add(new HolidayPeriod(fields.StartDate.Value, fields.EndDate.Value));
                }
            }

            foreach (var list in map.Values)
            {
                list.Sort((left, right) => left.Start.CompareTo(right.Start));
            }

            lock (_syncRoot)
            {
                _periodsByZone.Clear();
                foreach (var kvp in map)
                {
                    if (kvp.Value.Count > 0)
                    {
                        _periodsByZone[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        private static IEnumerable<SchoolHolidayZone> EnumerateTargetZones(SchoolHolidayZone zone)
        {
            if (zone == SchoolHolidayZone.Any)
            {
                yield return SchoolHolidayZone.ZoneA;
                yield return SchoolHolidayZone.ZoneB;
                yield return SchoolHolidayZone.ZoneC;
                yield break;
            }

            yield return zone;
        }

        private static IEnumerable<SchoolHolidayZone> ExtractZones(ApiFields fields)
        {
            if (fields.Zones != null)
            {
                foreach (var entry in fields.Zones)
                {
                    if (TryParseZone(entry, out var zone))
                    {
                        yield return zone;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fields.Location))
            {
                var parts = fields.Location.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (TryParseZone(part, out var zone))
                    {
                        yield return zone;
                    }
                }
            }
        }

        private static bool TryParseZone(string? text, out SchoolHolidayZone zone)
        {
            zone = SchoolHolidayZone.Any;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleaned = text.Trim().Replace(" ", string.Empty);
            if (Enum.TryParse(cleaned, true, out zone) && zone != SchoolHolidayZone.Any)
            {
                return true;
            }

            return false;
        }

        private sealed class HolidayPeriod
        {
            public HolidayPeriod(DateTime start, DateTime end)
            {
                Start = start.Date;
                End = end.Date;
            }

            public DateTime Start { get; }
            public DateTime End { get; }
        }

        private sealed class ApiResponse
        {
            [JsonProperty("records")]
            public List<ApiRecord>? Records { get; set; }
        }

        private sealed class ApiRecord
        {
            [JsonProperty("fields")]
            public ApiFields? Fields { get; set; }
        }

        private sealed class ApiFields
        {
            [JsonProperty("start_date")]
            public DateTime? StartDate { get; set; }

            [JsonProperty("end_date")]
            public DateTime? EndDate { get; set; }

            [JsonProperty("zones")]
            [JsonConverter(typeof(SingleOrArrayConverter))]
            public List<string>? Zones { get; set; }

            [JsonProperty("location")]
            public string? Location { get; set; }
        }

        private sealed class SingleOrArrayConverter : JsonConverter<List<string>?>
        {
            public override List<string>? ReadJson(JsonReader reader, Type objectType, List<string>? existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return null;
                }

                if (reader.TokenType == JsonToken.StartArray)
                {
                    var array = JArray.Load(reader);
                    var results = new List<string>();
                    foreach (var item in array)
                    {
                        if (item.Type == JTokenType.String)
                        {
                            var text = item.Value<string>();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                results.Add(text);
                            }
                        }
                    }

                    return results;
                }

                if (reader.TokenType == JsonToken.String)
                {
                    var value = (string?)reader.Value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return new List<string>();
                    }

                    return new List<string> { value };
                }

                if (reader.TokenType == JsonToken.StartObject)
                {
                    var token = JObject.Load(reader);
                    var text = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return new List<string>();
                    }

                    return new List<string> { text! };
                }

                throw new JsonSerializationException($"Unexpected token {reader.TokenType} when parsing zones.");
            }

            public override void WriteJson(JsonWriter writer, List<string>? value, JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }
}
