using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using Client.Models;

namespace Client.Helpers
{
    public class StringToImageSourceConverter : IValueConverter
    {
        private static readonly Uri FallbackAvatarUri = new(UserInfo.DefaultAvatar);
        private static readonly BitmapImage FallbackAvatar = new(FallbackAvatarUri);
        private static readonly string[] SupportedSchemes =
        {
            "http", "https", "ms-appx", "ms-appdata"
        };

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var candidate = value switch
            {
                Uri uri => uri,
                string text when Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) => uri,
                _ => null
            };

            if (candidate is { IsAbsoluteUri: true } &&
                Array.Exists(SupportedSchemes, scheme =>
                    string.Equals(scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    return new BitmapImage(candidate);
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
