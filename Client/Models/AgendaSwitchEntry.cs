using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Client.Models
{
    public class AgendaSwitchEntry : INotifyPropertyChanged
    {
        private DayOfWeek _day;
        private TimeSpan _startTime = TimeSpan.FromHours(8);
        private TimeSpan _endTime = TimeSpan.FromHours(18);
        private string _userName = string.Empty;
        private bool _isAllDay;

        public DayOfWeek Day
        {
            get => _day;
            set => SetProperty(ref _day, value);
        }

        public TimeSpan StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public TimeSpan EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        public bool IsAllDay
        {
            get => _isAllDay;
            set => SetProperty(ref _isAllDay, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
