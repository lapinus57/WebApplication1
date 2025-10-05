using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Helpers;
using Client.Models;
using System.Runtime.Versioning;

namespace Client.ViewModel
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        [SupportedOSPlatform("windows")]
        private bool _isOldSchoolMode;
        private double _messageFontSize = 14;
        private string _appTheme = "Dark";
        private string _colorUserName = "Black";
        private string _initials = string.Empty;
        private string _avatar = "ms-appx:///Assets/earth.png";
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

        private bool _useSenderColorForBubbles;

        public event Action<ChatStyle>? DisplayStyleChanged;
        public event Action<bool>? BubbleColorModeChanged;
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

        public string ColorUserName
        {
            get => _colorUserName;
            set
            {
                if (_colorUserName != value)
                {
                    _colorUserName = value;
                    OnPropertyChanged(nameof(ColorUserName));
                    Set("ColorUserName", value);
                    App.ChatService?.UpdateColorUserNameAsync(value);
                }
            }
        }

        public string Initials
        {
            get => _initials;
            set
            {
                if (_initials != value)
                {
                    _initials = value;
                    OnPropertyChanged(nameof(Initials));
                    Set("Initials", value);
                    if (App.MainWindow?.Content is FrameworkElement root &&
                        root.FindName("PersonPic") is Microsoft.UI.Xaml.Controls.PersonPicture pic)
                    {
                        pic.Initials = value;
                    }
                }
            }
        }

        public string Avatar
        {
            get => _avatar;
            set
            {
                if (_avatar != value)
                {
                    _avatar = value;
                    OnPropertyChanged(nameof(Avatar));
                    Set("Avatar", value);
                    App.ChatService?.UpdateAvatarAsync(value);
                }
            }
        }

        public bool UseSenderColorForBubbles
        {
            get => _useSenderColorForBubbles;
            set
            {
                if (_useSenderColorForBubbles != value)
                {
                    _useSenderColorForBubbles = value;
                    OnPropertyChanged(nameof(UseSenderColorForBubbles));
                    AppSettings.Set("UseSenderColorForBubbles", value ? "True" : "False");
                    BubbleColorModeChanged?.Invoke(value);
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
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_ctrlF9Exam != normalized)
                {
                    _ctrlF9Exam = normalized;
                    Logger.Log($"[SettingsViewModel] CtrlF9Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(CtrlF9Exam));
                    Set("CtrlF9Exam", normalized);
                }
            }
        }

        public string ShiftF9Exam
        {
            get => _shiftF9Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_shiftF9Exam != normalized)
                {
                    _shiftF9Exam = normalized;
                    Logger.Log($"[SettingsViewModel] ShiftF9Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(ShiftF9Exam));
                    Set("ShiftF9Exam", normalized);
                }
            }
        }

        public string CtrlF10Exam
        {
            get => _ctrlF10Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_ctrlF10Exam != normalized)
                {
                    _ctrlF10Exam = normalized;
                    Logger.Log($"[SettingsViewModel] CtrlF10Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(CtrlF10Exam));
                    Set("CtrlF10Exam", normalized);
                }
            }
        }

        public string ShiftF10Exam
        {
            get => _shiftF10Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_shiftF10Exam != normalized)
                {
                    _shiftF10Exam = normalized;
                    Logger.Log($"[SettingsViewModel] ShiftF10Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(ShiftF10Exam));
                    Set("ShiftF10Exam", normalized);
                }
            }
        }

        public string CtrlF11Exam
        {
            get => _ctrlF11Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_ctrlF11Exam != normalized)
                {
                    _ctrlF11Exam = normalized;
                    Logger.Log($"[SettingsViewModel] CtrlF11Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(CtrlF11Exam));
                    Set("CtrlF11Exam", normalized);
                }
            }
        }

        public string ShiftF11Exam
        {
            get => _shiftF11Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_shiftF11Exam != normalized)
                {
                    _shiftF11Exam = normalized;
                    Logger.Log($"[SettingsViewModel] ShiftF11Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(ShiftF11Exam));
                    Set("ShiftF11Exam", normalized);
                }
            }
        }

        public string CtrlF12Exam
        {
            get => _ctrlF12Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_ctrlF12Exam != normalized)
                {
                    _ctrlF12Exam = normalized;
                    Logger.Log($"[SettingsViewModel] CtrlF12Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(CtrlF12Exam));
                    Set("CtrlF12Exam", normalized);
                }
            }
        }

        public string ShiftF12Exam
        {
            get => _shiftF12Exam;
            set
            {
                var normalized = NormalizeExamValue(value);
                if (_shiftF12Exam != normalized)
                {
                    _shiftF12Exam = normalized;
                    Logger.Log($"[SettingsViewModel] ShiftF12Exam set to '{normalized}'.");
                    OnPropertyChanged(nameof(ShiftF12Exam));
                    Set("ShiftF12Exam", normalized);
                }
            }
        }

        public void Load()
        {
            Logger.Log("[SettingsViewModel] Loading settings.");
            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            _isOldSchoolMode = style == "OldSchool";
            if (double.TryParse(AppSettings.Get("MessageFontSize", "14"), out var size))
            {
                _messageFontSize = size;
            }
            _appTheme = AppSettings.Get("AppTheme", "Dark");
            ApplyTheme(_appTheme);
            _colorUserName = Get("ColorUserName");
            _initials = Get("Initials");
            _avatar = Get("Avatar");
            if (string.IsNullOrWhiteSpace(_initials))
            {
                _initials = string.Concat(App.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => char.ToUpperInvariant(s[0])));
            }
            _useSenderColorForBubbles = AppSettings.Get("UseSenderColorForBubbles", "False") == "True";

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

            _ctrlF9Exam = NormalizeExamValue(Get("CtrlF9Exam"));
            _shiftF9Exam = NormalizeExamValue(Get("ShiftF9Exam"));
            _ctrlF10Exam = NormalizeExamValue(Get("CtrlF10Exam"));
            _shiftF10Exam = NormalizeExamValue(Get("ShiftF10Exam"));
            _ctrlF11Exam = NormalizeExamValue(Get("CtrlF11Exam"));
            _shiftF11Exam = NormalizeExamValue(Get("ShiftF11Exam"));
            _ctrlF12Exam = NormalizeExamValue(Get("CtrlF12Exam"));
            _shiftF12Exam = NormalizeExamValue(Get("ShiftF12Exam"));

            ValidateExamSelections();

            // update resources with loaded values
            Application.Current.Resources["MessageFontSize"] = _messageFontSize;
            if (App.MainWindow?.Content is FrameworkElement root &&
                root.FindName("PersonPic") is Microsoft.UI.Xaml.Controls.PersonPicture pic)
            {
                pic.Initials = _initials;
            }
            OnPropertyChanged(string.Empty);
            Logger.Log("[SettingsViewModel] Settings loaded.");
        }

        private static string NormalizeExamValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public void ValidateExamSelections(IEnumerable<ExamOption> availableOptions)
        {
            ValidateExamSelectionsInternal(availableOptions);
        }

        private void ValidateExamSelections()
        {
            ValidateExamSelectionsInternal(ExamOption.Load());
        }

        private void ValidateExamSelectionsInternal(IEnumerable<ExamOption> availableOptions)
        {
            try
            {
                var validNames = BuildValidExamNameMap(availableOptions);

                ValidateExamSelection(_shiftF9Exam, value => ShiftF9Exam = value, validNames, nameof(ShiftF9Exam));
                ValidateExamSelection(_ctrlF9Exam, value => CtrlF9Exam = value, validNames, nameof(CtrlF9Exam));
                ValidateExamSelection(_shiftF10Exam, value => ShiftF10Exam = value, validNames, nameof(ShiftF10Exam));
                ValidateExamSelection(_ctrlF10Exam, value => CtrlF10Exam = value, validNames, nameof(CtrlF10Exam));
                ValidateExamSelection(_shiftF11Exam, value => ShiftF11Exam = value, validNames, nameof(ShiftF11Exam));
                ValidateExamSelection(_ctrlF11Exam, value => CtrlF11Exam = value, validNames, nameof(CtrlF11Exam));
                ValidateExamSelection(_shiftF12Exam, value => ShiftF12Exam = value, validNames, nameof(ShiftF12Exam));
                ValidateExamSelection(_ctrlF12Exam, value => CtrlF12Exam = value, validNames, nameof(CtrlF12Exam));
            }
            catch (Exception ex)
            {
                Logger.LogException("[SettingsViewModel] ValidateExamSelectionsInternal failed", ex);
            }
        }

        private static Dictionary<string, string> BuildValidExamNameMap(IEnumerable<ExamOption> options)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (options is null)
            {
                return map;
            }

            foreach (var option in options)
            {
                if (option is null)
                {
                    continue;
                }

                option.Normalize();

                var name = NormalizeExamValue(option.Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                map[name] = name;
                TryRegisterAlias(map, option.Description, name);
                TryRegisterAlias(map, option.CodeMSG, name);
                TryRegisterAlias(map, option.Annotation, name);
                TryRegisterAlias(map, option.EndAnnotation, name);
                TryRegisterAlias(map, option.Floor, name);
            }

            return map;
        }

        private static void TryRegisterAlias(IDictionary<string, string> map, string? value, string normalizedName)
        {
            var alias = NormalizeExamValue(value);
            if (string.IsNullOrWhiteSpace(alias) || map.ContainsKey(alias))
            {
                return;
            }

            map[alias] = normalizedName;
        }

        private static void ValidateExamSelection(string currentValue, Action<string> setter, IReadOnlyDictionary<string, string> validNames, string propertyName)
        {
            if (setter is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                Logger.Log($"[SettingsViewModel] {propertyName} is empty during validation.");
                return;
            }

            var lookupKey = NormalizeExamValue(currentValue);
            if (string.IsNullOrWhiteSpace(lookupKey))
            {
                Logger.Log($"[SettingsViewModel] Clearing exam shortcut because value '{currentValue}' is whitespace.");
                setter(string.Empty);
                return;
            }

            if (validNames.TryGetValue(lookupKey, out var normalized))
            {
                if (!string.Equals(currentValue, normalized, StringComparison.Ordinal))
                {
                    Logger.Log($"[SettingsViewModel] Normalized exam shortcut from '{currentValue}' to '{normalized}'.");
                    setter(normalized);
                }
                else
                {
                    Logger.Log($"[SettingsViewModel] {propertyName} validated with value '{normalized}'.");
                }
            }
            else
            {
                Logger.Log($"[SettingsViewModel] Exam shortcut value '{currentValue}' not found. Clearing selection.");
                setter(string.Empty);
            }
        }

        private static void ApplyTheme(string theme)
        {
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                //Application.Current.RequestedTheme = appTheme;

                if (App.MainWindow?.Content is FrameworkElement root)
                {
                    root.RequestedTheme =
                        appTheme == ApplicationTheme.Dark ?
                        ElementTheme.Dark :
                        ElementTheme.Light;
                }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
