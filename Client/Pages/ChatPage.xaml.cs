using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Client.Models;
using Client.Services;
using Client.Helpers;

namespace Client.Pages
{
    public sealed partial class ChatPage : Page
    {
        private readonly SignalRService _service;
        public ObservableCollection<UserInfo> ConnectedUsers => _service.ConnectedUsers;
        public ObservableCollection<object> Messages => _service.Messages;
        public ObservableCollection<Patient> Patients => _service.Patients;
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();
        private ObservableCollection<string> RoomsWithAll { get; } = new();

        private DateTime _currentDate = DateTime.Today;

        public ChatPage()
        {
            InitializeComponent();
            _service = App.ChatService;
            DataContext = this;

            BuildRooms();
            Rooms.CollectionChanged += Rooms_CollectionChanged;

            UsersList.ItemsSource = ConnectedUsers;
            MessagesList.ItemsSource = Messages;
            _service.Dispatcher = DispatcherQueue;

            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged += ApplyChatStyle;
            _service.ConnectedUsers.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(TryRestoreUserSelection);

            _service.OnMessageReceived += OnMessageReceived;
            Loaded += ChatPage_Loaded;
            Unloaded += ChatPage_Unloaded;
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _service.OnMessageReceived -= OnMessageReceived;
            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged -= ApplyChatStyle;
        }

        private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            ApplyChatStyle(style == "OldSchool" ? ChatStyle.OldSchool : ChatStyle.Modern);

            if (_service.Connection == null || _service.Connection.State != HubConnectionState.Connected)
            {
                await _service.InitializeAsync();
            }

            TryRestoreUserSelection();
            await WaitForConnectionReady();

            Messages.Clear();
            Messages.Add(new LoadMorePlaceholder());

            var cachedMessages = await _service.LoadTodayMessagesFromDiskAsync();
            if (cachedMessages.Any())
            {
                foreach (var msg in cachedMessages)
                    Messages.Add(msg);
                ScrollToLastMessage();
            }

            var result = await _service.LoadTodayMessagesAsync("Moi");
            if (result.Success)
            {
                Messages.RemoveWhere<ChatMessageModel>();
                foreach (var msg in result.Value)
                    Messages.Add(msg);

                await _service.SaveTodayMessagesToDiskAsync();
                ScrollToLastMessage();
            }

            InputBox.Focus(FocusState.Programmatic);
        }

        public void FocusInputBox()
        {
            InputBox.Focus(FocusState.Programmatic);
        }

