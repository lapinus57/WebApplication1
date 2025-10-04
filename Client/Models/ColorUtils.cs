using System;
using System.Collections.Generic;
using System.Globalization;
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

            if (hex.StartsWith("#"))
            {
                hex = hex[1..];
            }

            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }
            else if (hex.Length == 4)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }

            if (hex.Length == 6)
            {
                hex = "FF" + hex;
            }

            if (hex.Length != 8)
            {
                return Colors.Black;
            }

            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return Colors.Black;
            }

            byte a = (byte)((value & 0xFF000000) >> 24);
            byte r = (byte)((value & 0x00FF0000) >> 16);
            byte g = (byte)((value & 0x0000FF00) >> 8);
            byte b = (byte)(value & 0x000000FF);

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
