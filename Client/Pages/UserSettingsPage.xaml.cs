using Client.ViewModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Client.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Markup;

namespace Client.Pages
{
    public sealed partial class UserSettingsPage : Page
    {
        public SettingsViewModel ViewModelSettings { get; } = new();
        public ObservableCollection<ExamOption> ExamOptions { get; } = ExamOption.Load();

        private readonly ObservableCollection<string> _defaultAvatars = new();

        public UserSettingsPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModelSettings;
            ViewModelSettings.Load();
            Debug.WriteLine($"[UserSettingsPage] ViewModel instance: {ViewModelSettings.GetHashCode()}");
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
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Vertical' MaximumRowsOrColumns='6'/></ItemsPanelTemplate>");
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
}

