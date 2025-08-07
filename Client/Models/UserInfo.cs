using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Client.Models
{
    public class UserInfo : INotifyPropertyChanged
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        private ObservableCollection<string> _rooms = new();

        public UserInfo()
        {
            _rooms.CollectionChanged += Rooms_CollectionChanged;
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
                }
            }
        }

        public string ColorUserName { get; set; } = string.Empty;
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
        /// </summary>
        public string RoomsDisplay => Rooms.Count == 0 ? string.Empty : string.Join(", ", Rooms);

        private void Rooms_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(RoomsDisplay));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    }
}
