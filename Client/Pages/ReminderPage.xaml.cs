using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Client.Models;
using Microsoft.UI.Text;


namespace Client.Pages
{
    public sealed partial class ReminderPage : Page
    {
        public ObservableCollection<string> Times { get; } = new();
        public ObservableCollection<DayOfWeek> Days { get; } = new();
        public ObservableCollection<ReminderItem> Reminders { get; } = new();
        private ReminderItem? _editing;

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

        private void Day_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && Enum.TryParse<DayOfWeek>(cb.Tag?.ToString(), out var day))
            {
                if (!Days.Contains(day))
                    Days.Add(day);
            }
        }

        private void Day_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && Enum.TryParse<DayOfWeek>(cb.Tag?.ToString(), out var day))
            {
                Days.Remove(day);
            }
        }

        private void RemoveTime_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string t)
                Times.Remove(t);
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            var title = TitleBox.Text.Trim();
            MessageBox.Document.GetText(TextGetOptions.None, out var txt);
            var message = txt.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message) || Times.Count == 0 || Days.Count == 0)
                return;

            if (_editing != null)
            {
                _editing.Title = title;
                _editing.Message = message;
                _editing.Times = Times.ToList();
                _editing.Days = Days.ToList();
                Reminders.Add(_editing);
                _editing = null;
                AddReminderButton.Content = "Ajouter le rappel";
            }
            else
            {
                var item = new ReminderItem
                {
                    Title = title,
                    Message = message,
                    Days = Days.ToList(),
                    Times = Times.ToList()
                };
                Reminders.Add(item);
            }

            TitleBox.Text = string.Empty;
            MessageBox.Document.SetText(TextSetOptions.None, string.Empty);
            Times.Clear();
            Days.Clear();
            foreach (var cb in DaysPanel.Children.OfType<CheckBox>())
                cb.IsChecked = false;
        }

        private void EditReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ReminderItem item)
            {
                TitleBox.Text = item.Title;
                MessageBox.Document.SetText(TextSetOptions.None, item.Message);
                Times.Clear();
                foreach (var t in item.Times)
                    Times.Add(t);
                Days.Clear();
                foreach (var cb in DaysPanel.Children.OfType<CheckBox>())
                {
                    if (Enum.TryParse<DayOfWeek>(cb.Tag?.ToString(), out var day))
                        cb.IsChecked = item.Days.Contains(day);
                }
                foreach (var d in item.Days)
                    Days.Add(d);
                Reminders.Remove(item);
                _editing = item;
                AddReminderButton.Content = "Mettre à jour le rappel";
            }
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
