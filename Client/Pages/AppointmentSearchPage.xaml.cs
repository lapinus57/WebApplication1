using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Client.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages
{
    public sealed partial class AppointmentSearchPage : Page
    {
        private AppointmentSearchConfig? _config;
        private MachineConfig _machineConfig = MachineConfig.Load();
        private CancellationTokenSource? _searchToken;
        private bool _suppressHorizonUpdate;
        private readonly ISchoolHolidayService _holidayService = new SchoolHolidayService();
        private int _searchOffsetMonths;

        public ObservableCollection<AppointmentSearchResult> Results { get; } = new();

        public AppointmentSearchPage()
        {
            InitializeComponent();
            Loaded += AppointmentSearchPage_Loaded;
            Unloaded += AppointmentSearchPage_Unloaded;
        }

        private async void AppointmentSearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadConfigAsync();
            if (ReferenceDatePicker.Date is null)
            {
                ReferenceDatePicker.Date = DateTimeOffset.Now.AddMonths(1);
            }
        }

        private void AppointmentSearchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _searchToken?.Cancel();
            _searchToken?.Dispose();
            _searchToken = null;
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                var cfg = await App.ChatService.GetAppointmentSearchConfigAsync();
                _config = cfg ?? new AppointmentSearchConfig();
                if (!_config.IsValid())
                {
                    StatusText.Text = "Configuration incomplète. Veuillez vérifier les paramètres système.";
                }
                else
                {
                    StatusText.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppointmentSearch] LoadConfig", ex, "CLI41");
                StatusText.Text = "Impossible de charger la configuration (voir les journaux).";
            }
        }

        private async Task RunSearchAsync(bool allowOverload, bool resetOffset)
        {
            if (_config == null || !_config.IsValid())
            {
                await ShowDialogAsync("Configuration manquante", "Les paramètres de connexion à la base Access ne sont pas définis.");
                return;
            }

            if (ReferenceDatePicker.Date is null)
            {
                await ShowDialogAsync("Date manquante", "Veuillez sélectionner une date de référence.");
                return;
            }

            _machineConfig = MachineConfig.Load();

            if (resetOffset)
            {
                _searchOffsetMonths = 0;
            }

            _searchToken?.Cancel();
            _searchToken?.Dispose();
            _searchToken = new CancellationTokenSource();

            var anchorDate = ReferenceDatePicker.Date.Value.Date;
            var mode = GetSelectedMode();
            var canClimb = ClimbToggle.IsOn;
            var isFo = FoToggle.IsOn;
            var token = _searchToken.Token;
            var filters = BuildFilters();

            ToggleInputs(false);
            StatusText.Text = "Recherche en cours...";
            Results.Clear();

            try
            {
                var service = new AppointmentSearchService(_config, _machineConfig, _holidayService);
                var slots = await service.FindAvailableSlotsAsync(anchorDate, mode, canClimb, isFo, allowOverload, filters, _searchOffsetMonths, token);
                var groupedResults = AppointmentGroupBuilder.BuildResults(slots, filters);

                foreach (var result in groupedResults)
                {
                    Results.Add(result);
                }

                if (groupedResults.Any())
                {
                    var offsetText = _searchOffsetMonths > 0
                        ? $" (recherche décalée de {_searchOffsetMonths} mois)"
                        : string.Empty;
                    StatusText.Text = $"{groupedResults.Count} proposition(s) trouvée(s){offsetText}.";
                }
                else
                {
                    StatusText.Text = "Aucun créneau ne respecte les contraintes.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Recherche annulée.";
            }
            catch (Exception ex)
            {
                Logger.LogException("[AppointmentSearch] RunSearch", ex, "CLI42");
                StatusText.Text = "Erreur pendant la recherche (voir les journaux).";
                await ShowDialogAsync("Erreur", ex.Message);
            }
            finally
            {
                ToggleInputs(true);
            }
        }

        private void ToggleInputs(bool isEnabled)
        {
            HorizonCombo.IsEnabled = isEnabled;
            ReferenceDatePicker.IsEnabled = isEnabled;
            ModeCombo.IsEnabled = isEnabled;
            ClimbToggle.IsEnabled = isEnabled;
            FoToggle.IsEnabled = isEnabled;
            SearchButton.IsEnabled = isEnabled;
            SearchOverloadButton.IsEnabled = isEnabled;
            PersonCountCombo.IsEnabled = isEnabled;
            DayPreferenceCombo.IsEnabled = isEnabled;
            DayPartCombo.IsEnabled = isEnabled;
            HolidayCombo.IsEnabled = isEnabled;
            HolidayZoneCombo.IsEnabled = isEnabled;
            SearchFurtherButton.IsEnabled = isEnabled;
        }

        private SearchMode GetSelectedMode()
        {
            if (ModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<SearchMode>(tag, out var mode))
                {
                    return mode;
                }
            }
            return SearchMode.Around;
        }

        private AppointmentSearchFilters BuildFilters()
        {
            var filters = new AppointmentSearchFilters
            {
                PersonCount = GetSelectedPersonCount(),
                PreferredDay = GetPreferredDay(),
                TimePreference = GetTimePreference(),
                HolidayFilter = GetHolidayFilter(),
                HolidayZone = GetHolidayZone()
            };
            return filters;
        }

        private int GetSelectedPersonCount()
        {
            if (PersonCountCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var count))
            {
                return Math.Clamp(count, 1, 4);
            }

            return 1;
        }

        private DayOfWeek? GetPreferredDay()
        {
            if (DayPreferenceCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (string.Equals(tag, "Any", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (Enum.TryParse<DayOfWeek>(tag, true, out var day))
                    return day;
            }

            return null;
        }

        private TimeOfDayPreference GetTimePreference()
        {
            if (DayPartCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<TimeOfDayPreference>(tag, true, out var preference))
                {
                    return preference;
                }
            }

            return TimeOfDayPreference.Any;
        }

        private SchoolHolidayFilter GetHolidayFilter()
        {
            if (HolidayCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<SchoolHolidayFilter>(tag, true, out var filter))
                {
                    return filter;
                }
            }

            return SchoolHolidayFilter.Any;
        }

        private SchoolHolidayZone GetHolidayZone()
        {
            if (HolidayZoneCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<SchoolHolidayZone>(tag, true, out var zone))
                {
                    return zone;
                }
            }

            return SchoolHolidayZone.Any;
        }

        private void HorizonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HorizonCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag.Equals("custom", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (int.TryParse(tag, out var months))
                {
                    if (ReferenceDatePicker is null)
                    {
                        return;
                    }

                    _suppressHorizonUpdate = true;
                    ReferenceDatePicker.Date = DateTimeOffset.Now.AddMonths(months);
                    _suppressHorizonUpdate = false;
                }
            }
        }

        private void ReferenceDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (_suppressHorizonUpdate)
                return;

            if (args.NewDate != null && HorizonCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && !tag.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                HorizonCombo.SelectedIndex = HorizonCombo.Items.Count - 1;
            }
        }

        private async Task ShowDialogAsync(string title, string message)
        {
            if (Content is not FrameworkElement root)
                return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Fermer",
                XamlRoot = root.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await RunSearchAsync(false, true);
        }

        private async void SearchOverload_Click(object sender, RoutedEventArgs e)
        {
            await RunSearchAsync(true, true);
        }

        private async void SearchFurther_Click(object sender, RoutedEventArgs e)
        {
            _searchOffsetMonths++;
            await RunSearchAsync(false, false);
        }
    }
}
