using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Client.Helpers;
using Client.Models;

namespace Client.ViewModel
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private bool _isOldSchoolMode;
        private double _messageFontSize = 14;

        public event Action<ChatStyle>? DisplayStyleChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsOldSchoolMode
        {
            get => _isOldSchoolMode;
            set
            {
                if (_isOldSchoolMode != value)
                {
                    _isOldSchoolMode = value;
                    OnPropertyChanged(nameof(IsOldSchoolMode));
                    AppSettings.Set("ChatDisplayStyle", value ? "OldSchool" : "Modern");
                    DisplayStyleChanged?.Invoke(value ? ChatStyle.OldSchool : ChatStyle.Modern);
                }
            }
        }

        public double MessageFontSize
        {
            get => _messageFontSize;
            set
            {
                if (_messageFontSize != value)
                {
                    _messageFontSize = value;
                    OnPropertyChanged(nameof(MessageFontSize));
                    AppSettings.Set("MessageFontSize", value.ToString());
                    Application.Current.Resources["MessageFontSize"] = value;
                }
            }
        }

        public void Load()
        {
            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            _isOldSchoolMode = style == "OldSchool";
            if (double.TryParse(AppSettings.Get("MessageFontSize", "14"), out var size))
            {
                _messageFontSize = size;
            }
            // update resources with loaded values
            Application.Current.Resources["MessageFontSize"] = _messageFontSize;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
