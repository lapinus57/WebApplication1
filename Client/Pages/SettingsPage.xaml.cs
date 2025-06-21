using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
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
        }

        private void Exam_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ExamRoomPage));
        }
    }
}
