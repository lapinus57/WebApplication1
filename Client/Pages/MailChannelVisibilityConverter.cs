using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Client.Pages
{
    public sealed class MailChannelVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            string.Equals(value as string, "Mail", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
