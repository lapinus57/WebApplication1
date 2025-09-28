using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections;
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
        public ObservableCollection<object> FilteredMessages { get; } = new();
        public ObservableCollection<Patient> Patients => _service.Patients;
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();
        private ObservableCollection<string> RoomsWithAll { get; } = new();

        private DateTime _currentDate = DateTime.Today;
        private FrameworkElement? _currentMessageTarget;
        private bool _ignoreSelectionChanged;
        private bool _ignoreOrderChange;
        private bool _isApplyingSlashCommand;
        private readonly List<SlashCommandInfo> _slashCommands = new()
        {
            new SlashCommandInfo("/getallroom", "Afficher toutes les salles et leurs membres"),
            new SlashCommandInfo("/setallroom", "Modifier l'affectation des salles"),
            new SlashCommandInfo("/addpatienttest", "Ajouter une série de patients de test"),
            new SlashCommandInfo("/clearallpatientday", "Supprimer tous les patients du jour"),
            new SlashCommandInfo("/clearallmessageday", "Supprimer tous les messages du jour"),
            new SlashCommandInfo("/generatemessage", "Générer des messages de démonstration"),
        };
        private static readonly string[] TestFirstNames = new[]
        {
            "Léa", "Lucas","Lucie", "Benoit", "Olivier", "Emma", "Gabriel", "Chloé",
            "Louis", "Manon", "Arthur", "Camille", "Noah","Eric", "Jade", "Hugo","Alice","Julien","Sophie","Maxime",
        };

        private static readonly string[] TestLastNames = new[]
        {
            "Martin", "Bernard", "Dubois", "Thomas", "Robert","Richard", "Weber", "Muller", "Fournier", "Girard","Thomas",
            "Petit", "Durand", "Leroy", "Moreau", "Simon","Laurent", "Lefevre", "Michel", "Garcia", "David", "Bertrand","Roux",
        };

        private static readonly string[] TestTitles = new[]
        {
            "Enfant", "Mr", "Mme", "Mlle"
        };

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
            Messages.CollectionChanged += Messages_CollectionChanged;

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
            _service.OnCallReceived += Service_OnCallReceived;
            Loaded += ChatPage_Loaded;
            Unloaded += ChatPage_Unloaded;
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _service.OnMessageReceived -= OnMessageReceived;
            _service.OnPatientRemoved -= Service_OnPatientRemoved;
            _service.OnPatientUpdated -= Service_OnPatientUpdated;
            _service.ReconnectCountdownChanged -= Service_ReconnectCountdownChanged;
            _service.OnCallReceived -= Service_OnCallReceived;
            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged -= ApplyChatStyle;
            ViewModel.ViewModel.SettingsViewModel.BubbleColorModeChanged -= ApplyBubbleColorMode;
            Patients.CollectionChanged -= Patients_CollectionChanged;
            Messages.CollectionChanged -= Messages_CollectionChanged;
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
            ApplyMessageFilter();
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
                var alreadyExists = Messages
                    .OfType<ChatMessageModel>()
                    .Any(m => m.Sender == chat.Sender &&
                              m.Destinataire == chat.Destinataire &&
                              m.Content == chat.Content &&
                              m.Timestamp == chat.Timestamp);

                if (!alreadyExists)
                {
                    Messages.Add(chat);
                    AutoSelectConversationForIncomingMessage(chat);
                    ScrollToLastMessage();
                    await _service.SaveTodayMessagesToDiskAsync();
                }
                AutoSelectConversationForIncomingMessage(chat);
            });
        }

        private void AutoSelectConversationForIncomingMessage(ChatMessageModel message)
        {
            if (UsersList == null)
                return;

            var me = App.UserName;
            if (message.Sender == me)
                return;

            UserInfo? target = null;

            if (message.Destinataire == "A Tous")
            {
                target = ConnectedUsers.FirstOrDefault(u => u.Username == "A Tous");
            }
            else if (message.Destinataire == me)
            {
                target = ConnectedUsers.FirstOrDefault(u => u.Username == message.Sender);
            }

            if (target != null && !Equals(UsersList.SelectedItem, target))
            {
                UsersList.SelectedItem = target;
            }
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var selected = GetSelectedUser();

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddFilteredItems(e.NewItems, e.NewStartingIndex, selected);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemoveFilteredItems(e.OldItems);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    RemoveFilteredItems(e.OldItems);
                    AddFilteredItems(e.NewItems, e.NewStartingIndex, selected);
                    break;
                case NotifyCollectionChangedAction.Move:
                    ApplyMessageFilter(selected);
                    break;
                default:
                    ApplyMessageFilter(selected);
                    break;
            }

            ScrollToLastMessage();
        }

        private UserInfo? GetSelectedUser() => UsersList?.SelectedItem as UserInfo;

        private void ApplyMessageFilter()
        {
            ApplyMessageFilter(GetSelectedUser());
        }

        private void ApplyMessageFilter(UserInfo? selected)
        {
            FilteredMessages.Clear();
            foreach (var item in Messages)
            {
                if (ShouldKeepMessage(item, selected))
                    FilteredMessages.Add(item);
            }
        }

        private void AddFilteredItems(IList? newItems, int startingIndex, UserInfo? selected)
        {
            if (newItems == null)
                return;

            for (int i = 0; i < newItems.Count; i++)
            {
                var item = newItems[i];
                if (!ShouldKeepMessage(item, selected))
                    continue;

                var messageIndex = startingIndex >= 0 ? startingIndex + i : Messages.IndexOf(item);
                if (messageIndex < 0)
                {
                    FilteredMessages.Add(item);
                    continue;
                }

                var insertIndex = CalculateFilteredInsertIndex(messageIndex, selected);
                insertIndex = Math.Min(insertIndex, FilteredMessages.Count);
                FilteredMessages.Insert(insertIndex, item);
            }
        }

        private void RemoveFilteredItems(IList? oldItems)
        {
            if (oldItems == null)
                return;

            foreach (var item in oldItems)
            {
                if (item != null)
                    FilteredMessages.Remove(item);
            }
        }

        private int CalculateFilteredInsertIndex(int messageIndex, UserInfo? selected)
        {
            if (messageIndex <= 0)
                return 0;

            int count = 0;
            int limit = Math.Min(messageIndex, Messages.Count);
            for (int i = 0; i < limit; i++)
            {
                if (ShouldKeepMessage(Messages[i], selected))
                    count++;
            }

            return count;
        }

        private bool ShouldKeepMessage(object item, UserInfo? selected)
        {
            if (item is not ChatMessageModel msg)
                return true;

            if (selected == null)
                return false;

            string me = App.UserName;

            if (selected.Username == "A Tous")
                return msg.Destinataire == "A Tous";

            if (selected.Username == "Secrétariat")
                return (msg.Sender == me && msg.Destinataire == "Secrétariat") ||
                       (msg.Sender == "Secrétariat" && msg.Destinataire == me);

            return (msg.Sender == me && msg.Destinataire == selected.Username) ||
                   (msg.Sender == selected.Username && msg.Destinataire == me);
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
                ApplyMessageFilter();
                ScrollToLastMessage();
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
                    var avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/earth.png");
                    await _service.SendMessage(App.UserName, _service.RoomName, user.Username, text, avatar, DateTime.Now);
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
                HideSlashCommandsFlyout();
                var text = InputBox.Text.Trim();

                var options = ExamOption.Load();
                var exam = options.FirstOrDefault(o => !string.IsNullOrEmpty(o.CodeMSG) && text.StartsWith(o.CodeMSG, StringComparison.OrdinalIgnoreCase));
                if (exam != null)
                {
                    var name = text.Substring(exam.CodeMSG.Length).Trim();
                    if (string.IsNullOrEmpty(name))
                        _ = HotKeyService.ShowPatientDialogAsync(exam.Name);
                    else
                        _ = HotKeyService.DeclarePatientAsync(exam.Name, name);
                    InputBox.Text = string.Empty;
                    return;
                }

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
                else if (string.Equals(text, "/addpatienttest", StringComparison.OrdinalIgnoreCase))
                {
                    _ = AddTestPatientsAsync();
                    InputBox.Text = string.Empty;
                }
                else if (string.Equals(text, "/clearallpatientday", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _service.ClearTodayPatientsAsync();
                    InputBox.Text = string.Empty;
                }
                else if (string.Equals(text, "/clearallmessageday", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _service.ClearTodayMessagesAsync();
                    InputBox.Text = string.Empty;
                }
                else if (string.Equals(text, "/generatemessage", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _service.GenerateSampleMessagesAsync();
                    InputBox.Text = string.Empty;
                }
                else
                {
                    Send_Click(sender, e);
                }
            }
        }

        private async Task AddTestPatientsAsync()
        {
            try
            {
                var examOptions = await _service.GetExamOptionsAsync();
                if (examOptions == null || examOptions.Count == 0)
                {
                    examOptions = ExamOption.Load().ToList();
                }

                var validExams = examOptions
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                    .OrderBy(o => o.Index)
                    .ToList();

                if (validExams.Count == 0)
                    return;

                foreach (var exam in validExams)
                {
                    var firstName = TestFirstNames[Random.Shared.Next(TestFirstNames.Length)];
                    var lastName = TestLastNames[Random.Shared.Next(TestLastNames.Length)];
                    var title = TestTitles[Random.Shared.Next(TestTitles.Length)];
                    var fullName = $"{title} {lastName} {firstName}".Trim();

                    await HotKeyService.DeclarePatientAsync(exam.Name, fullName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatPage] Error adding test patients: {ex.Message}");
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
        private async void InfoPatient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                await HotKeyService.ShowPatientInfoDialogAsync(patient);
            }
        }


        private async void EditPatient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                await HotKeyService.ShowEditPatientDialogAsync(patient);
                UpdatePatientViews();
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

        private async Task Service_OnCallReceived(string caller, string room)
        {
            bool resetTopMost = false;
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.BringToForeground();
            }
            else if (App.MainWindow is Window genericWindow)
            {
                WindowHelper.SetTopMost(genericWindow, true, true);
                resetTopMost = true;
            }

            var dialog = new ContentDialog
            {
                Title = "Appel",
                Content = $"{caller} vous a appelé dans la pièce {room}",
                PrimaryButtonText = "0",
                SecondaryButtonText = "5",
                CloseButtonText = "AT",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (resetTopMost && App.MainWindow is Window window)
            {
                WindowHelper.SetTopMost(window, false, false);
            }
            string response = result switch
            {
                ContentDialogResult.Primary => "je vient dans 0",
                ContentDialogResult.Secondary => "je vient dans 5",
                _ => "met en attente"
            };

            var avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/earth.png");
            await _service.SendMessage(App.UserName, _service.RoomName, "A Tous", response, avatar, DateTime.Now);
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

        private void UserContextMenu_Opened(object sender, object e)
        {
            if (sender is MenuFlyout menu && menu.Items.Count > 0)
            {
                if (menu.Items[0] is MenuFlyoutItem item && menu.Target?.DataContext is UserInfo user)
                {
                    item.Visibility = (user.Username == "A Tous" || user.Username == "Secrétariat")
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
            }
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
                var content = msg.GetPlainTextContent();
                text = $"{msg.Header}\n{content}\n{msg.TimeFormatted}";
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

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SlashCommandsFlyout == null)
                return;

            if (_isApplyingSlashCommand)
            {
                _isApplyingSlashCommand = false;
                return;
            }

            if (sender is not TextBox textBox)
                return;

            var text = textBox.Text;

            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("/", StringComparison.Ordinal))
            {
                HideSlashCommandsFlyout();
                return;
            }

            var matches = _slashCommands
                .Where(cmd => cmd.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                HideSlashCommandsFlyout();
                return;
            }

            foreach (var item in SlashCommandsFlyout.Items.OfType<MenuFlyoutItem>().ToList())
            {
                item.Click -= SlashCommandFlyoutItem_Click;
            }

            SlashCommandsFlyout.Items.Clear();

            foreach (var command in matches)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{command.Command} — {command.Description}",
                    Tag = command.Command
                };
                item.Click += SlashCommandFlyoutItem_Click;
                SlashCommandsFlyout.Items.Add(item);
            }

            if (!SlashCommandsFlyout.IsOpen)
            {
                SlashCommandsFlyout.ShowAt(textBox);
            }
        }

        private void SlashCommandFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string command)
            {
                _isApplyingSlashCommand = true;
                InputBox.Text = command + " ";
                InputBox.SelectionStart = InputBox.Text.Length;
                InputBox.Focus(FocusState.Programmatic);
                HideSlashCommandsFlyout();
            }
        }

        private static string GetRichText(RichTextBlock block)
        {
            var sb = new StringBuilder();
            foreach (var b in block.Blocks)
            {
                if (b is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        AppendInlineText(sb, inline);
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private static void AppendInlineText(StringBuilder sb, Inline inline)

        {
            switch (inline)
            {
                case Run run:
                    sb.Append(run.Text);
                    break;
                case LineBreak:
                    sb.Append('\n');
                    break;
                case Span span:
                    foreach (var child in span.Inlines)
                    {
                        AppendInlineText(sb, child);
                    }
                    break;

                case InlineUIContainer container:
                    sb.Append(ExtractTextFromElement(container.Child));
                    break;
            }
        }

        private static string ExtractTextFromElement(UIElement? element)
        {
            switch (element)
            {
                case TextBlock textBlock:
                    if (!string.IsNullOrEmpty(textBlock.Text))
                    {
                        return textBlock.Text;
                    }

                    if (textBlock.Inlines != null && textBlock.Inlines.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var inline in textBlock.Inlines)
                        {
                            AppendInlineText(sb, inline);
                        }
                        return sb.ToString();
                    }

                    return string.Empty;

                case Border border:
                    return ExtractTextFromElement(border.Child);
                case Panel panel:
                    {
                        var sb = new StringBuilder();

                        foreach (var child in panel.Children)
                        {
                            if (child is UIElement uiElement)
                            {
                                sb.Append(ExtractTextFromElement(uiElement));
                            }
                        }
                        return sb.ToString();
                    }
                case ContentControl contentControl:
                    return contentControl.Content switch
                    {
                        string text => text,
                        UIElement uiElement => ExtractTextFromElement(uiElement),
                        _ => string.Empty
                    };
                default:
                    return string.Empty;
            }
        }
        private async void InputBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return;

            e.Handled = true;

            var text = await content.GetTextAsync();
            text = text.Replace("\r", string.Empty).Replace("\n", " ");
            if (sender is TextBox tb)
            {
                var start = tb.SelectionStart;
                var length = tb.SelectionLength;
                var current = tb.Text;
                tb.Text = current.Substring(0, start) + text + current.Substring(start + length);
                tb.SelectionStart = start + text.Length;
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

        private async void CallUser_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UserInfo user)
            {
                var avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/earth.png");
                var content = $"{App.UserName} a appelé {user.Username}";
                await _service.SendMessage(App.UserName, _service.RoomName, "A Tous", content, avatar, DateTime.Now);
                await _service.CallUser(App.UserName, _service.RoomName, user.Username);
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

        private void SaveUserOrder()
        {
            AppSettings.UserOrder = ConnectedUsers.Select(u => u.Username).ToList();
        }

        private void HideSlashCommandsFlyout()
        {
            if (SlashCommandsFlyout == null)
                return;

            foreach (var item in SlashCommandsFlyout.Items.OfType<MenuFlyoutItem>().ToList())
            {
                item.Click -= SlashCommandFlyoutItem_Click;
            }

            SlashCommandsFlyout.Items.Clear();

            if (SlashCommandsFlyout.IsOpen)
            {
                SlashCommandsFlyout.Hide();
            }
        }

        private sealed record SlashCommandInfo(string Command, string Description);
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
