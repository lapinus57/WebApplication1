using Client.ViewModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Client.Models;
using System.Collections.ObjectModel;

namespace Client.Pages
{
    public sealed partial class UserSettingsPage : Page
    {
        public SettingsViewModel ViewModelSettings { get; } = new();
        public ObservableCollection<ExamOption> ExamOptions { get; } = ExamOption.Load();

        public UserSettingsPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModelSettings;
            ViewModelSettings.Load();
            Debug.WriteLine($"[UserSettingsPage] ViewModel instance: {ViewModelSettings.GetHashCode()}");
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }
}

