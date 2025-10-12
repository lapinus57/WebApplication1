namespace Client.Models
{
    public class AppColorSettings
    {
        private const string DefaultTitleBarColor = "#FF0078D7";
        private const string DefaultTextTitleBarColor = "#FFFFFFFF";
        private const string DefaultNavigationViewColor = "#FFE6F1FF";
        private const string DefaultTextNavigationViewColor = "#FF000000";
        private const string DefaultMyMessageColor = "#FFCCE5FF";
        private const string DefaultTextMyMessageColor = "#FF000000";
        private const string DefaultOtherMessageColor = "#FFD9F2DC";
        private const string DefaultTextOtherMessageColor = "#FF000000";
        private const string DefaultAppBackgroundColor = "#FFFFFFFF";
        private const string DefaultTextAppBackgroundColor = "#FF000000";
        private const string DefaultSystemAccentColorDark1 = "#FF0078D7";

        private string _titleBarColor = DefaultTitleBarColor;
        private string _textTitleBarColor = DefaultTextTitleBarColor;
        private string _navigationViewColor = DefaultNavigationViewColor;
        private string _textNavigationViewColor = DefaultTextNavigationViewColor;
        private string _myMessageColor = DefaultMyMessageColor;
        private string _textMyMessageColor = DefaultTextMyMessageColor;
        private string _otherMessageColor = DefaultOtherMessageColor;
        private string _textOtherMessageColor = DefaultTextOtherMessageColor;
        private string _appBackgroundColor = DefaultAppBackgroundColor;
        private string _textAppBackgroundColor = DefaultTextAppBackgroundColor;
        private string _systemAccentColorDark1 = DefaultSystemAccentColorDark1;

        public string TitleBarColor
        {
            get => Normalize(_titleBarColor, DefaultTitleBarColor);
            set => _titleBarColor = Normalize(value, DefaultTitleBarColor);
        }

        public string TextTitleBarColor
        {
            get => Normalize(_textTitleBarColor, DefaultTextTitleBarColor);
            set => _textTitleBarColor = Normalize(value, DefaultTextTitleBarColor);
        }

        public string NavigationViewColor
        {
            get => Normalize(_navigationViewColor, DefaultNavigationViewColor);
            set => _navigationViewColor = Normalize(value, DefaultNavigationViewColor);
        }

        public string TextNavigationViewColor
        {
            get => Normalize(_textNavigationViewColor, DefaultTextNavigationViewColor);
            set => _textNavigationViewColor = Normalize(value, DefaultTextNavigationViewColor);
        }

        public string MyMessageColor
        {
            get => Normalize(_myMessageColor, DefaultMyMessageColor);
            set => _myMessageColor = Normalize(value, DefaultMyMessageColor);
        }

        public string TextMyMessageColor
        {
            get => Normalize(_textMyMessageColor, DefaultTextMyMessageColor);
            set => _textMyMessageColor = Normalize(value, DefaultTextMyMessageColor);
        }

        public string OtherMessageColor
        {
            get => Normalize(_otherMessageColor, DefaultOtherMessageColor);
            set => _otherMessageColor = Normalize(value, DefaultOtherMessageColor);
        }

        public string TextOtherMessageColor
        {
            get => Normalize(_textOtherMessageColor, DefaultTextOtherMessageColor);
            set => _textOtherMessageColor = Normalize(value, DefaultTextOtherMessageColor);
        }

        public string AppBackgroundColor
        {
            get => Normalize(_appBackgroundColor, DefaultAppBackgroundColor);
            set => _appBackgroundColor = Normalize(value, DefaultAppBackgroundColor);
        }

        public string TextAppBackgroundColor
        {
            get => Normalize(_textAppBackgroundColor, DefaultTextAppBackgroundColor);
            set => _textAppBackgroundColor = Normalize(value, DefaultTextAppBackgroundColor);
        }

        public string SystemAccentColorDark1
        {
            get => Normalize(_systemAccentColorDark1, DefaultSystemAccentColorDark1);
            set => _systemAccentColorDark1 = Normalize(value, DefaultSystemAccentColorDark1);
        }

        private static string Normalize(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
