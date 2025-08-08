using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Helpers;

namespace Client.Pages
{
    public sealed partial class SystemPage : Page
    {
        public string RoomName { get; set; } = string.Empty;
        public bool ShowTimeModification { get; set; }
        public List<string> Users { get; set; } = new();
        public string DefaultUser { get; set; } = string.Empty;
        public bool ConnectLastUser { get; set; }

        public SystemPage()
        {

            this.InitializeComponent();
            var cfg = MachineConfig.Load();
            ShowTimeModification = cfg.ShowTimeModification;
            RoomName = cfg.RoomName;
            DefaultUser = cfg.DefaultUser;
            ConnectLastUser = cfg.ConnectLastUser;

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*_settings.json");
                Users = files.Select(f => Path.GetFileName(f).Replace("_settings.json", "")).ToList();
            }
            if (!Users.Contains(DefaultUser) && !string.IsNullOrWhiteSpace(DefaultUser))
                Users.Add(DefaultUser);

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
            cfg.DefaultUser = DefaultUser;
            cfg.ConnectLastUser = ConnectLastUser;
            MachineConfig.Save(cfg);
            App.ChatService.RoomName = RoomName;
            await App.ChatService.UpdateRoomNameAsync(RoomName);
        }
    }
}