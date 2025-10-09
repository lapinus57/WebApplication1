using System;

namespace Client.Models
{
    public class Patient
    {
        public string Id { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ForegroundColor =>
           ColorUtils.ToHex(
               ColorUtils.GetContrastingTextColor(
                   ColorUtils.FromHex(Colors)));
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string Exams { get; set; } = string.Empty;
        public string Eye { get; set; } = string.Empty; 
        public string Annotation { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public DateTime HoldTime { get; set; }
        public DateTime? PickUpTime { get; set; }
        public TimeSpan TimeOrder { get; set; }
        public string Examinator { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public bool IsTaken { get; set; }
        public bool IsArchived { get; set; }

        public string HoldTimeFormatted => HoldTime.ToString("HH:mm");
        public string? PickUpTimeFormatted => PickUpTime?.ToString("HH:mm");
        public string TimeSinceHoldTimeFormatted
        {
            get
            {
                var span = DateTime.Now - HoldTime;

                if (span < TimeSpan.Zero)
                {
                    span = TimeSpan.Zero;
                }

                if (span.TotalDays >= 1)
                {
                    var days = (int)span.TotalDays;
                    return $"{days}j {span:hh\\:mm}";
                }

                return span.TotalHours >= 1
                    ? span.ToString("hh\\:mm")
                    : span.ToString("mm\\:ss");
            }
        }

        public string ToggleExamLabel => IsTaken
            ? $"Annuler {Exams} de {FirstName} {LastName}"
            : $"Faire passer {Exams} de {FirstName} {LastName}";
    }
}
