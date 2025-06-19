using System;

namespace Client.ViewModel
{
    public class SettingsViewModel
    {
        public event Action<Models.ChatStyle>? DisplayStyleChanged;

        public void RaiseDisplayStyleChanged(Models.ChatStyle style)
        {
            DisplayStyleChanged?.Invoke(style);
        }
    }
}
