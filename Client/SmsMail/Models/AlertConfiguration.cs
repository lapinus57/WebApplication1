using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Client.SmsMail.Models
{
    public enum AlertOffsetUnit
    {
        Minutes,
        Hours,
        Days
    }

    public sealed class AlertConfiguration : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private int _offsetMinutes = 60;
        private AlertOffsetUnit _offsetUnit = AlertOffsetUnit.Minutes;
        private int _offsetValue = 60;
        private string _label = "Alerte";

        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public int OffsetMinutes
        {
            get => _offsetMinutes;
            set
            {
                if (SetProperty(ref _offsetMinutes, value))
                {
                    UpdateUnitAndValueFromMinutes();
                    OnPropertyChanged(nameof(OffsetDisplay));
                }
            }
        }

        public AlertOffsetUnit OffsetUnit
        {
            get => _offsetUnit;
            set
            {
                if (SetProperty(ref _offsetUnit, value))
                {
                    SyncOffsetMinutesFromValue();
                }
            }
        }

        public int OffsetValue
        {
            get => _offsetValue;
            set
            {
                if (SetProperty(ref _offsetValue, value))
                {
                    SyncOffsetMinutesFromValue();
                }
            }
        }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public string OffsetDisplay
        {
            get
            {
                if (OffsetMinutes == 0) return "À l’heure du rendez-vous";
                var timeSpan = TimeSpan.FromMinutes(OffsetMinutes);

                if (timeSpan.TotalDays >= 1 && Math.Abs(timeSpan.TotalDays % 1) < double.Epsilon)
                {
                    var days = (int)timeSpan.TotalDays;
                    return days > 1 ? $"{days} jours avant" : "1 jour avant";
                }

                if (timeSpan.TotalHours >= 1 && Math.Abs(timeSpan.TotalHours % 1) < double.Epsilon)
                {
                    var hours = (int)timeSpan.TotalHours;
                    return hours > 1 ? $"{hours} heures avant" : "1 heure avant";
                }

                return OffsetMinutes > 1 ? $"{OffsetMinutes} minutes avant" : "1 minute avant";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void SyncOffsetMinutesFromValue()
        {
            var minutes = _offsetUnit switch
            {
                AlertOffsetUnit.Days => _offsetValue * 24 * 60,
                AlertOffsetUnit.Hours => _offsetValue * 60,
                _ => _offsetValue
            };

            if (minutes == _offsetMinutes)
            {
                OnPropertyChanged(nameof(OffsetDisplay));
                return;
            }

            _offsetMinutes = minutes;
            OnPropertyChanged(nameof(OffsetMinutes));
            UpdateUnitAndValueFromMinutes();
            OnPropertyChanged(nameof(OffsetDisplay));
        }

        private void UpdateUnitAndValueFromMinutes()
        {
            AlertOffsetUnit preferredUnit;
            int value;

            if (_offsetMinutes >= 1440 && _offsetMinutes % 1440 == 0)
            {
                preferredUnit = AlertOffsetUnit.Days;
                value = _offsetMinutes / 1440;
            }
            else if (_offsetMinutes >= 60 && _offsetMinutes % 60 == 0)
            {
                preferredUnit = AlertOffsetUnit.Hours;
                value = _offsetMinutes / 60;
            }
            else
            {
                preferredUnit = AlertOffsetUnit.Minutes;
                value = _offsetMinutes;
            }

            if (_offsetUnit != preferredUnit)
            {
                _offsetUnit = preferredUnit;
                OnPropertyChanged(nameof(OffsetUnit));
            }

            if (_offsetValue != value)
            {
                _offsetValue = value;
                OnPropertyChanged(nameof(OffsetValue));
            }
        }
    }
}

