using System;
using Microsoft.UI;

namespace Client.Models
{
    public static class ColorHelper
    {
        public static Windows.UI.Color FromArgb(string hex)
        {
            return Microsoft.UI.ColorHelper.FromArgb(
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16),
                Convert.ToByte(hex.Substring(7, 2), 16));
        }
    }
}
