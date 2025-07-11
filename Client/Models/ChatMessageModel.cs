using System;


namespace Client.Models
{
    public class ChatMessageModel
    {
        public string Sender { get; set; } = string.Empty;
        public string Destinataire { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public string SenderColor => ColorUtils.ToHex(ColorUtils.GetPastelColor(Sender));
        public string SenderTextColor => ColorUtils.ToHex(ColorUtils.GetContrastingTextColor(ColorUtils.GetPastelColor(Sender)));

        public string TimeFormatted => Timestamp.ToString("dd/MM/yy HH:mm");
        public string Header => $"{Sender} ({Room}) :";
        public string IrcHeader => $"[{Timestamp:dd/MM/yy HH:mm}] <{Sender} ({Room})> <{Destinataire}> : {Content}";
    }
}
