using Microsoft.UI.Xaml.Data;
using System;

namespace Client.Helpers
{
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }

            if (value is bool?)
            {
                var nullable = (bool?)value;
                return nullable != true;
            }

            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }

            if (value is bool?)
            {
                var nullable = (bool?)value;
                return nullable != true;
            }

            return false;
        }
    }
}
