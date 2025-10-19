using System;

namespace Client.Models
{
    public class AppointmentSearchConfig
    {
        public string DatabasePath { get; set; } = string.Empty;
        public string TableName { get; set; } = "Ag_rdv";
        public string DateColumn { get; set; } = "Date";
        public string TimeColumn { get; set; } = "Heure";
        public string ColorColumn { get; set; } = "Marque";
        public int SlotLengthMinutes { get; set; } = 15;
        public int MaxAppointmentsPerSlot { get; set; } = 3;
        public int OverloadExtraAppointments { get; set; } = 1;
        public TimeSpan MorningStart { get; set; } = TimeSpan.FromHours(8);
        public TimeSpan MorningEnd { get; set; } = TimeSpan.FromHours(11.5);
        public TimeSpan AfternoonStart { get; set; } = TimeSpan.FromHours(13);
        public TimeSpan AfternoonEnd { get; set; } = TimeSpan.FromHours(18);
        public TimeSpan FoMorningLimit { get; set; } = TimeSpan.FromHours(10);
        public TimeSpan FoAfternoonLimit { get; set; } = TimeSpan.FromHours(16.5);
        public int ExcludedDayReleaseMonths { get; set; } = 3;

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(DatabasePath)
                && !string.IsNullOrWhiteSpace(TableName)
                && !string.IsNullOrWhiteSpace(DateColumn)
                && !string.IsNullOrWhiteSpace(TimeColumn)
                && !string.IsNullOrWhiteSpace(ColorColumn)
                && SlotLengthMinutes > 0
                && MaxAppointmentsPerSlot > 0;
        }

        public void EnforceClientSlotLimits()
        {
            MaxAppointmentsPerSlot = Math.Max(1, Math.Min(2, MaxAppointmentsPerSlot));
            OverloadExtraAppointments = 0;
        }
    }
}
