using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Client.Models;
using System;
using Windows.UI;

namespace Client.Helpers
{
    public class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string s)
            {
                try
                {
                    return ColorUtils.FromHex(s);
                }
                catch { }
            }
            return Colors.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Color color)
            {
                return ColorUtils.ToHex(color);
            }
            return "#000000";
        }
    }
}
