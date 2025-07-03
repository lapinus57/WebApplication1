using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Client.Helpers;

namespace Client.Pages
{
    public sealed partial class SystemPage : Page
    {
        public string RoomName { get; set; } = string.Empty;
        public bool ShowTimeModification { get; set; }

        public SystemPage()
        {

            this.InitializeComponent();
            var cfg = MachineConfig.Load();
            ShowTimeModification = cfg.ShowTimeModification;
            RoomName = cfg.RoomName;
            DataContext = this;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = MachineConfig.Load();
            cfg.RoomName = RoomName;
            cfg.ShowTimeModification = ShowTimeModification;
            MachineConfig.Save(cfg);
            App.ChatService.RoomName = RoomName;
            await App.ChatService.UpdateRoomNameAsync(RoomName);
        }
    }
}