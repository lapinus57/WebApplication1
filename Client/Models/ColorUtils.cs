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

        public static Color EnsureContrast(Color baseColor, Color background, double minimumContrastRatio = 3.0)
        {
            var currentContrast = GetContrastRatio(baseColor, background);
            if (currentContrast >= minimumContrastRatio)
            {
                return baseColor;
            }

            var bestColor = baseColor;
            double bestContrast = currentContrast;

            var lighterCandidate = baseColor;
            var darkerCandidate = baseColor;

            for (int i = 0; i < 10; i++)
            {
                lighterCandidate = BlendTowards(lighterCandidate, Colors.White, 0.15);
                var lighterContrast = GetContrastRatio(lighterCandidate, background);
                if (lighterContrast > bestContrast)
                {
                    bestContrast = lighterContrast;
                    bestColor = lighterCandidate;
                    if (lighterContrast >= minimumContrastRatio)
                    {
                        return lighterCandidate;
                    }
                }

                darkerCandidate = BlendTowards(darkerCandidate, Colors.Black, 0.15);
                var darkerContrast = GetContrastRatio(darkerCandidate, background);
                if (darkerContrast > bestContrast)
                {
                    bestContrast = darkerContrast;
                    bestColor = darkerCandidate;
                    if (darkerContrast >= minimumContrastRatio)
                    {
                        return darkerCandidate;
                    }
                }
            }

            return bestColor;
        }

        private static Color BlendTowards(Color source, Color target, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            byte a = source.A;
            byte r = Lerp(source.R, target.R, amount);
            byte g = Lerp(source.G, target.G, amount);
            byte b = Lerp(source.B, target.B, amount);
            return Color.FromArgb(a, r, g, b);
        }

        private static byte Lerp(byte start, byte end, double amount)
        {
            return (byte)Math.Clamp(Math.Round(start + (end - start) * amount), 0, 255);
        }

        private static double GetContrastRatio(Color first, Color second)
        {
            var l1 = GetRelativeLuminance(first);
            var l2 = GetRelativeLuminance(second);
            if (l1 < l2)
            {
                (l1, l2) = (l2, l1);
            }

            return (l1 + 0.05) / (l2 + 0.05);
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Linearize(double channel)
            {
                return channel <= 0.03928
                    ? channel / 12.92
                    : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }

            var r = Linearize(color.R / 255.0);
            var g = Linearize(color.G / 255.0);
            var b = Linearize(color.B / 255.0);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
    }
}
