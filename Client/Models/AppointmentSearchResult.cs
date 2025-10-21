using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Client.Models
{
    public class AppointmentSearchResult
    {
        public AppointmentSearchResult(
            int personCount,
            IReadOnlyList<AppointmentResultSlot> slots,
            CombinationQuality quality,
            TimeSpan totalSpan)
        {
            PersonCount = personCount;
            Slots = slots;
            Quality = quality;
            TotalSpan = totalSpan;
        }

        public int PersonCount { get; }
        public IReadOnlyList<AppointmentResultSlot> Slots { get; }
        public CombinationQuality Quality { get; }
        public TimeSpan TotalSpan { get; }

        public DateTime Day => Slots.Count > 0 ? Slots[0].Slot.SlotStart.Date : DateTime.MinValue;

        public bool UsesOverload => Slots.Any(s => s.Slot.UsesOverloadCapacity);

        public bool UsesRedRelaxation => Slots.Any(s => s.Slot.RedRelaxationApplied);

        public string Header
        {
            get
            {
                var culture = CultureInfo.CurrentCulture;
                var textInfo = culture.TextInfo;
                var dayText = Day.ToString("dddd dd/MM/yyyy", culture);
                var duration = TotalSpan <= TimeSpan.Zero
                    ? "durée totale <1min"
                    : $"durée totale {FormatDuration(TotalSpan)}";
                return $"{textInfo.ToTitleCase(dayText)} - {PersonCount} personne{(PersonCount > 1 ? "s" : string.Empty)} ({duration})";
            }
        }

        public string Detail
        {
            get
            {
                var slotDescriptions = Slots
                    .Select(slot => slot.ToDescription())
                    .ToList();
                return string.Join(" • ", slotDescriptions);
            }
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalMinutes < 1)
            {
                return "<1min";
            }

            var parts = new List<string>();
            if (value.Hours > 0)
            {
                parts.Add(value.Hours == 1 ? "1h" : $"{value.Hours}h");
            }

            var minutes = value.Minutes + (value.Seconds >= 30 ? 1 : 0);
            if (minutes > 0)
            {
                parts.Add($"{minutes}min");
            }

            return parts.Count == 0 ? "<1min" : string.Join(" ", parts);
        }
    }

    public class AppointmentResultSlot
    {
        public AppointmentResultSlot(AppointmentSlotInfo slot, int personCount)
        {
            Slot = slot;
            PersonCount = personCount;
        }

        public AppointmentSlotInfo Slot { get; }
        public int PersonCount { get; }

        public string TimeLabel => Slot.TimeLabel;

        public string PersonSummary => PersonCount > 1
            ? $"{PersonCount} personnes"
            : "1 personne";

        public string ExistingSummary => $"RDV existants : {Slot.ExistingAppointments}";

        public string RemainingCapacity => PersonCount >= Slot.AvailableCapacity
            ? "Capacité utilisée"
            : $"Capacité restante : {Slot.AvailableCapacity - PersonCount}";

        public string SlotSummary => Slot.Summary;

        public string ToDescription()
        {
            var parts = new List<string>
            {
                $"{TimeLabel} ({PersonSummary})"
            };

            if (Slot.UsesOverloadCapacity)
            {
                parts.Add("surcharge");
            }

            if (Slot.RedRelaxationApplied)
            {
                parts.Add("tolérance rouge");
            }

            return string.Join(" - ", parts);
        }
    }

    public enum CombinationQuality
    {
        Strict = 0,
        Relaxed = 1,
        Fallback = 2
    }
}
