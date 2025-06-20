using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Client.Models
{
    public static class ColorUtils
    {
        private static readonly IReadOnlyDictionary<string, Color> NamedColors =
           typeof(Colors)
               .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
               .Where(p => p.PropertyType == typeof(Color))
               .ToDictionary(p => p.Name.ToLowerInvariant(), p => (Color)p.GetValue(null)!);

        public static Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Colors.Black;

            hex = hex.Trim();

            if (NamedColors.TryGetValue(hex.ToLowerInvariant(), out var named))
            {
                return named;
            }

            hex = hex.Replace("#", "");
            if (hex.Length == 6)
                hex = "FF" + hex;

            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);

            return Color.FromArgb(a, r, g, b);
        }

        public static string ToHex(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static Color GetContrastingTextColor(Color background)
        {
            double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
            return luminance > 0.5 ? Colors.Black : Colors.White;
        }
    }
}
