using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Client.Helpers
{
    public class StringToImageSourceConverter : IValueConverter
    {
        private static readonly Uri FallbackAvatarUri = new("ms-appx:///Assets/utilisateur.png");
        private static readonly BitmapImage FallbackAvatar = new(FallbackAvatarUri);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Uri uriValue)
            {
                return new BitmapImage(uriValue);
            }

            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    return new BitmapImage(new Uri(s, UriKind.RelativeOrAbsolute));
                }
                catch
                {
                    return FallbackAvatar;
                }
            }
            return FallbackAvatar;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is BitmapImage img && img.UriSource != null)
            {
                return img.UriSource.AbsoluteUri;
            }
            return FallbackAvatarUri.AbsoluteUri;
        }
    }
}
