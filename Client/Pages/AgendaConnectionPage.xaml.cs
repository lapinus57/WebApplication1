using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages
{
    public sealed partial class AgendaConnectionPage : Page, INotifyPropertyChanged
    {
        private readonly MachineConfig _config;
        private bool _agendaModeEnabled;
        private bool _autoSwitchEnabled;

        public ObservableCollection<AgendaSwitchEntry> ScheduleEntries { get; } = new();
        public ObservableCollection<string> Users { get; } = new();
        public List<DayOption> DayOptions { get; } = new();

        public bool AgendaModeEnabled
        {
            get => _agendaModeEnabled;
            set => SetProperty(ref _agendaModeEnabled, value);
        }

        public bool AutoSwitchEnabled
        {
            get => _autoSwitchEnabled;
            set => SetProperty(ref _autoSwitchEnabled, value);
        }

        public AgendaConnectionPage()
        {
            InitializeComponent();
            _config = MachineConfig.Load();
            _agendaModeEnabled = _config.AgendaModeEnabled;
            _autoSwitchEnabled = _config.AutoSwitchEnabled;
            InitializeDayOptions();
            LoadSchedule();
            DataContext = this;
            Loaded += AgendaConnectionPage_Loaded;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private async void AgendaConnectionPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUsersAsync();
        }

        private void InitializeDayOptions()
        {
            DayOptions.Clear();
            DayOptions.Add(new DayOption(DayOfWeek.Monday, "Lundi"));
            DayOptions.Add(new DayOption(DayOfWeek.Tuesday, "Mardi"));
            DayOptions.Add(new DayOption(DayOfWeek.Wednesday, "Mercredi"));
            DayOptions.Add(new DayOption(DayOfWeek.Thursday, "Jeudi"));
            DayOptions.Add(new DayOption(DayOfWeek.Friday, "Vendredi"));
            DayOptions.Add(new DayOption(DayOfWeek.Saturday, "Samedi"));
            DayOptions.Add(new DayOption(DayOfWeek.Sunday, "Dimanche"));
        }

        private void LoadSchedule()
        {
            ScheduleEntries.Clear();
            if (_config.AgendaSchedule != null)
            {
                foreach (var entry in _config.AgendaSchedule)
                {
                    ScheduleEntries.Add(entry);
                }
            }
        }

        private async Task LoadUsersAsync()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var names = new List<string>();

            if (App.ChatService is not null)
            {
                try
                {
                    var localUsers = await App.ChatService.LoadUsersFromDiskAsync();
                    names = localUsers
                        .Select(u => u.Username?.Trim())
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(comparer)
                        .OrderBy(u => u, comparer)
                        .ToList();
                }
                catch (Exception ex)
                {
                    Logger.LogException("[AgendaConnectionPage] Unable to load users", ex, "CLI47");
                }
            }

            if (names.Count == 0)
            {
                names = LoadUsernamesFromSettingsFiles();
            }

            UpdateUsersCollection(names);
        }

        private void UpdateUsersCollection(IEnumerable<string> names)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var finalNames = names
                .Select(name => name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(comparer)
                .ToList();

            foreach (var entry in ScheduleEntries)
            {
                var user = entry.UserName?.Trim();
                if (!string.IsNullOrWhiteSpace(user) && !finalNames.Any(name => comparer.Equals(name, user)))
                {
                    finalNames.Add(user);
                }
            }

            finalNames.Sort(comparer);

            Users.Clear();
            foreach (var user in finalNames)
            {
                Users.Add(user);
            }
        }

        private static List<string> LoadUsernamesFromSettingsFiles()
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                if (!Directory.Exists(folder))
                {
                    return new List<string>();
                }

                return Directory
                    .GetFiles(folder, "*_settings.json")
                    .Select(file => Path.GetFileName(file).Replace("_settings.json", string.Empty).Trim())
                    .Select(AppSettings.SanitizeUserNameForFile)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.LogException("[AgendaConnectionPage] Unable to load local users", ex, "CLI48");
                return new List<string>();
            }
        }

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            var firstUser = Users.FirstOrDefault() ?? string.Empty;
            var entry = new AgendaSwitchEntry
            {
                Day = DayOfWeek.Monday,
                StartTime = TimeSpan.FromHours(8),
                EndTime = TimeSpan.FromHours(18),
                UserName = firstUser
            };

            ScheduleEntries.Add(entry);
        }

        private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AgendaSwitchEntry entry)
            {
                ScheduleEntries.Remove(entry);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var latestConfig = MachineConfig.Load();
            _config.AgendaModeEnabled = AgendaModeEnabled;
            AutoSwitchEnabled = latestConfig.AutoSwitchEnabled;
            _config.AutoSwitchEnabled = AgendaModeEnabled && AutoSwitchEnabled;
            _config.AgendaSchedule = ScheduleEntries
                .Select(entry => new AgendaSwitchEntry
                {
                    Day = entry.Day,
                    StartTime = entry.StartTime,
                    EndTime = entry.EndTime,
                    UserName = entry.UserName?.Trim() ?? string.Empty,
                    IsAllDay = entry.IsAllDay
                })
                .ToList();

            MachineConfig.Save(_config);

            if (Application.Current is App app)
            {
                app.RefreshAgendaTimer();
                if (App.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshAgendaSwitchState();
                }
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            if (propertyName == nameof(AgendaModeEnabled) && !AgendaModeEnabled)
            {
                AutoSwitchEnabled = false;
            }
        }

        public class DayOption
        {
            public DayOption(DayOfWeek day, string label)
            {
                Day = day;
                Label = label;
            }

            public DayOfWeek Day { get; set; }
            public string Label { get; set; }
        }
    }
}
