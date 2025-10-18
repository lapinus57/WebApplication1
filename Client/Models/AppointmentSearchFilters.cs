using System;

namespace Client.Models
{
    public class AppointmentSearchFilters
    {
        public int PersonCount { get; set; } = 1;
        public DayOfWeek? PreferredDay { get; set; }
        public TimeOfDayPreference TimePreference { get; set; } = TimeOfDayPreference.Any;
        public SchoolHolidayFilter HolidayFilter { get; set; } = SchoolHolidayFilter.Any;
        public SchoolHolidayZone HolidayZone { get; set; } = SchoolHolidayZone.Any;

        public AppointmentSearchFilters Clone() => new()
        {
            PersonCount = PersonCount,
            PreferredDay = PreferredDay,
            TimePreference = TimePreference,
            HolidayFilter = HolidayFilter,
            HolidayZone = HolidayZone
        };
    }

    public enum TimeOfDayPreference
    {
        Any,
        Morning,
        Afternoon
    }

    public enum SchoolHolidayFilter
    {
        Any,
        OnlyDuring,
        Exclude
    }

    public enum SchoolHolidayZone
    {
        Any,
        ZoneA,
        ZoneB,
        ZoneC
    }
}
