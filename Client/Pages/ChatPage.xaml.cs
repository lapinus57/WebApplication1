using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Client.Helpers;
using Client.ViewModel;

namespace Client.Pages
{
    public sealed partial class ChatPage : Page
    {
        private readonly SignalRService _service;
        public ObservableCollection<UserInfo> ConnectedUsers => _service.ConnectedUsers;
        public ObservableCollection<object> Messages => _service.Messages;
        public ObservableCollection<Patient> Patients => _service.Patients;
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();

        public ChatPage()
        {
            this.InitializeComponent();
            _service = App.ChatService;
            DataContext = this;

            UsersList.ItemsSource = ConnectedUsers;
            MessagesList.ItemsSource = Messages;
            _service.Dispatcher = this.DispatcherQueue;

            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged += style => { };
            _service.OnMessageReceived += OnMessageReceived;
            this.Loaded += ChatPage_Loaded;
            this.Unloaded += ChatPage_Unloaded;
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _service.OnMessageReceived -= OnMessageReceived;
            ViewModel.ViewModel.SettingsViewModel.DisplayStyleChanged -= style => { };
        }

        private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _service.InitializeAsync();
            Messages.Clear();
            Messages.Add(new LoadMorePlaceholder());
        }

        private void OnMessageReceived(ChatMessageModel chat)
        {
            Messages.Add(chat);
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text;
            var user = UsersList.SelectedItem as UserInfo;
            if (!string.IsNullOrWhiteSpace(text) && user != null)
            {
                await _service.SendMessage("Moi", "RDC", user.Username, text, string.Empty, DateTime.Now);
                InputBox.Text = string.Empty;
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

        private void LoadMore_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder
        }
    }
}
