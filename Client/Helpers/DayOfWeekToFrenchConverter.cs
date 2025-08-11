using Microsoft.UI.Xaml.Data;
using System;

namespace Client.Helpers
{
    public class DayOfWeekToFrenchConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DayOfWeek day)
            {
                return day switch
                {
                    DayOfWeek.Monday => "Lundi",
                    DayOfWeek.Tuesday => "Mardi",
                    DayOfWeek.Wednesday => "Mercredi",
                    DayOfWeek.Thursday => "Jeudi",
                    DayOfWeek.Friday => "Vendredi",
                    DayOfWeek.Saturday => "Samedi",
                    DayOfWeek.Sunday => "Dimanche",
                    _ => string.Empty
                };
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string s)
            {
                switch (s.ToLower())
                {
                    case "lundi": return DayOfWeek.Monday;
                    case "mardi": return DayOfWeek.Tuesday;
                    case "mercredi": return DayOfWeek.Wednesday;
                    case "jeudi": return DayOfWeek.Thursday;
                    case "vendredi": return DayOfWeek.Friday;
                    case "samedi": return DayOfWeek.Saturday;
                    case "dimanche": return DayOfWeek.Sunday;
                }
            }
            return DayOfWeek.Monday;
        }
    }
}
