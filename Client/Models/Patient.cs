using System;
using System.ComponentModel;

namespace Client.Models
{
    public class Patient : INotifyPropertyChanged
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

        private int _pickupAlertThresholdMinutes;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string HoldTimeFormatted => HoldTime.ToString("HH:mm");
        public string? PickUpTimeFormatted => PickUpTime?.ToString("HH:mm");
        public string TimeSinceHoldTimeFormatted
        {
            get
            {
                var span = GetElapsedHoldTime();
                var minutes = (int)Math.Max(0, Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
                var formatted = $"{minutes} min";

                return IsAlertActive(span)
                    ? $"⚠️ {formatted}"
                    : formatted;
            }
        }

        public bool RequiresPickupAttention => IsAlertActive(GetElapsedHoldTime());

        public void RefreshHoldTimeInfo(int pickupAlertThresholdMinutes)
        {
            _pickupAlertThresholdMinutes = Math.Max(0, pickupAlertThresholdMinutes);
            OnPropertyChanged(nameof(TimeSinceHoldTimeFormatted));
            OnPropertyChanged(nameof(RequiresPickupAttention));
        }

        public string ToggleExamLabel => IsTaken
            ? $"Annuler {Exams} de {FirstName} {LastName}"
            : $"Faire passer {Exams} de {FirstName} {LastName}";

        private TimeSpan GetElapsedHoldTime()
        {
            var span = DateTime.Now - HoldTime;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        private bool IsAlertActive(TimeSpan span)
        {
            if (IsTaken || _pickupAlertThresholdMinutes <= 0)
            {
                return false;
            }

            return span.TotalMinutes >= _pickupAlertThresholdMinutes;
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
