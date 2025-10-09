using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Client.Helpers;

namespace Client.Models
{
    public class UserInfo : INotifyPropertyChanged
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        private ObservableCollection<string> _rooms = new();
        private string _colorUserName = string.Empty;
        private string _accentAwareColorUserName = string.Empty;
        private bool _usesAccentContrast;

        public UserInfo()
        {
            _rooms.CollectionChanged += Rooms_CollectionChanged;
            UpdateAccentAwareColor();
        }

        public ObservableCollection<string> Rooms
        {
            get => _rooms;
            set
            {
                if (_rooms != value)
                {
                    if (_rooms != null)
                        _rooms.CollectionChanged -= Rooms_CollectionChanged;
                    _rooms = value ?? new ObservableCollection<string>();
                    _rooms.CollectionChanged += Rooms_CollectionChanged;
                    OnPropertyChanged(nameof(Rooms));
                    OnPropertyChanged(nameof(RoomsDisplay));
                    IsOnline = _rooms.Count > 0;
                }
            }
        }

        public string ColorUserName
        {
            get => _colorUserName;
            set
            {
                var newValue = value ?? string.Empty;
                if (_colorUserName != newValue)
                {
                    _colorUserName = newValue;
                    OnPropertyChanged(nameof(ColorUserName));
                    UpdateAccentAwareColor();
                }
            }
        }
        public string AccentAwareColorUserName
        {
            get => _accentAwareColorUserName;
            private set
            {
                if (_accentAwareColorUserName != value)
                {
                    _accentAwareColorUserName = value;
                    OnPropertyChanged(nameof(AccentAwareColorUserName));
                }
            }
        }
        public string DisplayName { get; set; } = string.Empty;

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline != value)
                {
                    _isOnline = value;
                    OnPropertyChanged(nameof(IsOnline));
                }
            }
        }

        public string Note { get; set; } = string.Empty;

        /// <summary>
        /// Convenience property returning <see cref="DisplayName"/> if set or
        /// <see cref="Username"/> otherwise.
        /// </summary>
        public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

        /// <summary>
        /// Returns a comma separated list of rooms the user is connected to.
        /// "A Tous" and "Secrétariat" never display the "Offline" label.
        /// </summary>
        public string RoomsDisplay
            => Username == "A Tous" || Username == "Secrétariat"
                ? string.Join(", ", Rooms)
                : Rooms.Count == 0 ? "Offline" : string.Join(", ", Rooms);

        private void Rooms_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            IsOnline = _rooms.Count > 0;
            OnPropertyChanged(nameof(RoomsDisplay));
        }

        public void RefreshAccentAwareColor()
        {
            UpdateAccentAwareColor();
        }

        public void SetAccentSelection(bool isSelected)
        {
            if (_usesAccentContrast != isSelected)
            {
                _usesAccentContrast = isSelected;
                UpdateAccentAwareColor();
            }
        }

        private void UpdateAccentAwareColor()
        {
            if (!_usesAccentContrast || string.IsNullOrWhiteSpace(_colorUserName))
            {
                AccentAwareColorUserName = _colorUserName;
                return;
            }

            var colors = AppSettings.GetObject<AppColorSettings>("Colors");
            var accent = ColorUtils.FromHex(colors.TitleBarColor);
            var original = ColorUtils.FromHex(_colorUserName);
            var adjusted = ColorUtils.EnsureContrast(original, accent);
            AccentAwareColorUserName = ColorUtils.ToHex(adjusted);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // Propriétés manquantes pour la correction XLS0432
        public bool CanRenameLocalUser { get; set; }

        public UserInfo Clone()
        {
            var clone = new UserInfo
            {
                ConnectionId = ConnectionId,
                Username = Username,
                DisplayName = DisplayName,
                Avatar = Avatar,
                ColorUserName = ColorUserName,
                Note = Note,
                IsOnline = IsOnline,
                CanRenameLocalUser = CanRenameLocalUser
            };

            var roomsToCopy = Rooms?.Where(room => !string.IsNullOrWhiteSpace(room))
                ?? Enumerable.Empty<string>();

            clone.Rooms = new ObservableCollection<string>(roomsToCopy);
            clone.SetAccentSelection(_usesAccentContrast);

            return clone;
        }
    }
}