        private void OnMessageReceived(ChatMessageModel chat)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                Messages.Add(chat);
                ScrollToLastMessage();
                await _service.SaveTodayMessagesToDiskAsync();
            });
        }

        private void TryRestoreUserSelection()
        {
            if (ConnectedUsers.Count == 0) return;

            if (AppSettings.CurrentSelectedUser != null)
            {
                var found = ConnectedUsers.FirstOrDefault(u => u.Username == AppSettings.CurrentSelectedUser.Username);
                if (found != null)
                {
                    UsersList.SelectedItem = found;
                    return;
                }
            }

            var defaultUser = ConnectedUsers.FirstOrDefault(u => u.Username == "A Tous");
            if (defaultUser != null)
            {
                UsersList.SelectedItem = defaultUser;
                AppSettings.CurrentSelectedUser = defaultUser;
            }
        }

        private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UsersList.SelectedItem is UserInfo selected)
            {
                AppSettings.CurrentSelectedUser = selected;
                InputBox.Focus(FocusState.Programmatic);
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text;
            var user = UsersList.SelectedItem as UserInfo;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (user != null)
                {
                    await _service.SendMessage("Moi", "RDC", user.Username, text, @"E:\benoit.png", DateTime.Now);
                    Debug.WriteLine($"📤 Message envoyé à {user.Username}: {text}");
                    InputBox.Text = string.Empty;
                }
            }
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Send_Click(sender, e);
            }
        }

        private async Task LoadHistoryAsync(string withUser)
        {
            var username = "Moi";

            try
            {
                var history = await _service.Connection.InvokeAsync<List<ChatMessageModel>>("GetHistory", username, withUser);

                Messages.Clear();
                Messages.Add(new LoadMorePlaceholder());

                foreach (var msg in history)
                {
                    Messages.Add(msg);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur chargement historique : {ex.Message}");
            }
        }

        private async void JoinGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Rejoindre un groupe",
                PrimaryButtonText = "OK",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };

            var groupNameBox = new TextBox { PlaceholderText = "Nom du groupe" };
            var passwordBox = new PasswordBox { PlaceholderText = "Mot de passe" };

            stack.Children.Add(groupNameBox);
            stack.Children.Add(passwordBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var groupName = groupNameBox.Text;
                var password = passwordBox.Password;

                var response = await App.ChatService.Connection.InvokeAsync<string>("JoinProtectedGroup", groupName, password);
                Debug.WriteLine($"🔐 Groupe {groupName} : {response}");
            }
        }

        private void RefreshUsers_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("🔄 Rafraîchissement manuel de la liste d'utilisateurs");

            var sorted = ConnectedUsers
                .OrderBy(u => u.Username != "A Tous")
                .ThenBy(u => u.Username != "Secrétariat")
                .ThenBy(u => u.DisplayName)
                .ToList();

            ConnectedUsers.Clear();
            foreach (var u in sorted)
                ConnectedUsers.Add(u);
        }

        private async void LoadMore_Click(object sender, RoutedEventArgs e)
        {
            const int maxDaysToTry = 30;
            int tryCount = 0;

            while (tryCount < maxDaysToTry)
            {
                _currentDate = _currentDate.AddDays(-1);
                tryCount++;

                var result = await _service.LoadMessagesForDateAsync("Moi", _currentDate);

                if (result.Success && result.Value.Any())
                {
                    int insertIndex = 1;
                    foreach (var msg in result.Value.OrderBy(m => m.Timestamp))
                    {
                        Messages.Insert(insertIndex++, msg);
                    }

                    return;
                }
            }

            if (tryCount >= maxDaysToTry)
            {
                var loadMore = Messages.FirstOrDefault(x => x is LoadMorePlaceholder);
                if (loadMore != null)
                    Messages.Remove(loadMore);
                Messages.Insert(0, new EmptyPlaceholder());
            }

            Debug.WriteLine("📭 Aucun message trouvé dans les 30 derniers jours.");
        }

        private void ApplyChatStyle(ChatStyle style)
        {
            Debug.WriteLine($"[ChatPage] ApplyChatStyle: {style}");

            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
            {
                selector.DisplayMode = style;
            }

            var styleKey = style == ChatStyle.OldSchool ? "CompactItemStyle" : "BubbleItemStyle";
            if (Resources[styleKey] is Style itemStyle)
            {
                MessagesList.ItemContainerStyle = itemStyle;
            }

            MessagesList.ItemsSource = Messages;
            MessagesList.UpdateLayout();
        }

        private async void ScrollToLastMessage()
        {
            await Task.Delay(100);

            if (MessagesList.Items.Count > 0)
            {
                var last = MessagesList.Items[MessagesList.Items.Count - 1];
                MessagesList.ScrollIntoView(last);
            }
        }

        private async Task WaitForConnectionReady()
        {
            int retries = 0;
            while ((_service.Connection == null || _service.Connection.State != HubConnectionState.Connected) && retries < 30)
            {
                await Task.Delay(100);
                retries++;
            }
        }

        private void Rooms_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            BuildRooms();
        }
        
        private void BuildRooms()
        {
            RoomsWithAll.Clear();
            foreach (var room in Rooms)
                RoomsWithAll.Add(room);
            RoomsWithAll.Add("Toutes");

            RoomsPivot.Items.Clear();
            foreach (var room in RoomsWithAll)
            {
                var item = new PivotItem { Header = room, Content = new TextBlock { Text = room, Margin = new Thickness(10) } };
                RoomsPivot.Items.Add(item);
            }
        }
    }

    public static class ObservableCollectionExtensions
    {
        public static void RemoveWhere<T>(this ObservableCollection<object> collection)
        {
            var toRemove = collection.OfType<T>().ToList();
            foreach (var item in toRemove)
                collection.Remove(item);
        }
        
    }
}
