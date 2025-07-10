using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml;
using System;
using System.Text.RegularExpressions;
using Windows.System;

namespace Client.Models
{
    public class ChatMessageModel
    {
        public int Id { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string Destinataire { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsDeleted { get; set; }

        public string TimeFormatted => Timestamp.ToString("dd/MM/yy HH:mm");
        public string Header => $"{Sender} ({Room}) :";
        /// <summary>
        /// Timestamp formatted depending on the current day. If the message was
        /// sent today, only the time is displayed, otherwise the full date and
        /// time are returned.
        /// </summary>
        public string IrcTimestamp => Timestamp.Date == DateTime.Today
            ? Timestamp.ToString("HH:mm")
            : Timestamp.ToString("dd/MM/yy HH:mm");

        public string IrcHeader => $"[{IrcTimestamp}] <{Sender} ({Room})> <{Destinataire}> : {Content}";

        private static readonly Regex _urlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public void FormatContent(object sender, RoutedEventArgs e)
        {
            if (sender is not RichTextBlock block)
                return;

            if (block.FindName("ContentParagraph") is not Paragraph paragraph)
                return;

            paragraph.Inlines.Clear();

            var text = Content;
            var last = 0;
            foreach (Match match in _urlRegex.Matches(text))
            {
                if (match.Index > last)
                    paragraph.Inlines.Add(new Run { Text = text.Substring(last, match.Index - last) });

                var url = match.Value;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    paragraph.Inlines.Add(new Run { Text = url });
                }
                else
                {
                    var link = new Hyperlink
                    {
                        NavigateUri = uri,
                        Foreground = paragraph.Foreground
                    };
                    link.Inlines.Add(new Run { Text = url });
                    link.Click += async (_, _) => await Launcher.LaunchUriAsync(uri);
                    paragraph.Inlines.Add(link);
                }

                last = match.Index + match.Length;
            }
            if (last < text.Length)
                paragraph.Inlines.Add(new Run { Text = text[last..] });
        }
    }
}
