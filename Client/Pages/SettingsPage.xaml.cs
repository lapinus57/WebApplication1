using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Helpers;

namespace Client.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            var cfg = MachineConfig.Load();
            ReminderGrid.Visibility = cfg.ShowReminderPage ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Appearance_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(AppearanceSettingsPage));
        }

        private void User_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(UserSettingsPage));
        }

        private void System_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SystemPage));
        }

        private void Exam_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ExamRoomPage));
        }

        private void Connection_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConnectionPage));
        }

        private void Reminder_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ReminderPage));
        }
    }
}
