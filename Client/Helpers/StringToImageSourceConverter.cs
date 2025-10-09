using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Client.Helpers
{
    public class StringToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    return new BitmapImage(new Uri(s));
                }
                catch
                {
                }
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is BitmapImage img && img.UriSource != null)
            {
                return img.UriSource.AbsoluteUri;
            }
            return DependencyProperty.UnsetValue;
        }
    }
}
