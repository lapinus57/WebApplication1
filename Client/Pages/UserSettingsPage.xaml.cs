using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.ComponentModel;

namespace Client.Pages
{
    public sealed partial class UserSettingsPage : Page
    {
        public SettingsViewModel ViewModelSettings { get; } = new();
        public ObservableCollection<ExamOption> ExamOptions { get; } = ExamOption.Load();
        public ObservableCollection<ExamShortcutGroup> ExamShortcutGroups { get; } = new();

        private readonly ObservableCollection<string> _defaultAvatars = new();

        public UserSettingsPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModelSettings;
            this.Loaded += UserSettingsPage_Loaded;
            this.Unloaded += UserSettingsPage_Unloaded;
            Logger.Log($"[UserSettingsPage] Constructed with {ExamOptions.Count} cached exam option(s).");
            ViewModelSettings.Load();
            BuildExamShortcutGroups();
            Logger.Log($"[UserSettingsPage] Initialized with {ExamOptions.Count} cached exam option(s).");
            Debug.WriteLine($"[UserSettingsPage] ViewModel instance: {ViewModelSettings.GetHashCode()}");
        }

        private async void UserSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Log($"[UserSettingsPage] Loaded. Current shortcut groups: {ExamShortcutGroups.Count}.");
            App.ChatService.ExamOptionsUpdated += ChatService_ExamOptionsUpdated;
            Logger.Log("[UserSettingsPage] Page loaded.");
            await RefreshExamOptionsAsync();
        }

        private void UserSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Logger.Log("[UserSettingsPage] Unloaded.");
            App.ChatService.ExamOptionsUpdated -= ChatService_ExamOptionsUpdated;
            Logger.Log("[UserSettingsPage] Page unloaded.");
            DisposeExamShortcutGroups();
            ExamShortcutGroups.Clear();
        }

        private void BuildExamShortcutGroups()
        {
            DisposeExamShortcutGroups();
            ExamShortcutGroups.Clear();

            var root = ViewModelSettings;

            ExamShortcutGroups.Add(new ExamShortcutGroup(
                "F9",
                new[]
                {
                    CreateShortcutEntry(root, "Maj F9", nameof(SettingsViewModel.ShiftF9Exam), vm => vm.ShiftF9Exam, (vm, value) => vm.ShiftF9Exam = value),
                    CreateShortcutEntry(root, "Ctrl F9", nameof(SettingsViewModel.CtrlF9Exam), vm => vm.CtrlF9Exam, (vm, value) => vm.CtrlF9Exam = value)
                }));

            ExamShortcutGroups.Add(new ExamShortcutGroup(
                "F10",
                new[]
                {
                    CreateShortcutEntry(root, "Maj F10", nameof(SettingsViewModel.ShiftF10Exam), vm => vm.ShiftF10Exam, (vm, value) => vm.ShiftF10Exam = value),
                    CreateShortcutEntry(root, "Ctrl F10", nameof(SettingsViewModel.CtrlF10Exam), vm => vm.CtrlF10Exam, (vm, value) => vm.CtrlF10Exam = value)
                }));

            ExamShortcutGroups.Add(new ExamShortcutGroup(
                "F11",
                new[]
                {
                    CreateShortcutEntry(root, "Maj F11", nameof(SettingsViewModel.ShiftF11Exam), vm => vm.ShiftF11Exam, (vm, value) => vm.ShiftF11Exam = value),
                    CreateShortcutEntry(root, "Ctrl F11", nameof(SettingsViewModel.CtrlF11Exam), vm => vm.CtrlF11Exam, (vm, value) => vm.CtrlF11Exam = value)
                }));

            ExamShortcutGroups.Add(new ExamShortcutGroup(
                "F12",
                new[]
                {
                    CreateShortcutEntry(root, "Maj F12", nameof(SettingsViewModel.ShiftF12Exam), vm => vm.ShiftF12Exam, (vm, value) => vm.ShiftF12Exam = value),
                    CreateShortcutEntry(root, "Ctrl F12", nameof(SettingsViewModel.CtrlF12Exam), vm => vm.CtrlF12Exam, (vm, value) => vm.CtrlF12Exam = value)
                }));
        }

        private ExamShortcutEntry CreateShortcutEntry(
            SettingsViewModel root,
            string header,
            string propertyName,
            Func<SettingsViewModel, string> getter,
            Action<SettingsViewModel, string> setter)
        {
            return new ExamShortcutEntry(root, ExamOptions, header, propertyName, getter, setter);
        }

        private void DisposeExamShortcutGroups()
        {
            foreach (var group in ExamShortcutGroups)
            {
                group.Dispose();
            }
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
            foreach (var group in ExamShortcutGroups)
            {
                group.BeginExamOptionsUpdate();
            }

            try
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
            finally
            {
                foreach (var group in ExamShortcutGroups)
                {
                    group.EndExamOptionsUpdate();
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

    }

    public sealed class ExamShortcutGroup : IDisposable
    {
        public ExamShortcutGroup(string title, IEnumerable<ExamShortcutEntry> entries)
        {
            Title = title;
            Entries = new ObservableCollection<ExamShortcutEntry>(entries ?? Enumerable.Empty<ExamShortcutEntry>());
        }

        public string Title { get; }

        public ObservableCollection<ExamShortcutEntry> Entries { get; }

        public void Dispose()
        {
            foreach (var entry in Entries)
            {
                entry.Dispose();
            }
        }

        public void BeginExamOptionsUpdate()
        {
            foreach (var entry in Entries)
            {
                entry.BeginExamOptionsUpdate();
            }
        }

        public void EndExamOptionsUpdate()
        {
            foreach (var entry in Entries)
            {
                entry.EndExamOptionsUpdate();
            }
        }
    }

    public sealed class ExamShortcutEntry : INotifyPropertyChanged, IDisposable
    {
        private readonly Func<SettingsViewModel, string> _getter;
        private readonly Action<SettingsViewModel, string> _setter;
        private readonly string _viewModelPropertyName;

        public ExamShortcutEntry(
            SettingsViewModel root,
            ObservableCollection<ExamOption> availableExamOptions,
            string header,
            string viewModelPropertyName,
            Func<SettingsViewModel, string> getter,
            Action<SettingsViewModel, string> setter)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            AvailableExamOptions = availableExamOptions ?? throw new ArgumentNullException(nameof(availableExamOptions));
            Header = header;
            _viewModelPropertyName = viewModelPropertyName ?? string.Empty;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));

            Root.PropertyChanged += RootOnPropertyChanged;
        }

        public string Header { get; }

        public SettingsViewModel Root { get; }

        public ObservableCollection<ExamOption> AvailableExamOptions { get; }

        private bool _isUpdatingExamOptions;

        public string SelectedExam
        {
            get => _getter(Root);
            set
            {
                var normalized = NormalizeExamValue(value);
                var currentValue = _getter(Root);

                if (_isUpdatingExamOptions &&
                    string.IsNullOrEmpty(normalized) &&
                    !string.IsNullOrEmpty(currentValue))
                {
                    return;
                }

                if (!string.Equals(currentValue, normalized, StringComparison.Ordinal))
                {
                    _setter(Root, normalized);
                    OnPropertyChanged(nameof(SelectedExam));
                }
            }
        }

        private void RootOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, _viewModelPropertyName, StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(SelectedExam));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            Root.PropertyChanged -= RootOnPropertyChanged;
        }

        public void BeginExamOptionsUpdate()
        {
            _isUpdatingExamOptions = true;
        }

        public void EndExamOptionsUpdate()
        {
            _isUpdatingExamOptions = false;
            OnPropertyChanged(nameof(SelectedExam));
        }

        private static string NormalizeExamValue(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

