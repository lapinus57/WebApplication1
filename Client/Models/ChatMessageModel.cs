using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml;
using System;
using System.Text.RegularExpressions;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;

namespace Client.Models
{
    public class ChatMessageModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Id { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string Destinataire { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;

        private string _senderColor = string.Empty;
        public string SenderColor
        {
            get => _senderColor;
            set
            {
                if (_senderColor != value)
                {
                    _senderColor = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SenderColor)));
                }
            }
        }

        private string _destinataireColor = string.Empty;
        public string DestinataireColor
        {
            get => _destinataireColor;
            set
            {
                if (_destinataireColor != value)
                {
                    _destinataireColor = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DestinataireColor)));
                }
            }
        }

        public DateTime Timestamp { get; set; }
        public bool IsDeleted { get; set; }

        /// <summary>
        /// IrcTimestamp wrapped in square brackets for display.
        /// </summary>
        public string IrcTimestampWithBrackets => $"[{IrcTimestamp}]";
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

            InlineCollection? container = null;
            Brush? foreground = null;

            if (block.FindName("ContentParagraph") is Paragraph paragraph)
            {
                container = paragraph.Inlines;
                foreground = paragraph.Foreground;
            }
            else if (block.FindName("ContentSpan") is Span span)
            {
                container = span.Inlines;
                foreground = span.Foreground;
            }
            if (container is null)
                return;

            container.Clear();

            var text = Content;
            var last = 0;
            foreach (Match match in _urlRegex.Matches(text))
            {
                if (match.Index > last)
                    container.Add(new Run { Text = text.Substring(last, match.Index - last) });

                var url = match.Value;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    container.Add(new Run { Text = url });
                }
                else
                {
                    var link = new Hyperlink
                    {
                        NavigateUri = uri,
                        Foreground = foreground
                    };
                    link.Inlines.Add(new Run { Text = url });
                    link.Click += async (_, _) => await Launcher.LaunchUriAsync(uri);
                    container.Add(link);
                }

                last = match.Index + match.Length;
            }
            if (last < text.Length)
                container.Add(new Run { Text = text[last..] });
        }
    }
}
