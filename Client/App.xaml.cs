using Microsoft.UI.Xaml;
using System;
using Client.Services;

namespace Client
{
    public partial class App : Application
    {
        public static SignalRService ChatService { get; } = new SignalRService();

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }

        private Window? m_window;
    }
}
