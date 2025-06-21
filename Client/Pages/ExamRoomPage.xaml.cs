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

namespace Client.Pages
{
    public sealed partial class ExamRoomPage : Page
    {
        public ObservableCollection<ExamOption> Options { get; } = ExamOption.Load();
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();

        private bool _syncing;
        private bool _hasChanges;

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

        private async void ExamRoomPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.ChatService.ExamOptionsUpdated -= ChatService_ExamOptionsUpdated;
            App.ChatService.RoomsUpdated -= ChatService_RoomsUpdated;

            if (_hasChanges)
            {
                await TrySendExamOptionsAsync();
                await TrySendRoomsAsync();
                _hasChanges = false;
            }
        }

        private void DeleteExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExamOption opt)
            {
                Options.Remove(opt);
            }
        }
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            Options.Add(new ExamOption
            {
                Index = Options.Count + 1,
                Color = "yellow"
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
            _hasChanges = true;
            ExamOption.Save(Options);
        }

        private void Options_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncing) return;
            int index = 1;
            foreach (var opt in Options)
                opt.Index = index++;

            if (e.NewItems != null)
            {
                foreach (ExamOption opt in e.NewItems)
                    opt.PropertyChanged += Option_PropertyChanged;
            }

            if (e.OldItems != null)
            {
                foreach (ExamOption opt in e.OldItems)
                    opt.PropertyChanged -= Option_PropertyChanged;
            }

            _hasChanges = true;
            ExamOption.Save(Options);
        }

        private void Rooms_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_syncing) return;
            _hasChanges = true;
            RoomList.Save(Rooms);
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
        private async void ExamRoomPage_Loaded(object sender, RoutedEventArgs e)
        {
            //App.ChatService.ExamOptionsUpdated += ChatService_ExamOptionsUpdated;
            App.ChatService.RoomsUpdated += ChatService_RoomsUpdated;
            await SyncWithServerAsync();
        }

        private void ChatService_ExamOptionsUpdated(IEnumerable<ExamOption> obj)
        {
            _ = DispatcherQueue.TryEnqueue(async () => await SyncWithServerAsync());
        }

        private void ChatService_RoomsUpdated(IEnumerable<string> obj)
        {
            _ = DispatcherQueue.TryEnqueue(async () => await SyncWithServerAsync());
        }

        private async Task SyncWithServerAsync()
        {
            if (App.ChatService.Connection == null ||
                App.ChatService.Connection.State != HubConnectionState.Connected)
                return;

            _syncing = true;

            var serverOptions = await App.ChatService.GetExamOptionsAsync();
            if (serverOptions.Any())
            {
                if (!AreExamOptionsEqual(serverOptions, Options))
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
            }
            else if (Options.Any())
            {
                await App.ChatService.SendExamOptionsAsync(Options);
            }

            var serverRooms = await App.ChatService.GetRoomsAsync();
            if (serverRooms.Any())
            {
                if (!serverRooms.SequenceEqual(Rooms))
                {
                    BackupFile(RoomList.FilePath);
                    Rooms.CollectionChanged -= Rooms_CollectionChanged;
                    Rooms.Clear();
                    foreach (var r in serverRooms)
                        Rooms.Add(r);
                    Rooms.CollectionChanged += Rooms_CollectionChanged;
                    RoomList.Save(Rooms);
                }
            }
            else if (Rooms.Any())
            {
                await App.ChatService.SendRoomsAsync(Rooms);
            }

            _syncing = false;
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
}

