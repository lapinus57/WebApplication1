using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Client.Models;

namespace Client.Pages
{
    public sealed partial class ReminderPage : Page
    {
        public ObservableCollection<string> Times { get; } = new();

        public ReminderPage()
        {
            this.InitializeComponent();
            TimesList.ItemsSource = Times;
            Loaded += ReminderPage_Loaded;
        }

        private async void ReminderPage_Loaded(object sender, RoutedEventArgs e)
        {
            var cfg = await App.ChatService.GetReminderAsync();
            if (cfg != null)
            {
                MessageBox.Text = cfg.Message;
                Times.Clear();
                foreach (var t in cfg.Times)
                    Times.Add(t);
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

        private void TimesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (TimesList.SelectedItem is string t)
                Times.Remove(t);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = new ReminderConfig
            {
                Message = MessageBox.Text,
                Times = Times.ToList(),
                IsEnabled = EnableSwitch.IsOn
            };
            await App.ChatService.SendReminderAsync(cfg);
        }
    }
}
