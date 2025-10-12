using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using Client.Helpers;
using System.Text.RegularExpressions;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace Client.Pages
{
    public sealed partial class ExamRoomPage : Page
    {
        public ObservableCollection<ExamOption> Options { get; } = ExamOption.Load();
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();

        private bool _syncing;

        public ExamRoomPage()
        {
            this.InitializeComponent();
            foreach (var opt in Options)
                opt.PropertyChanged += Option_PropertyChanged;

            Options.CollectionChanged += Options_CollectionChanged;
            Rooms.CollectionChanged += Rooms_CollectionChanged;
            this.Loaded += ExamRoomPage_Loaded;
            this.Unloaded += ExamRoomPage_Unloaded;
        }

        private void ExamRoomPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.ChatService.ExamOptionsUpdated -= ChatService_ExamOptionsUpdated;
            App.ChatService.RoomsUpdated -= ChatService_RoomsUpdated;
        }

        private void DeleteExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExamOption opt)
            {
                Options.Remove(opt);
            }
        }

        private void DuplicateExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExamOption opt)
            {
                var duplicate = new ExamOption
                {
                    Color = opt.Color,
                    Name = GenerateDuplicateName(opt.Name),
                    Description = opt.Description,
                    CodeMSG = opt.CodeMSG,
                    Annotation = opt.Annotation,
                    EndAnnotation = opt.EndAnnotation,
                    Floor = opt.Floor
                };

                var index = Options.IndexOf(opt);
                if (index >= 0)
                {
                    Options.Insert(index + 1, duplicate);
                }
                else
                {
                    Options.Add(duplicate);
                }
            }
        }

        private string GenerateDuplicateName(string originalName)
        {
            var baseName = originalName.Trim();
            var match = Regex.Match(baseName, @"^(.*)\s\((\d+)\)$");
            if (match.Success)
            {
                baseName = match.Groups[1].Value;
            }

            var counter = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} ({counter})";
                counter++;
            }
            while (Options.Any(o => string.Equals(o.Name, candidate, StringComparison.OrdinalIgnoreCase)));

            return candidate;
        }

        private void MoveExamUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExamOption opt)
            {
                var index = Options.IndexOf(opt);
                if (index > 0)
                {
                    Options.Move(index, index - 1);
                }
            }
        }

        private void MoveExamDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExamOption opt)
            {
                var index = Options.IndexOf(opt);
                if (index >= 0 && index < Options.Count - 1)
                {
                    Options.Move(index, index + 1);
                }
            }
        }
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            Options.Add(new ExamOption
            {
                Index = Options.Count + 1,
                Color = "#FF0000", // Default color red
                Name = "Nouvel examen",
                Description = "Nouvel examen",
                CodeMSG = "examen",
                Annotation = string.Empty,
                EndAnnotation = string.Empty,
                Floor = "tes"


            });
        }
        private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (sender.DataContext is ExamOption option)
            {
                option.Color = ColorUtils.ToHex(args.NewColor);
            }
        }

        private void AddRoom_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("NewRoomBox") is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                Rooms.Add(tb.Text);
                tb.Text = string.Empty;
            }
        }
        private void RemoveRoom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string room)
            {
                Rooms.Remove(room);
            }
        }

        private async void LoadServer_Click(object sender, RoutedEventArgs e)
        {
            await LoadConfigFromServerAsync();
        }

        private async void SendServer_Click(object sender, RoutedEventArgs e)
        {
            await SendConfigToServerAsync();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Configuration EyeChat", new List<string> { ".eyechatconfig" });
            picker.SuggestedFileName = $"EyeChatConfig_{DateTime.Now:yyyyMMdd_HHmm}";
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            try
            {
                var configuration = new ExamRoomConfiguration
                {
                    Exams = Options.ToList(),
                    Rooms = Rooms.ToList()
                };

                var json = JsonConvert.SerializeObject(configuration, Formatting.Indented);
                CachedFileManager.DeferUpdates(file);
                await FileIO.WriteTextAsync(file, json);
                await CachedFileManager.CompleteUpdatesAsync(file);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d'export", $"Impossible d'enregistrer la configuration : {ex.Message}");
            }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".eyechatconfig");
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var configuration = JsonConvert.DeserializeObject<ExamRoomConfiguration>(json);
                if (configuration == null)
                {
                    await ShowMessageAsync("Import invalide", "Le fichier sélectionné est vide ou invalide.");
                    return;
                }

                _syncing = true;

                BackupFile(ExamOption.FilePath);
                Options.CollectionChanged -= Options_CollectionChanged;
                foreach (var option in Options)
                    option.PropertyChanged -= Option_PropertyChanged;
                Options.Clear();
                if (configuration.Exams != null)
                {
                    foreach (var opt in configuration.Exams.OrderBy(o => o.Index))
                    {
                        Options.Add(opt);
                        opt.PropertyChanged += Option_PropertyChanged;
                    }
                }
                int index = 1;
                foreach (var opt in Options)
                    opt.Index = index++;
                Options.CollectionChanged += Options_CollectionChanged;
                ExamOption.Save(Options);

                BackupFile(RoomList.FilePath);
                Rooms.CollectionChanged -= Rooms_CollectionChanged;
                Rooms.Clear();
                if (configuration.Rooms != null)
                {
                    foreach (var room in configuration.Rooms)
                        Rooms.Add(room);
                }
                Rooms.CollectionChanged += Rooms_CollectionChanged;
                RoomList.Save(Rooms);

            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Erreur d'import", $"Impossible de charger la configuration : {ex.Message}");
            }
            finally
            {
                _syncing = false;
            }
        }
        private async Task TrySendExamOptionsAsync()
        {
            try
            {
                if (App.ChatService.Connection != null &&
                    App.ChatService.Connection.State == HubConnectionState.Connected)
                {
                    await App.ChatService.SendExamOptionsAsync(Options);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur synchro examens : {ex.Message}");
            }
        }

        private async Task TrySendRoomsAsync()
        {
            try
            {
                if (App.ChatService.Connection != null &&
                    App.ChatService.Connection.State == HubConnectionState.Connected)
                {
                    Debug.WriteLine($"Envoi {Rooms.Count} salles");
                    await App.ChatService.SendRoomsAsync(Rooms);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur synchro salles : {ex.Message}");
            }
        }

        private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_syncing) return;
            ExamOption.Save(Options);
        }

        private void Options_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var option in Options)
                    option.PropertyChanged += Option_PropertyChanged;
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (ExamOption item in e.OldItems)
                        item.PropertyChanged -= Option_PropertyChanged;
                }

                if (e.NewItems != null)
                {
                    foreach (ExamOption item in e.NewItems)
                        item.PropertyChanged += Option_PropertyChanged;
                }
            }

            if (_syncing) return;
            int index = 1;
            foreach (var opt in Options)
                opt.Index = index++;

            ExamOption.Save(Options);
        }

        private void Rooms_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_syncing) return;
            RoomList.Save(Rooms);
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
        private void ExamRoomPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Synchronisation manuelle uniquement
        }

        private void ChatService_ExamOptionsUpdated(IEnumerable<ExamOption> obj)
        {
            // plus de synchronisation automatique
        }

        private void ChatService_RoomsUpdated(IEnumerable<string> obj)
        {
            // plus de synchronisation automatique
        }

        private async Task LoadConfigFromServerAsync()
        {
            if (App.ChatService.Connection == null ||
                App.ChatService.Connection.State != HubConnectionState.Connected)
                return;

            _syncing = true;

            var serverOptions = await App.ChatService.GetExamOptionsAsync();
            if (serverOptions.Any())
            {
                BackupFile(ExamOption.FilePath);
                Options.CollectionChanged -= Options_CollectionChanged;
                foreach (var o in Options)
                    o.PropertyChanged -= Option_PropertyChanged;
                Options.Clear();
                foreach (var opt in serverOptions.OrderBy(o => o.Index))
                {
                    Options.Add(opt);
                    opt.PropertyChanged += Option_PropertyChanged;
                }
                Options.CollectionChanged += Options_CollectionChanged;
                ExamOption.Save(Options);
            }

            var serverRooms = await App.ChatService.GetRoomsAsync();
            if (serverRooms.Any())
            {
                BackupFile(RoomList.FilePath);
                Rooms.CollectionChanged -= Rooms_CollectionChanged;
                Rooms.Clear();
                foreach (var r in serverRooms)
                    Rooms.Add(r);
                Rooms.CollectionChanged += Rooms_CollectionChanged;
                RoomList.Save(Rooms);
            }

            _syncing = false;
        }

        private async Task SendConfigToServerAsync()
        {
            try
            {
                if (App.ChatService.Connection != null &&
                    App.ChatService.Connection.State == HubConnectionState.Connected)
                {
                    await App.ChatService.SendExamOptionsSilentAsync(Options);
                    await App.ChatService.SendRoomsSilentAsync(Rooms);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi configuration : {ex.Message}");
            }
        }

        private static void BackupFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", true);
            }
            catch
            {
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            ThemeHelper.ApplyDialogTheme(dialog);

            await dialog.ShowAsync();
        }

        private static bool AreExamOptionsEqual(IEnumerable<ExamOption> a, IEnumerable<ExamOption> b)
        {
            var ja = JsonConvert.SerializeObject(a);
            var jb = JsonConvert.SerializeObject(b);
            return ja == jb;
        }

        private async void RenameRoom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string room)
            {
                var dialog = new ContentDialog
                {
                    Title = "Renommer la salle",
                    PrimaryButtonText = "Valider",
                    CloseButtonText = "Annuler",
                    XamlRoot = this.XamlRoot
                };

                ThemeHelper.ApplyDialogTheme(dialog);

                var box = new TextBox { Text = room };
                dialog.Content = box;
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var index = Rooms.IndexOf(room);
                    if (index >= 0 && !string.IsNullOrWhiteSpace(box.Text))
                        Rooms[index] = box.Text.Trim();
                }
            }
        }
    }

    internal class ExamRoomConfiguration
    {
        public List<ExamOption>? Exams { get; set; }
        public List<string>? Rooms { get; set; }
    }
}

