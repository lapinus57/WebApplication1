using System;
using System.Collections.Generic;
using System.Linq;
using Client.Models;

namespace Client.Services
{
    public static class AppointmentGroupBuilder
    {
        public static IReadOnlyList<AppointmentSearchResult> BuildResults(
            IEnumerable<AppointmentSlotInfo> slots,
            AppointmentSearchFilters filters)
        {
            var personCount = Math.Max(1, filters.PersonCount);
            var orderedSlots = slots
                .OrderBy(s => s.SlotStart)
                .ToList();

            if (personCount == 1)
            {
                return orderedSlots
                    .Select(slot => new AppointmentSearchResult(
                        1,
                        new[] { new AppointmentResultSlot(slot, 1) },
                        CombinationQuality.Strict,
                        TimeSpan.Zero))
                    .ToList();
            }

            var results = new List<AppointmentSearchResult>();
            foreach (var dayGroup in orderedSlots.GroupBy(s => s.SlotStart.Date))
            {
                var generator = new CombinationGenerator(dayGroup.OrderBy(s => s.SlotStart).ToList(), personCount);
                results.AddRange(generator.Generate());
            }

            if (results.Count == 0)
                return results;

            var bestQuality = results.Min(r => r.Quality);
            return results
                .Where(r => r.Quality == bestQuality)
                .OrderBy(r => r.Day)
                .ThenBy(r => r.TotalSpan)
                .ThenBy(r => r.Slots.First().Slot.SlotStart)
                .ToList();
        }

        private sealed class CombinationGenerator
        {
            private readonly List<AppointmentSlotInfo> _slots;
            private readonly int _personCount;
            private readonly List<AppointmentResultSlot> _current = new();
            private readonly List<AppointmentSearchResult> _results = new();

            public CombinationGenerator(List<AppointmentSlotInfo> slots, int personCount)
            {
                _slots = slots;
                _personCount = personCount;
            }

            public IReadOnlyList<AppointmentSearchResult> Generate()
            {
                GenerateRecursive(0, _personCount);
                return _results;
            }

            private void GenerateRecursive(int index, int remaining)
            {
                if (remaining == 0)
                {
                    EvaluateCurrent();
                    return;
                }

                if (index >= _slots.Count)
                {
                    return;
                }

                var slot = _slots[index];
                var maxTake = Math.Min(slot.AvailableCapacity, remaining);
                for (var take = 0; take <= maxTake; take++)
                {
                    if (take > 0)
                    {
                        _current.Add(new AppointmentResultSlot(slot, take));
                    }

                    GenerateRecursive(index + 1, remaining - take);

                    if (take > 0)
                    {
                        _current.RemoveAt(_current.Count - 1);
                    }
                }
            }

            private void EvaluateCurrent()
            {
                if (_current.Count == 0)
                    return;

                var times = new List<DateTime>();
                var morningCount = 0;
                var afternoonCount = 0;

                foreach (var allocation in _current)
                {
                    for (var i = 0; i < allocation.PersonCount; i++)
                    {
                        times.Add(allocation.Slot.SlotStart);
                    }

                    if (allocation.Slot.IsMorning)
                        morningCount += allocation.PersonCount;
                    if (allocation.Slot.IsAfternoon)
                        afternoonCount += allocation.PersonCount;
                }

                if (times.Count != _personCount)
                    return;

                times.Sort();

                var totalSpan = times.Count > 1
                    ? times.Last() - times.First()
                    : TimeSpan.Zero;

                var distinctTimes = times.Distinct().OrderBy(t => t).ToList();

                var quality = EvaluateQuality(distinctTimes, morningCount, afternoonCount, totalSpan);
                if (quality == null)
                    return;

                _results.Add(new AppointmentSearchResult(
                    _personCount,
                    _current.Select(slot => new AppointmentResultSlot(slot.Slot, slot.PersonCount)).ToList(),
                    quality.Value,
                    totalSpan));
            }

            private CombinationQuality? EvaluateQuality(
                IReadOnlyList<DateTime> distinctTimes,
                int morningCount,
                int afternoonCount,
                TimeSpan totalSpan)
            {
                var mixDayParts = morningCount > 0 && afternoonCount > 0;

                if (_personCount == 2)
                {
                    if (mixDayParts)
                        return null;

                    var gap = distinctTimes.Count > 1
                        ? distinctTimes[1] - distinctTimes[0]
                        : TimeSpan.Zero;

                    if (gap <= TimeSpan.FromHours(1) && totalSpan <= TimeSpan.FromHours(1))
                        return CombinationQuality.Strict;

                    if (totalSpan <= TimeSpan.FromHours(2))
                        return CombinationQuality.Relaxed;

                    return CombinationQuality.Fallback;
                }

                if (_personCount == 3)
                {
                    if (mixDayParts)
                        return CombinationQuality.Fallback;

                    var hasPair = _current.Any(s => s.PersonCount >= 2);
                    var strict = totalSpan <= TimeSpan.FromMinutes(90)
                                 && GapWithin(distinctTimes, 0, TimeSpan.FromHours(1))
                                 && GapWithin(distinctTimes, 1, TimeSpan.FromMinutes(30));

                    if (strict)
                        return CombinationQuality.Strict;

                    if (hasPair && totalSpan <= TimeSpan.FromHours(2))
                        return CombinationQuality.Relaxed;

                    if (totalSpan <= TimeSpan.FromHours(3))
                        return CombinationQuality.Fallback;

                    return null;
                }

                if (_personCount == 4)
                {
                    var hasValidMix = !mixDayParts || (morningCount >= 2 && afternoonCount >= 2);
                    if (!hasValidMix)
                        return null;

                    var strict = totalSpan <= TimeSpan.FromHours(2)
                                 && GapWithin(distinctTimes, 0, TimeSpan.FromHours(1))
                                 && GapWithin(distinctTimes, 1, TimeSpan.FromMinutes(30))
                                 && GapWithin(distinctTimes, 2, TimeSpan.FromMinutes(30));

                    if (strict)
                        return CombinationQuality.Strict;

                    if (totalSpan <= TimeSpan.FromHours(2.5))
                        return CombinationQuality.Relaxed;

                    if (totalSpan <= TimeSpan.FromHours(3))
                        return CombinationQuality.Fallback;

                    return null;
                }

                return null;
            }

            private static bool GapWithin(IReadOnlyList<DateTime> times, int index, TimeSpan limit)
            {
                if (times.Count <= index + 1)
                    return true;

                var gap = times[index + 1] - times[index];
                return gap <= limit;
            }
        }
    }
}
