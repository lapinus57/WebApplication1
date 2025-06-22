using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
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
                    var color = ColorUtils.FromHex(s);
                    if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
                        return new SolidColorBrush(color);

                    return color;
                }
                catch
                {
                }
               
            }
            if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
                return new SolidColorBrush(Colors.Black);
            return Colors.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Color color)
            {
                return ColorUtils.ToHex(color);
            }

            if (value is SolidColorBrush brush)
            {
                return ColorUtils.ToHex(brush.Color);
            }

            return "#000000";
        }
    }
}
