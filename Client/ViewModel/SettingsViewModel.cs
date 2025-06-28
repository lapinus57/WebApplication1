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
        private string _appTheme = "Dark";
        private string _shortcutF5Refraction = string.Empty;
        private string _shortcutF5Lentilles = string.Empty;
        private string _shortcutF5Pathologies = string.Empty;
        private string _shortcutF5Orthoptie = string.Empty;

        private string _shortcutF6Refraction = string.Empty;
        private string _shortcutF6Lentilles = string.Empty;
        private string _shortcutF6Pathologies = string.Empty;
        private string _shortcutF6Orthoptie = string.Empty;

        private string _shortcutF7Refraction = string.Empty;
        private string _shortcutF7Lentilles = string.Empty;
        private string _shortcutF7Pathologies = string.Empty;
        private string _shortcutF7Orthoptie = string.Empty;

        private string _shortcutF8Refraction = string.Empty;
        private string _shortcutF8Lentilles = string.Empty;
        private string _shortcutF8Pathologies = string.Empty;
        private string _shortcutF8Orthoptie = string.Empty;

        private string _ctrlF9Exam = string.Empty;
        private string _shiftF9Exam = string.Empty;
        private string _ctrlF10Exam = string.Empty;
        private string _shiftF10Exam = string.Empty;
        private string _ctrlF11Exam = string.Empty;
        private string _shiftF11Exam = string.Empty;
        private string _ctrlF12Exam = string.Empty;
        private string _shiftF12Exam = string.Empty;

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

        public string AppTheme
        {
            get => _appTheme;
            set
            {
                if (_appTheme != value)
                {
                    _appTheme = value;
                    OnPropertyChanged(nameof(AppTheme));
                    AppSettings.Set("AppTheme", value);
                    ApplyTheme(value);
                }
            }
        }

        private string Get(string key) => AppSettings.Get(key, string.Empty);
        private void Set(string key, string value) => AppSettings.Set(key, value);

        public string ShortcutF5Refraction
        {
            get => _shortcutF5Refraction;
            set { if (_shortcutF5Refraction != value) { _shortcutF5Refraction = value; OnPropertyChanged(nameof(ShortcutF5Refraction)); Set("ShortcutF5Refraction", value); } }
        }

        public string ShortcutF5Lentilles
        {
            get => _shortcutF5Lentilles;
            set { if (_shortcutF5Lentilles != value) { _shortcutF5Lentilles = value; OnPropertyChanged(nameof(ShortcutF5Lentilles)); Set("ShortcutF5Lentilles", value); } }
        }

        public string ShortcutF5Pathologies
        {
            get => _shortcutF5Pathologies;
            set { if (_shortcutF5Pathologies != value) { _shortcutF5Pathologies = value; OnPropertyChanged(nameof(ShortcutF5Pathologies)); Set("ShortcutF5Pathologies", value); } }
        }

        public string ShortcutF5Orthoptie
        {
            get => _shortcutF5Orthoptie;
            set { if (_shortcutF5Orthoptie != value) { _shortcutF5Orthoptie = value; OnPropertyChanged(nameof(ShortcutF5Orthoptie)); Set("ShortcutF5Orthoptie", value); } }
        }

        public string ShortcutF6Refraction
        {
            get => _shortcutF6Refraction;
            set { if (_shortcutF6Refraction != value) { _shortcutF6Refraction = value; OnPropertyChanged(nameof(ShortcutF6Refraction)); Set("ShortcutF6Refraction", value); } }
        }

        public string ShortcutF6Lentilles
        {
            get => _shortcutF6Lentilles;
            set { if (_shortcutF6Lentilles != value) { _shortcutF6Lentilles = value; OnPropertyChanged(nameof(ShortcutF6Lentilles)); Set("ShortcutF6Lentilles", value); } }
        }

        public string ShortcutF6Pathologies
        {
            get => _shortcutF6Pathologies;
            set { if (_shortcutF6Pathologies != value) { _shortcutF6Pathologies = value; OnPropertyChanged(nameof(ShortcutF6Pathologies)); Set("ShortcutF6Pathologies", value); } }
        }

        public string ShortcutF6Orthoptie
        {
            get => _shortcutF6Orthoptie;
            set { if (_shortcutF6Orthoptie != value) { _shortcutF6Orthoptie = value; OnPropertyChanged(nameof(ShortcutF6Orthoptie)); Set("ShortcutF6Orthoptie", value); } }
        }

        public string ShortcutF7Refraction
        {
            get => _shortcutF7Refraction;
            set { if (_shortcutF7Refraction != value) { _shortcutF7Refraction = value; OnPropertyChanged(nameof(ShortcutF7Refraction)); Set("ShortcutF7Refraction", value); } }
        }

        public string ShortcutF7Lentilles
        {
            get => _shortcutF7Lentilles;
            set { if (_shortcutF7Lentilles != value) { _shortcutF7Lentilles = value; OnPropertyChanged(nameof(ShortcutF7Lentilles)); Set("ShortcutF7Lentilles", value); } }
        }

        public string ShortcutF7Pathologies
        {
            get => _shortcutF7Pathologies;
            set { if (_shortcutF7Pathologies != value) { _shortcutF7Pathologies = value; OnPropertyChanged(nameof(ShortcutF7Pathologies)); Set("ShortcutF7Pathologies", value); } }
        }

        public string ShortcutF7Orthoptie
        {
            get => _shortcutF7Orthoptie;
            set { if (_shortcutF7Orthoptie != value) { _shortcutF7Orthoptie = value; OnPropertyChanged(nameof(ShortcutF7Orthoptie)); Set("ShortcutF7Orthoptie", value); } }
        }

        public string ShortcutF8Refraction
        {
            get => _shortcutF8Refraction;
            set { if (_shortcutF8Refraction != value) { _shortcutF8Refraction = value; OnPropertyChanged(nameof(ShortcutF8Refraction)); Set("ShortcutF8Refraction", value); } }
        }

        public string ShortcutF8Lentilles
        {
            get => _shortcutF8Lentilles;
            set { if (_shortcutF8Lentilles != value) { _shortcutF8Lentilles = value; OnPropertyChanged(nameof(ShortcutF8Lentilles)); Set("ShortcutF8Lentilles", value); } }
        }

        public string ShortcutF8Pathologies
        {
            get => _shortcutF8Pathologies;
            set { if (_shortcutF8Pathologies != value) { _shortcutF8Pathologies = value; OnPropertyChanged(nameof(ShortcutF8Pathologies)); Set("ShortcutF8Pathologies", value); } }
        }

        public string ShortcutF8Orthoptie
        {
            get => _shortcutF8Orthoptie;
            set { if (_shortcutF8Orthoptie != value) { _shortcutF8Orthoptie = value; OnPropertyChanged(nameof(ShortcutF8Orthoptie)); Set("ShortcutF8Orthoptie", value); } }
        }

        public string CtrlF9Exam
        {
            get => _ctrlF9Exam;
            set { if (_ctrlF9Exam != value) { _ctrlF9Exam = value; OnPropertyChanged(nameof(CtrlF9Exam)); Set("CtrlF9Exam", value); } }
        }

        public string ShiftF9Exam
        {
            get => _shiftF9Exam;
            set { if (_shiftF9Exam != value) { _shiftF9Exam = value; OnPropertyChanged(nameof(ShiftF9Exam)); Set("ShiftF9Exam", value); } }
        }

        public string CtrlF10Exam
        {
            get => _ctrlF10Exam;
            set { if (_ctrlF10Exam != value) { _ctrlF10Exam = value; OnPropertyChanged(nameof(CtrlF10Exam)); Set("CtrlF10Exam", value); } }
        }

        public string ShiftF10Exam
        {
            get => _shiftF10Exam;
            set { if (_shiftF10Exam != value) { _shiftF10Exam = value; OnPropertyChanged(nameof(ShiftF10Exam)); Set("ShiftF10Exam", value); } }
        }

        public string CtrlF11Exam
        {
            get => _ctrlF11Exam;
            set { if (_ctrlF11Exam != value) { _ctrlF11Exam = value; OnPropertyChanged(nameof(CtrlF11Exam)); Set("CtrlF11Exam", value); } }
        }

        public string ShiftF11Exam
        {
            get => _shiftF11Exam;
            set { if (_shiftF11Exam != value) { _shiftF11Exam = value; OnPropertyChanged(nameof(ShiftF11Exam)); Set("ShiftF11Exam", value); } }
        }

        public string CtrlF12Exam
        {
            get => _ctrlF12Exam;
            set { if (_ctrlF12Exam != value) { _ctrlF12Exam = value; OnPropertyChanged(nameof(CtrlF12Exam)); Set("CtrlF12Exam", value); } }
        }

        public string ShiftF12Exam
        {
            get => _shiftF12Exam;
            set { if (_shiftF12Exam != value) { _shiftF12Exam = value; OnPropertyChanged(nameof(ShiftF12Exam)); Set("ShiftF12Exam", value); } }
        }

        public void Load()
        {
            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            _isOldSchoolMode = style == "OldSchool";
            if (double.TryParse(AppSettings.Get("MessageFontSize", "14"), out var size))
            {
                _messageFontSize = size;
            }
            _appTheme = AppSettings.Get("AppTheme", "Dark");
            ApplyTheme(_appTheme);

            _shortcutF5Refraction = Get("ShortcutF5Refraction");
            _shortcutF5Lentilles = Get("ShortcutF5Lentilles");
            _shortcutF5Pathologies = Get("ShortcutF5Pathologies");
            _shortcutF5Orthoptie = Get("ShortcutF5Orthoptie");

            _shortcutF6Refraction = Get("ShortcutF6Refraction");
            _shortcutF6Lentilles = Get("ShortcutF6Lentilles");
            _shortcutF6Pathologies = Get("ShortcutF6Pathologies");
            _shortcutF6Orthoptie = Get("ShortcutF6Orthoptie");

            _shortcutF7Refraction = Get("ShortcutF7Refraction");
            _shortcutF7Lentilles = Get("ShortcutF7Lentilles");
            _shortcutF7Pathologies = Get("ShortcutF7Pathologies");
            _shortcutF7Orthoptie = Get("ShortcutF7Orthoptie");

            _shortcutF8Refraction = Get("ShortcutF8Refraction");
            _shortcutF8Lentilles = Get("ShortcutF8Lentilles");
            _shortcutF8Pathologies = Get("ShortcutF8Pathologies");
            _shortcutF8Orthoptie = Get("ShortcutF8Orthoptie");

            _ctrlF9Exam = Get("CtrlF9Exam");
            _shiftF9Exam = Get("ShiftF9Exam");
            _ctrlF10Exam = Get("CtrlF10Exam");
            _shiftF10Exam = Get("ShiftF10Exam");
            _ctrlF11Exam = Get("CtrlF11Exam");
            _shiftF11Exam = Get("ShiftF11Exam");
            _ctrlF12Exam = Get("CtrlF12Exam");
            _shiftF12Exam = Get("ShiftF12Exam");

            // update resources with loaded values
            Application.Current.Resources["MessageFontSize"] = _messageFontSize;
            OnPropertyChanged(string.Empty);
        }

        private static void ApplyTheme(string theme)
        {
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                Application.Current.RequestedTheme = appTheme;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
