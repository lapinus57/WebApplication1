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
        private readonly ObservableCollection<object> _messages;
        public ObservableCollection<UserInfo> ConnectedUsers => _service.ConnectedUsers;
        public ObservableCollection<object> Messages => _messages;
        public ObservableCollection<Patient> Patients => _service.Patients;
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();
        private ObservableCollection<string> RoomsWithAll { get; } = new();

        private DateTime _currentDate = DateTime.Today;

        public ChatPage()
        {
            InitializeComponent();
            _service = App.ChatService ?? throw new InvalidOperationException("Chat service is not initialized.");
            _messages = _service.Messages;
            DataContext = this;

            BuildRooms();
            Rooms.CollectionChanged += Rooms_CollectionChanged;

            UsersList.ItemsSource = ConnectedUsers;
            MessagesList.ItemsSource = Messages;
            _service.Dispatcher = DispatcherQueue;

            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
            {
                selector.MyUsername = _service.Username;
            }

            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged += ApplyChatStyle;
            ViewModel.ViewModel.SettingsViewModel.SenderColorModeChanged += ApplyColorMode;
            _service.ConnectedUsers.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(TryRestoreUserSelection);

            _service.OnMessageReceived += OnMessageReceived;
            Loaded += ChatPage_Loaded;
            Unloaded += ChatPage_Unloaded;
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _service.OnMessageReceived -= OnMessageReceived;
            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged -= ApplyChatStyle;
            ViewModel.ViewModel.SettingsViewModel.SenderColorModeChanged -= ApplyColorMode;
        }

        private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            ApplyChatStyle(style == "OldSchool" ? ChatStyle.OldSchool : ChatStyle.Modern);
            var colorMode = AppSettings.Get("UseSenderColors", "False") == "True";
            ApplyColorMode(colorMode);

            if (_service.Connection == null || _service.Connection.State != HubConnectionState.Connected)
            {
                await _service.InitializeAsync();
                if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
                {
                    selector.MyUsername = _service.Username;
                }
            }

            TryRestoreUserSelection();
            await WaitForConnectionReady();

            if (!_service.IsHistoryLoaded && !_messages.Any(m => m is ChatMessageModel))
            {
                _messages.Clear();
                _messages.Add(new LoadMorePlaceholder());

                var cachedMessages = await _service.LoadTodayMessagesFromDiskAsync();
                foreach (var msg in cachedMessages)
                    _messages.Add(msg);

                var result = await _service.LoadTodayMessagesAsync(_service.Username);
                if (result.Success && result.Value is { } loadedMessages)
                {
                    foreach (var item in _messages.OfType<ChatMessageModel>().ToList())
                    {
                        if (item is not null)
                        {
                            _messages.Remove(item);
                        }
                    }

                    foreach (var msg in loadedMessages)
                        _messages.Add(msg);

                    await _service.SaveTodayMessagesToDiskAsync();
                }

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
                // Avoid adding a duplicate message if one with the same
                // sender, recipient, content and timestamp already exists
                var existing = _messages
                    .OfType<ChatMessageModel>()
                    .Any(m => m.Sender == chat.Sender &&
                              m.Destinataire == chat.Destinataire &&
                              m.Content == chat.Content &&
                              m.Timestamp == chat.Timestamp);

                if (!existing)
                {
                    _messages.Add(chat);
                    ScrollToLastMessage();
                    await _service.SaveTodayMessagesToDiskAsync();
                }
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
                    await _service.SendMessage(_service.Username, "RDC", user.Username, text, @"E:\benoit.png", DateTime.Now);
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
            var username = _service.Username;
            var connection = _service.Connection;

            if (connection is null)
            {
                return;
            }

            try
            {
                var history = await connection.InvokeAsync<List<ChatMessageModel>>("GetHistory", username, withUser);

                _messages.Clear();
                _messages.Add(new LoadMorePlaceholder());

                foreach (var msg in history)
                {
                    _messages.Add(msg);
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

                var connection = App.ChatService.Connection;
                if (connection is null)
                {
                    Debug.WriteLine("❌ Impossible de rejoindre le groupe : connexion indisponible.");
                    return;
                }

                var response = await connection.InvokeAsync<string>("JoinProtectedGroup", groupName, password);
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

                var result = await _service.LoadMessagesForDateAsync(_service.Username, _currentDate);

                if (result.Success && result.Value is { } moreMessages && moreMessages.Any())
                {
                    int insertIndex = 1;
                    foreach (var msg in moreMessages.OrderBy(m => m.Timestamp))
                    {
                        _messages.Insert(insertIndex++, msg);
                    }

                    return;
                }
            }

            if (tryCount >= maxDaysToTry)
            {
                var loadMore = _messages.FirstOrDefault(x => x is LoadMorePlaceholder);
                if (loadMore != null)
                    _messages.Remove(loadMore);
                _messages.Insert(0, new EmptyPlaceholder());
            }

            Debug.WriteLine("📭 Aucun message trouvé dans les 30 derniers jours.");
        }

        private static bool ShouldKeepMessage(object? item, UserInfo? selected)
        {
            if (item is null)
            {
                return false;
            }

            if (item is not ChatMessageModel message)
            {
                // Preserve placeholders and other non-chat elements.
                return true;
            }

            if (selected is null)
            {
                return true;
            }

            if (string.Equals(selected.Username, "A Tous", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(message.Sender, selected.Username, StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Destinataire, selected.Username, StringComparison.OrdinalIgnoreCase);
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

        private void ApplyColorMode(bool useSenderColors)
        {
            Debug.WriteLine($"[ChatPage] ApplyColorMode: {useSenderColors}");
            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
            {
                selector.UseSenderColors = useSenderColors;
            }
            MessagesList.ItemsSource = Messages;
            MessagesList.UpdateLayout();
        }

        public async void ScrollToLastMessage()
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
