namespace ChatServeur
{
    public static class ColorHelpers
    {
        public static string GetForeground(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return "#FF000000";
            hex = hex.Trim().TrimStart('#');
            try
            {
                byte r, g, b;
                if (hex.Length == 8)
                {
                    r = Convert.ToByte(hex.Substring(2, 2), 16);
                    g = Convert.ToByte(hex.Substring(4, 2), 16);
                    b = Convert.ToByte(hex.Substring(6, 2), 16);
                }
                else if (hex.Length == 6)
                {
                    r = Convert.ToByte(hex.Substring(0, 2), 16);
                    g = Convert.ToByte(hex.Substring(2, 2), 16);
                    b = Convert.ToByte(hex.Substring(4, 2), 16);
                }
                else
                {
                    var c = System.Drawing.ColorTranslator.FromHtml("#" + hex);
                    r = c.R; g = c.G; b = c.B;
                }
                double luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
                return luminance > 0.5 ? "#FF000000" : "#FFFFFFFF";
            }
            catch
            {
                return "#FF000000";
            }
        }
    }
}