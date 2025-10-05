using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Client.ViewModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System;
using System.IO;
using Microsoft.UI.Xaml.Markup;

namespace Client.Pages
{
    public sealed partial class UserSettingsPage : Page
    {
        public SettingsViewModel ViewModelSettings { get; } = new();
        public ObservableCollection<ExamOption> ExamOptions { get; } = ExamOption.Load();
        public ObservableCollection<ExamShortcutGroup> ExamShortcutGroups { get; } = new();

        private readonly ObservableCollection<string> _defaultAvatars = new();
        private readonly Dictionary<string, ExamShortcutEntry> _shortcutEntryMap = new(StringComparer.OrdinalIgnoreCase);

        public UserSettingsPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModelSettings;
            this.Loaded += UserSettingsPage_Loaded;
            this.Unloaded += UserSettingsPage_Unloaded;
            Logger.Log($"[UserSettingsPage] Constructed with {ExamOptions.Count} cached exam option(s).");
            ViewModelSettings.Load();
            Logger.Log($"[UserSettingsPage] Initialized with {ExamOptions.Count} cached exam option(s).");
            Debug.WriteLine($"[UserSettingsPage] ViewModel instance: {ViewModelSettings.GetHashCode()}");
        }

        private async void UserSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Log($"[UserSettingsPage] Loaded. Current shortcut groups: {ExamShortcutGroups.Count}.");
            ViewModelSettings.PropertyChanged -= ViewModelSettings_PropertyChanged;
            ViewModelSettings.PropertyChanged += ViewModelSettings_PropertyChanged;
            App.ChatService.ExamOptionsUpdated += ChatService_ExamOptionsUpdated;
            Logger.Log("[UserSettingsPage] Page loaded.");
            await RefreshExamOptionsAsync();
        }

        private void UserSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Logger.Log("[UserSettingsPage] Unloaded.");
            App.ChatService.ExamOptionsUpdated -= ChatService_ExamOptionsUpdated;
            Logger.Log("[UserSettingsPage] Page unloaded.");
        }

        private void ChatService_ExamOptionsUpdated(IEnumerable<ExamOption> options)
        {
            if (options is null)
            {
                return;
            }

            Logger.Log($"[UserSettingsPage] Received ExamOptionsUpdated event with {options.Count()} option(s).");
            DispatcherQueue?.TryEnqueue(() => UpdateExamOptions(options));
        }

        private async Task RefreshExamOptionsAsync()
        {
            try
            {
                Logger.Log("[UserSettingsPage] RefreshExamOptionsAsync starting.");
                var serverOptions = await App.ChatService.GetExamOptionsAsync();
                if (serverOptions?.Any() == true)
                {
                    Logger.Log($"[UserSettingsPage] Loaded {serverOptions.Count()} exam option(s) from server.");
                    UpdateExamOptions(serverOptions);
                }
                else
                {
                    Logger.Log("[UserSettingsPage] No server exam options received. Validating local selections only.");
                    ViewModelSettings.ValidateExamSelections(ExamOptions);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("UserSettingsPage.RefreshExamOptionsAsync failed", ex);
            }
        }

        private void UpdateExamOptions(IEnumerable<ExamOption> options)
        {
            var sanitized = options
                .Where(option => option is not null)
                .Select(option =>
                {
                    option.Normalize();
                    return option;
                })
                .OrderBy(option => option.Index)
                .ToList();

            ExamOptions.Clear();
            foreach (var option in sanitized)
            {
                ExamOptions.Add(option);
            }

            Logger.Log($"[UserSettingsPage] Exam options updated. Total count: {ExamOptions.Count}.");
            ExamOption.Save(ExamOptions);
            ViewModelSettings.ValidateExamSelections(ExamOptions);
            Logger.Log($"[UserSettingsPage] Exam options updated. Count={ExamOptions.Count}.");
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void ChangeAvatar_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[UserSettingsPage] ChangeAvatar_Click invoked.");
            var serverAvatars = await App.ChatService.GetAvailableAvatarsAsync();
            _defaultAvatars.Clear();
            foreach (var avatar in serverAvatars.Distinct())
            {
                if (!_defaultAvatars.Contains(avatar))
                    _defaultAvatars.Add(avatar);
            }

            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Avatars", CreationCollisionOption.OpenIfExists);
            var files = await folder.GetFilesAsync();
            foreach (var file in files)
            {
                var path = $"ms-appdata:///local/Avatars/{file.Name}";
                if (!_defaultAvatars.Contains(path))
                    _defaultAvatars.Add(path);
            }

            if (!string.IsNullOrEmpty(ViewModelSettings.Avatar) && !_defaultAvatars.Contains(ViewModelSettings.Avatar))
                _defaultAvatars.Add(ViewModelSettings.Avatar);

            var dialog = new ContentDialog
            {
                Title = "Choisir un avatar",
                PrimaryButtonText = "Valider",
                CloseButtonText = "Annuler",
                XamlRoot = this.XamlRoot
            };

            var grid = new GridView
            {
                ItemsSource = _defaultAvatars,
                SelectionMode = ListViewSelectionMode.Single,
                ItemTemplate = (DataTemplate)this.Resources["AvatarTemplate"],
                MaxHeight = 200
            };
            grid.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Vertical' MaximumRowsOrColumns='9'/></ItemsPanelTemplate>");
            grid.SelectedIndex = _defaultAvatars.IndexOf(ViewModelSettings.Avatar);

            var importButton = new Button { Content = "Importer une image..." };
            importButton.Click += async (s, args) => await ImportAvatarAsync(grid);

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(grid);
            panel.Children.Add(importButton);

            dialog.Content = panel;
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (grid.SelectedItem is string avatar)
                    ViewModelSettings.Avatar = avatar;
            }
        }

        private async Task ImportAvatarAsync(ListViewBase list)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                Logger.Log($"[UserSettingsPage] Importing avatar '{file.Name}'.");
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Avatars", CreationCollisionOption.OpenIfExists);
                var newFile = await file.CopyAsync(folder, file.Name, NameCollisionOption.GenerateUniqueName);

                using var stream = await newFile.OpenStreamForReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                var serverPath = await App.ChatService.UploadAvatarAsync(newFile.Name, base64);
                string path = string.IsNullOrEmpty(serverPath)
                    ? $"ms-appdata:///local/Avatars/{newFile.Name}"
                    : $"{App.ChatService.ServerAddress}{serverPath}";

                ViewModelSettings.Avatar = path;
                if (!_defaultAvatars.Contains(path))
                    _defaultAvatars.Add(path);
                list.SelectedItem = null;
            }
        }

        private void InitializeShortcutGroups()
        {
            ExamShortcutGroups.Clear();
            _shortcutEntryMap.Clear();

            ExamShortcutGroups.Add(new ExamShortcutGroup("F9", new[]
            {
                CreateShortcutEntry("Maj F9", nameof(SettingsViewModel.ShiftF9Exam), () => ViewModelSettings.ShiftF9Exam, value => ViewModelSettings.ShiftF9Exam = value),
                CreateShortcutEntry("Ctrl F9", nameof(SettingsViewModel.CtrlF9Exam), () => ViewModelSettings.CtrlF9Exam, value => ViewModelSettings.CtrlF9Exam = value)
            }));

            ExamShortcutGroups.Add(new ExamShortcutGroup("F10", new[]
            {
                CreateShortcutEntry("Maj F10", nameof(SettingsViewModel.ShiftF10Exam), () => ViewModelSettings.ShiftF10Exam, value => ViewModelSettings.ShiftF10Exam = value),
                CreateShortcutEntry("Ctrl F10", nameof(SettingsViewModel.CtrlF10Exam), () => ViewModelSettings.CtrlF10Exam, value => ViewModelSettings.CtrlF10Exam = value)
            }));

            ExamShortcutGroups.Add(new ExamShortcutGroup("F11", new[]
            {
                CreateShortcutEntry("Maj F11", nameof(SettingsViewModel.ShiftF11Exam), () => ViewModelSettings.ShiftF11Exam, value => ViewModelSettings.ShiftF11Exam = value),
                CreateShortcutEntry("Ctrl F11", nameof(SettingsViewModel.CtrlF11Exam), () => ViewModelSettings.CtrlF11Exam, value => ViewModelSettings.CtrlF11Exam = value)
            }));

            ExamShortcutGroups.Add(new ExamShortcutGroup("F12", new[]
            {
                CreateShortcutEntry("Maj F12", nameof(SettingsViewModel.ShiftF12Exam), () => ViewModelSettings.ShiftF12Exam, value => ViewModelSettings.ShiftF12Exam = value),
                CreateShortcutEntry("Ctrl F12", nameof(SettingsViewModel.CtrlF12Exam), () => ViewModelSettings.CtrlF12Exam, value => ViewModelSettings.CtrlF12Exam = value)
            }));

            Logger.Log($"[UserSettingsPage] Initialized {ExamShortcutGroups.Count} shortcut group(s).");
        }

        private ExamShortcutEntry CreateShortcutEntry(string label, string propertyName, Func<string> getter, Action<string> setter)
        {
            var entry = new ExamShortcutEntry(label, getter, setter);
            _shortcutEntryMap[propertyName] = entry;
            return entry;
        }

        private void RefreshShortcutEntries()
        {
            foreach (var entry in _shortcutEntryMap.Values)
            {
                entry.Refresh();
            }
        }

        private void ViewModelSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                Logger.Log("[UserSettingsPage] ViewModelSettings signaled full refresh.");
                RefreshShortcutEntries();
                return;
            }

            if (_shortcutEntryMap.TryGetValue(e.PropertyName, out var entry))
            {
                Logger.Log($"[UserSettingsPage] ViewModel property '{e.PropertyName}' changed. Refreshing shortcut entry.");
                entry.Refresh();
            }
        }
    }
    public sealed class ExamShortcutGroup
    {
        public ExamShortcutGroup(string title, IEnumerable<ExamShortcutEntry> shortcuts)
        {
            Title = title;
            Shortcuts = new ObservableCollection<ExamShortcutEntry>(shortcuts ?? Enumerable.Empty<ExamShortcutEntry>());
        }

        public string Title { get; }

        public ObservableCollection<ExamShortcutEntry> Shortcuts { get; }
    }

    public sealed class ExamShortcutEntry : INotifyPropertyChanged
    {
        private readonly Func<string> _getter;
        private readonly Action<string> _setter;

        public ExamShortcutEntry(string label, Func<string> getter, Action<string> setter)
        {
            Label = label;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public string Label { get; }

        public string Value
        {
            get => _getter();
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
                if (!string.Equals(_getter(), sanitized, StringComparison.Ordinal))
                {
                    _setter(sanitized);
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh()
        {
            OnPropertyChanged(nameof(Value));
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

