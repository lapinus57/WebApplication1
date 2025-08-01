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
using Client;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Documents;

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
        private FrameworkElement? _currentMessageTarget;
        private bool _ignoreSelectionChanged;
        private bool _ignoreOrderChange;
        public bool ShowTimeModification { get; private set; }
        public Visibility TimeModificationVisibility => ShowTimeModification ? Visibility.Visible : Visibility.Collapsed;
        public ChatPage()
        {
            InitializeComponent();
            _service = App.ChatService;
            var cfg = MachineConfig.Load();
            ShowTimeModification = cfg.ShowTimeModification;
            DataContext = this;

            BuildRooms();
            Rooms.CollectionChanged += Rooms_CollectionChanged;
            Patients.CollectionChanged += Patients_CollectionChanged;

            UsersList.ItemsSource = ConnectedUsers;
            _service.Dispatcher = DispatcherQueue;

            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged += ApplyChatStyle;
            ViewModel.ViewModel.SettingsViewModel.BubbleColorModeChanged += ApplyBubbleColorMode;
            _service.ConnectedUsers.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                ApplySavedUserOrder();
                TryRestoreUserSelection();
            });

            _service.OnMessageReceived += OnMessageReceived;
            _service.OnPatientRemoved += Service_OnPatientRemoved;
            _service.OnPatientUpdated += Service_OnPatientUpdated;
            _service.ReconnectCountdownChanged += Service_ReconnectCountdownChanged;
            Loaded += ChatPage_Loaded;
            Unloaded += ChatPage_Unloaded;
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _service.OnMessageReceived -= OnMessageReceived;
            _service.OnPatientRemoved -= Service_OnPatientRemoved;
            _service.OnPatientUpdated -= Service_OnPatientUpdated;
            _service.ReconnectCountdownChanged -= Service_ReconnectCountdownChanged;
            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged -= ApplyChatStyle;
            ViewModel.ViewModel.SettingsViewModel.BubbleColorModeChanged -= ApplyBubbleColorMode;
            Patients.CollectionChanged -= Patients_CollectionChanged;
        }

        private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector sel)
            {
                sel.MyUsername = App.UserName;
                var useSenderColor = AppSettings.Get("UseSenderColorForBubbles", "False") == "True";
                sel.UseSenderColor = useSenderColor;
                ApplyBubbleColorMode(useSenderColor);
            }

            var style = AppSettings.Get("ChatDisplayStyle", "Modern");
            ApplyChatStyle(style == "OldSchool" ? ChatStyle.OldSchool : ChatStyle.Modern);

            if (_service.Connection == null || _service.Connection.State != HubConnectionState.Connected)
            {
                await _service.InitializeAsync();
            }

            TryRestoreUserSelection();
            await WaitForConnectionReady();
            UpdateReconnectButtonVisibility();
            await _service.RefreshGroupsAsync();
            ApplySavedUserOrder();

            if (!_service.IsHistoryLoaded && !Messages.OfType<ChatMessageModel>().Any())
            {
                Messages.Clear();
                Messages.Add(new LoadMorePlaceholder());

                var cachedMessages = await _service.LoadTodayMessagesFromDiskAsync();
                foreach (var msg in cachedMessages)
                    Messages.Add(msg);

                var result = await _service.LoadTodayMessagesAsync(App.UserName);
                if (result.Success)
                {
                    foreach (var item in Messages.OfType<ChatMessageModel>().ToList())
                        Messages.Remove(item);
                    foreach (var msg in result.Value)
                        Messages.Add(msg);

                    await _service.SaveTodayMessagesToDiskAsync();
                }

                ScrollToLastMessage();
            }

            InputBox.Focus(FocusState.Programmatic);
        }

        private void UpdateReconnectButtonVisibility()
        {
            if (_service.Connection == null || _service.Connection.State != HubConnectionState.Connected)
            {
                ReconnectButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReconnectButton.Visibility = Visibility.Collapsed;
            }
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
                var existing = Messages
                    .OfType<ChatMessageModel>()
                    .Any(m => m.Sender == chat.Sender &&
                              m.Destinataire == chat.Destinataire &&
                              m.Content == chat.Content &&
                              m.Timestamp == chat.Timestamp);

                if (!existing)
                {
                    Messages.Add(chat);
                    ScrollToLastMessage();
                    await _service.SaveTodayMessagesToDiskAsync();
                }
            });
        }

        private void TryRestoreUserSelection()
        {
            if (ConnectedUsers.Count == 0) return;
            var target = App.LastUserChanged ?? AppSettings.CurrentSelectedUser;
            if (target != null && target.Username == App.UserName)
                target = null;

            if (target != null)
            {
                var found = ConnectedUsers.FirstOrDefault(u => u.Username == target.Username);
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
                App.LastUserChanged = defaultUser;
            }
        }

        private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreSelectionChanged)
                return;

            if (UsersList.SelectedItem is UserInfo selected)
            {
                if (selected.Username == App.UserName)
                {
                    _ignoreSelectionChanged = true;
                    UsersList.SelectedItem = App.LastUserChanged ?? AppSettings.CurrentSelectedUser;
                    _ignoreSelectionChanged = false;
                    return;
                }

                App.LastUserChanged = selected;
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
                    await _service.SendMessage(App.UserName, _service.RoomName, user.Username, text, @"E:\benoit.png", DateTime.Now);
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
                var text = InputBox.Text.Trim();
                if (string.Equals(text, "/getallroom", StringComparison.OrdinalIgnoreCase))
                {
                    _ = ShowAllGroupsDialog();
                    InputBox.Text = string.Empty;
                }
                else if (string.Equals(text, "/setallroom", StringComparison.OrdinalIgnoreCase))
                {
                    _ = ShowSetRoomDialog();
                    InputBox.Text = string.Empty;
                }
                else
                {
                    Send_Click(sender, e);
                }
            }
        }

        private async Task LoadHistoryAsync(string withUser)
        {
            var username = App.UserName;

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
                await _service.RefreshGroupsAsync();
                ApplySavedUserOrder();
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
            SaveUserOrder();
        }

        private async void LoadMore_Click(object sender, RoutedEventArgs e)
        {
            const int maxDaysToTry = 30;
            int tryCount = 0;

            while (tryCount < maxDaysToTry)
            {
                _currentDate = _currentDate.AddDays(-1);
                tryCount++;

                var result = await _service.LoadMessagesForDateAsync(App.UserName, _currentDate);

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
                MessagesList.ItemTemplateSelector = null;
                MessagesList.ItemTemplateSelector = selector;
            }

            var styleKey = style == ChatStyle.OldSchool ? "CompactItemStyle" : "BubbleItemStyle";
            if (Resources[styleKey] is Style itemStyle)
            {
                MessagesList.ItemContainerStyle = itemStyle;
            }

            MessagesList.UpdateLayout();
        }

        private void ApplyBubbleColorMode(bool useSenderColor)
        {
            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
            {
                selector.UseSenderColor = useSenderColor;
                MessagesList.ItemTemplateSelector = null;
                MessagesList.ItemTemplateSelector = selector;
            }

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
                var list = new ListView { SelectionMode = ListViewSelectionMode.None };
                if (Resources["PatientTemplateSelector"] is DataTemplateSelector selector)
                    list.ItemTemplateSelector = selector;
                list.ItemsSource = GetPatientsForRoom(room).ToList();
                var item = new PivotItem { Header = room, Content = list };
                RoomsPivot.Items.Add(item);
            }

            UpdatePatientViews();
        }

        private void Patients_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdatePatientViews);
        }


        private void UpdatePatientViews()
        {
            foreach (var pi in RoomsPivot.Items.OfType<PivotItem>())
            {
                if (pi.Content is ListView lv && pi.Header is string room)
                {
                    lv.ItemsSource = GetPatientsForRoom(room).ToList();
                }
            }
        }

        private async void DeletePatient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirmation",
                    Content = "Supprimer ce patient ?",
                    PrimaryButtonText = "Oui",
                    CloseButtonText = "Non",
                    XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    Patients.Remove(patient);
                    await _service.RemovePatientAsync(patient.Id);
                }
            }
        }

        private async void TogglePatientTaken_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var newValue = !patient.IsTaken;
                patient.IsTaken = newValue;
                patient.PickUpTime = newValue ? DateTime.Now : null;
                UpdatePatientViews();
                await _service.SetPatientTakenAsync(patient.Id, newValue);
            }
        }

        private async void MovePatientFirst_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var lastTaken = Patients.Where(p => p.IsTaken && !p.IsArchived)
                    .OrderBy(p => p.HoldTime)
                    .LastOrDefault();
                var firstWaiting = Patients.Where(p => !p.IsTaken && !p.IsArchived)
                    .OrderBy(p => p.HoldTime)
                    .FirstOrDefault();

                if (firstWaiting != null)
                {
                    DateTime newTime;

                    if (lastTaken != null)
                    {
                        var avgTicks = (lastTaken.HoldTime.Ticks + firstWaiting.HoldTime.Ticks) / 2;
                        newTime = new DateTime(avgTicks);
                    }
                    else
                    {
                        newTime = firstWaiting.HoldTime.AddSeconds(-1);
                    }

                    patient.HoldTime = newTime;
                    UpdatePatientViews();
                    await _service.UpdatePatientHoldTimeAsync(patient.Id, newTime);
                }
            }
        }

        private async void MovePatientUp_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var list = Patients.Where(p => !p.IsTaken && !p.IsArchived).OrderBy(p => p.HoldTime).ToList();
                var index = list.IndexOf(patient);
                if (index > 0)
                {
                    var h1 = list[Math.Max(index - 1, 0)].HoldTime;
                    var h2 = list[Math.Max(index - 2, 0)].HoldTime;
                    var avgTicks = (h1.Ticks + h2.Ticks) / 2;
                    var newTime = new DateTime(avgTicks);
                    patient.HoldTime = newTime;
                    UpdatePatientViews();
                    await _service.UpdatePatientHoldTimeAsync(patient.Id, newTime);
                }
            }
        }

        private async void MovePatientDown_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var list = Patients.Where(p => !p.IsTaken && !p.IsArchived).OrderBy(p => p.HoldTime).ToList();
                var index = list.IndexOf(patient);
                if (index >= 0 && index < list.Count - 1)
                {
                    var h1 = list[Math.Min(index + 1, list.Count - 1)].HoldTime;
                    var h2 = list[Math.Min(index + 2, list.Count - 1)].HoldTime;
                    var avgTicks = (h1.Ticks + h2.Ticks) / 2;
                    var newTime = new DateTime(avgTicks);
                    patient.HoldTime = newTime;
                    UpdatePatientViews();
                    await _service.UpdatePatientHoldTimeAsync(patient.Id, newTime);
                }
            }
        }

        private async void ArchivePatients_Click(object sender, RoutedEventArgs e)
        {
            await _service.ArchiveTakenPatientsAsync();
        }

        private async void UnarchivePatients_Click(object sender, RoutedEventArgs e)
        {
            await _service.UnarchiveAllPatientsAsync();
        }

        private async void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            await _service.ReconnectAsync();
        }


        private void Service_OnPatientRemoved(string id)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var p = Patients.FirstOrDefault(x => x.Id == id);
                if (p != null) Patients.Remove(p);
            });
        }

        private void Service_OnPatientUpdated(Patient patient)
        {
            DispatcherQueue.TryEnqueue(UpdatePatientViews);
        }

        private void Service_ReconnectCountdownChanged(int seconds)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_service.Connection == null || _service.Connection.State != HubConnectionState.Connected)
                {
                    ReconnectButton.Visibility = Visibility.Visible;
                    ReconnectButton.Content = $"Reconnecter ({seconds})";
                }
                else
                {
                    ReconnectButton.Visibility = Visibility.Collapsed;
                }
            });
        }

        private IEnumerable<Patient> GetPatientsForRoom(string room)
        {
            IEnumerable<Patient> query = room == "Toutes"
              ? Patients.Where(p => !p.IsArchived)
              : Patients.Where(p => p.Position == room && !p.IsArchived);

            return query
                .OrderByDescending(p => p.IsTaken)
                .ThenBy(p => p.HoldTime);
        }

        private void MessageContextMenu_Opened(object sender, object e)
        {
            if (sender is MenuFlyout menu)
                _currentMessageTarget = menu.Target as FrameworkElement;
        }

        private void CopySelection_Click(object sender, RoutedEventArgs e)
        {
            string? text = null;
            if (_currentMessageTarget is TextBlock tb)
            {
                text = string.IsNullOrEmpty(tb.SelectedText) ? tb.Text : tb.SelectedText;
            }
            else if (_currentMessageTarget is RichTextBlock rtb)
            {
                rtb.SelectAll();
                text = GetRichText(rtb);
            }
            else if (_currentMessageTarget?.DataContext is ChatMessageModel msg)
            {
                text = $"{msg.Header}\n{msg.Content}\n{msg.TimeFormatted}";
            }

            if (!string.IsNullOrEmpty(text))
            {
                text = text.Replace("\r", string.Empty).Replace("\n", " ");
                var data = new DataPackage();
                data.SetText(text);
                Clipboard.SetContent(data);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMessageTarget is TextBlock tb)
            {
                tb.SelectAll();
            }
            else if (_currentMessageTarget is RichTextBlock rtb)
            {
                rtb.SelectAll();
            }
        }
        private static string GetRichText(RichTextBlock block)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in block.Blocks)
            {
                if (b is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        switch (inline)
                        {
                            case Run run:
                                sb.Append(run.Text);
                                break;
                            case LineBreak:
                                sb.Append('\n');
                                break;
                        }
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
        private async void InputBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                text = text.Replace("\r", string.Empty).Replace("\n", " ");
                if (sender is TextBox tb)
                {
                    var start = tb.SelectionStart;
                    var length = tb.SelectionLength;
                    var current = tb.Text;
                    tb.Text = current.Substring(0, start) + text + current.Substring(start + length);
                    tb.SelectionStart = start + text.Length;
                    e.Handled = true;
                }
            }
        }

        private async Task ShowAllGroupsDialog()
        {
            var groups = await _service.GetAllGroupsAsync();
            var sb = new StringBuilder();
            foreach (var kvp in groups)
            {
                sb.AppendLine($"{kvp.Key}: {string.Join(", ", kvp.Value)}");
            }

            var dialog = new ContentDialog
            {
                Title = "Groupes",
                Content = new ScrollViewer { Content = new TextBlock { Text = sb.ToString() } },
                CloseButtonText = "Fermer",
                XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task ShowSetRoomDialog()
        {
            var groups = await _service.GetAllGroupsAsync();

            var dialog = new ContentDialog
            {
                Title = "Edition de salle",
                CloseButtonText = "Fermer",
                XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            var roomBox = new ComboBox { ItemsSource = groups.Keys.ToList(), PlaceholderText = "Salle" };
            var nameBox = new TextBox { PlaceholderText = "Nouveau nom" };
            var pwdBox = new PasswordBox { PlaceholderText = "Nouveau mot de passe" };
            var membersList = new ListView { Height = 100 };

            roomBox.SelectionChanged += (s, e) =>
            {
                if (roomBox.SelectedItem is string sel && groups.TryGetValue(sel, out var list))
                    membersList.ItemsSource = list;
            };

            var renameBtn = new Button { Content = "Renommer" };
            renameBtn.Click += async (s, e) =>
            {
                if (roomBox.SelectedItem is string sel && !string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    await _service.RenameGroupAsync(sel, nameBox.Text);
                }
            };

            var passBtn = new Button { Content = "Changer mot de passe" };
            passBtn.Click += async (s, e) =>
            {
                if (roomBox.SelectedItem is string sel)
                {
                    await _service.ChangeGroupPasswordAsync(sel, pwdBox.Password);
                }
            };

            var removeBtn = new Button { Content = "Supprimer utilisateur" };
            removeBtn.Click += async (s, e) =>
            {
                if (roomBox.SelectedItem is string sel && membersList.SelectedItem is string user)
                {
                    await _service.RemoveUserFromGroupAsync(sel, user);

                    if (groups.TryGetValue(sel, out var list))
                    {
                        list.Remove(user);
                        membersList.ItemsSource = null;
                        membersList.ItemsSource = list;
                    }
                }
            };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            btns.Children.Add(renameBtn);
            btns.Children.Add(passBtn);
            btns.Children.Add(removeBtn);

            stack.Children.Add(roomBox);
            stack.Children.Add(nameBox);
            stack.Children.Add(pwdBox);
            stack.Children.Add(membersList);
            stack.Children.Add(btns);

            dialog.Content = stack;
            await dialog.ShowAsync();
        }

        private void Message_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (sender is RichTextBlock block && args.NewValue is ChatMessageModel msg)
            {
                msg.FormatContent(block, new RoutedEventArgs());
            }
        }

        private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMessageTarget?.DataContext is ChatMessageModel msg)
            {
                Messages.Remove(msg);
                await _service.DeleteMessageAsync(msg.Id);
                await _service.SaveTodayMessagesToDiskAsync();

                // Force the list view to refresh or the UI may still display
                // the removed item until the page is reloaded
                
                if (Resources["MessageTemplateSelector"] is DataTemplateSelector selector)
                {
                    MessagesList.ItemTemplateSelector = null;
                    MessagesList.ItemTemplateSelector = selector;
                }
            }
        }

        public void RefreshUsername()
        {
            if (Resources["MessageTemplateSelector"] is ChatMessageTemplateSelector selector)
            {
                selector.MyUsername = App.UserName;
                MessagesList.ItemTemplateSelector = null;
                MessagesList.ItemTemplateSelector = selector;
                MessagesList.UpdateLayout();
            }
        }

        private void MoveUserUp_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UserInfo user)
            {
                var index = ConnectedUsers.IndexOf(user);
                if (index > 0)
                {
                    _ignoreOrderChange = true;
                    ConnectedUsers.Move(index, index - 1);
                    _ignoreOrderChange = false;
                    SaveUserOrder();
                }
            }
        }

        private void MoveUserDown_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UserInfo user)
            {
                var index = ConnectedUsers.IndexOf(user);
                if (index >= 0 && index < ConnectedUsers.Count - 1)
                {
                    _ignoreOrderChange = true;
                    ConnectedUsers.Move(index, index + 1);
                    _ignoreOrderChange = false;
                    SaveUserOrder();
                }
            }
        }

        private void ApplySavedUserOrder()
        {
            if (_ignoreOrderChange)
                return;

            var order = AppSettings.UserOrder;
            if (order.Count == 0)
                return;

            var sorted = ConnectedUsers.OrderBy(u =>
            {
                int idx = order.IndexOf(u.Username);
                return idx >= 0 ? idx : int.MaxValue;
            }).ToList();

            if (sorted.SequenceEqual(ConnectedUsers))
                return;

            _ignoreOrderChange = true;
            ConnectedUsers.Clear();
            foreach (var u in sorted)
                ConnectedUsers.Add(u);
            _ignoreOrderChange = false;
        }

        private async void SaveUserOrder()
        {
            AppSettings.UserOrder = ConnectedUsers.Select(u => u.Username).ToList();

            if (_service.Connection != null &&
                _service.Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await _service.SaveUserSettingsAsync(App.UserName,
                        AppSettings.Export());
                }
                catch { }
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
    }}