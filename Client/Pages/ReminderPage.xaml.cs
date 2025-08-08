using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using Client.Models;

namespace Client.Pages
{
    public sealed partial class ReminderPage : Page
    {
        public ObservableCollection<string> Times { get; } = new();
        public ObservableCollection<ReminderItem> Reminders { get; } = new();

        public ReminderPage()
        {
            this.InitializeComponent();
            TimesList.ItemsSource = Times;
            RemindersList.ItemsSource = Reminders;
            Loaded += ReminderPage_Loaded;
        }

        private async void ReminderPage_Loaded(object sender, RoutedEventArgs e)
        {
            var cfg = await App.ChatService.GetReminderAsync();
            if (cfg != null)
            {
                Reminders.Clear();
                foreach (var r in cfg.Reminders)
                    Reminders.Add(r);
                EnableSwitch.IsOn = cfg.IsEnabled;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void AddTime_Click(object sender, RoutedEventArgs e)
        {
            var t = TimePicker.Time.ToString("hh\\:mm");
            if (!Times.Contains(t))
                Times.Add(t);
        }

        private void RemoveTime_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string t)
                Times.Remove(t);
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageBox.Text) || Times.Count == 0)
                return;
            var item = new ReminderItem
            {
                Message = MessageBox.Text,
                Times = Times.ToList()
            };
            Reminders.Add(item);
            MessageBox.Text = string.Empty;
            Times.Clear();
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ReminderItem item)
                Reminders.Remove(item);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = new ReminderConfig
            {
                Reminders = Reminders.ToList(),
                IsEnabled = EnableSwitch.IsOn
            };
            await App.ChatService.SendReminderAsync(cfg);
        }
    }
}
