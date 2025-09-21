using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using Windows.UI;
using Windows.UI.Text;

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
                    OnPropertyChanged(nameof(SenderColor));
                    OnPropertyChanged(nameof(ForegroundColor));
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
                    OnPropertyChanged(nameof(DestinataireColor));
                }
            }
        }

        public string ForegroundColor =>
            ColorUtils.ToHex(
                ColorUtils.GetContrastingTextColor(
                    ColorUtils.FromHex(SenderColor)));

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
        private static readonly Regex _keyRegex = new(@"\[\[(?<key>[^\[\]]+)\]\]", RegexOptions.Compiled);

        private enum SpecialSegmentType
        {
            Url,
            Key
        }

        private sealed class SpecialSegment
        {
            public int Index { get; init; }
            public int Length { get; init; }
            public string Value { get; init; } = string.Empty;
            public SpecialSegmentType Type { get; init; }
        }

        public void FormatContent(object sender, RoutedEventArgs e)
        {
            if (sender is not RichTextBlock block)
                return;

            InlineCollection? container = null;
            Brush? foreground = null;
            double fontSize = block.FontSize;
            FontFamily? fontFamily = block.FontFamily;

            if (block.FindName("ContentParagraph") is Paragraph paragraph)
            {
                container = paragraph.Inlines;
                foreground = paragraph.Foreground;
                if (paragraph.FontSize > 0)
                    fontSize = paragraph.FontSize;
                if (paragraph.FontFamily != null)
                    fontFamily = paragraph.FontFamily;
            }
            else if (block.FindName("ContentSpan") is Span span)
            {
                container = span.Inlines;
                foreground = span.Foreground;
                if (span.FontSize > 0)
                    fontSize = span.FontSize;
                if (span.FontFamily != null)
                    fontFamily = span.FontFamily;
            }
            if (container is null)
                return;

            container.Clear();

            var text = Content;
            if (string.IsNullOrEmpty(text))
                return;

            var segments = new List<SpecialSegment>();

            foreach (Match match in _keyRegex.Matches(text))
            {
                var keyText = match.Groups["key"].Value.Trim();
                if (string.IsNullOrEmpty(keyText))
                    continue;

                segments.Add(new SpecialSegment
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = keyText,
                    Type = SpecialSegmentType.Key
                });
            }

            foreach (Match match in _urlRegex.Matches(text))
            {
                segments.Add(new SpecialSegment
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = match.Value,
                    Type = SpecialSegmentType.Url
                });
            }

            segments.Sort((a, b) => a.Index.CompareTo(b.Index));

            var currentIndex = 0;
            foreach (var segment in segments)
            {
                if (segment.Index < currentIndex)
                    continue;

                if (segment.Index > currentIndex)
                {
                    container.Add(new Run
                    {
                        Text = text.Substring(currentIndex, segment.Index - currentIndex)
                    });
                }

                switch (segment.Type)
                {
                    case SpecialSegmentType.Key:
                        container.Add(CreateKeyInline(segment.Value, fontSize, fontFamily, foreground));
                        break;
                    case SpecialSegmentType.Url:
                        AddUrlInline(container, segment.Value, foreground);
                        break;
                }

                currentIndex = segment.Index + segment.Length;
            }

            if (currentIndex < text.Length)
            {
                container.Add(new Run { Text = text[currentIndex..] });
            }
        }

        private static void AddUrlInline(InlineCollection container, string url, Brush? foreground)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                container.Add(new Run { Text = url });
                return;
            }

            var link = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = foreground
            };
            link.Inlines.Add(new Run { Text = url });
            link.Click += async (_, _) => await Launcher.LaunchUriAsync(uri);
            container.Add(link);
        }

        private static Inline CreateKeyInline(string keyText, double fontSize, FontFamily? fontFamily, Brush? foreground)
        {
            var baseColor = Colors.Gray;
            if (foreground is SolidColorBrush solid)
            {
                baseColor = solid.Color;
            }

            var backgroundColor = Color.FromArgb(40, baseColor.R, baseColor.G, baseColor.B);
            var borderColor = Color.FromArgb(120, baseColor.R, baseColor.G, baseColor.B);

            var textBlock = new TextBlock
            {
                Text = keyText,
                FontSize = fontSize > 0 ? fontSize : 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (foreground != null)
            {
                textBlock.Foreground = foreground;
            }

            if (fontFamily != null)
            {
                textBlock.FontFamily = fontFamily;
            }

            var border = new Border
            {
                Background = new SolidColorBrush(backgroundColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 2, 0),
                Child = textBlock
            };

            return new InlineUIContainer
            {
                Child = border,
                BaselineAlignment = BaselineAlignment.Center
            };
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
