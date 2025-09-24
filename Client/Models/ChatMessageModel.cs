using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Windows.System;
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
        private static readonly Regex _combinationRegex = new(@"\{\{(?<combo>.*?)\}\}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly HashSet<string> _knownKeyTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "CTRL", "CONTROL", "ALT", "ALTGR", "ALTGRAPH", "SHIFT", "MAJ", "MAJUSCULE",
            "CMD", "COMMAND", "COMMANDE", "WINDOWS", "WIN", "SUPER", "META", "OPTION",
            "OPT", "FN", "MENU", "APPS", "APP", "ENTER", "ENTREE", "ENTRÉE", "RETURN",
            "TAB", "TABULATION", "ESC", "ESCAPE", "ECHAP", "ÉCHAP", "SPACE", "SPACEBAR",
            "ESPACE", "BACKSPACE", "SUPPR", "DELETE", "DEL", "INS", "INSERT", "HOME",
            "END", "PGUP", "PAGEUP", "PGDN", "PAGEDOWN", "UP", "DOWN", "LEFT", "RIGHT",
            "PAUSE", "BREAK", "PRINTSCREEN", "IMPR", "IMPRECRAN", "IMPRIMECRAN", "PRTSC",
            "SCROLLLOCK", "SCRLK", "CAPSLOCK", "VERRMAJ", "NUMLOCK", "VERRNUM"
        };
        private static readonly HashSet<char> _punctuationKeyCharacters = new(new[]
        {
            '`', '~', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '_', '=', '+',
            '[', ']', '{', '}', '\\', '|', ';', ':', '\'', '"', ',', '.', '<', '>', '/', '?'
        });
        private static readonly HashSet<char> _tokenTrimCharacters = new(new[] { '(', ')', '[', ']', '{', '}', '<', '>', '\'', '"', '`' });

        private enum SpecialSegmentType
        {
            Key,
            Url,
            Combination
        }

        private sealed class SpecialSegment
        {
            public int Index { get; init; }
            public int Length { get; init; }
            public string Value { get; init; } = string.Empty;
            public SpecialSegmentType Type { get; init; }
            public string Original { get; init; } = string.Empty;
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
            var combinationRanges = new List<(int Start, int End)>();

            foreach (Match match in _combinationRegex.Matches(text))
            {
                var combinationText = match.Groups["combo"].Value.Trim();
                if (string.IsNullOrEmpty(combinationText))
                    continue;

                if (!_keyRegex.IsMatch(combinationText))
                    continue;

                segments.Add(new SpecialSegment
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = combinationText,
                    Original = match.Value,
                    Type = SpecialSegmentType.Combination
                });

                combinationRanges.Add((match.Index, match.Index + match.Length));
            }

            foreach (Match match in _keyRegex.Matches(text))
            {
                var keyText = match.Groups["key"].Value.Trim();
                if (string.IsNullOrEmpty(keyText))
                    continue;

                if (IsInsideAnyRange(match.Index, match.Length, combinationRanges))
                    continue;

                segments.Add(new SpecialSegment
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = keyText,
                    Original = match.Value,
                    Type = SpecialSegmentType.Key
                });
            }

            foreach (Match match in _urlRegex.Matches(text))
            {
                if (IsInsideAnyRange(match.Index, match.Length, combinationRanges))
                    continue;

                segments.Add(new SpecialSegment
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = match.Value,
                    Original = match.Value,
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
                    AddPlainTextInline(container,
                        text.Substring(currentIndex, segment.Index - currentIndex),
                        fontSize,
                        fontFamily,
                        foreground);
                }

                switch (segment.Type)
                {
                    case SpecialSegmentType.Key:
                        container.Add(CreateKeyInline(segment.Value, fontSize, fontFamily, foreground));
                        break;
                    case SpecialSegmentType.Url:
                        AddUrlInline(container, segment.Value, foreground);
                        break;
                    case SpecialSegmentType.Combination:
                        container.Add(CreateCombinationInline(segment.Value, segment.Original, fontSize, fontFamily, foreground));
                        break;
                }

                currentIndex = segment.Index + segment.Length;
            }

            if (currentIndex < text.Length)
            {
                AddPlainTextInline(container,
                    text[currentIndex..],
                    fontSize,
                    fontFamily,
                    foreground);
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

        private static void AddPlainTextInline(
            InlineCollection container,
            string text,
            double fontSize,
            FontFamily? fontFamily,
            Brush? foreground,
            bool allowTightConnectors = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var current = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '+')
                    continue;


                if (!ShouldTreatPlusAsConnector(text, i, allowTightConnectors))

                    continue;

                if (i > current)
                {
                    container.Add(CreateTextRun(text.Substring(current, i - current), fontSize, fontFamily, foreground));
                }

                container.Add(CreateConnectorInline(fontSize, fontFamily, foreground));
                current = i + 1;
            }

            if (current < text.Length)
            {
                container.Add(CreateTextRun(text.Substring(current), fontSize, fontFamily, foreground));
            }
        }

        private static Inline CreateConnectorInline(double fontSize, FontFamily? fontFamily, Brush? foreground)
        {
            var resolvedFontSize = fontSize > 0 ? fontSize : 14;
            var verticalPadding = Math.Max(1d, Math.Round(resolvedFontSize * 0.15));
            var horizontalMargin = Math.Max(0d, Math.Round(resolvedFontSize * 0.05));

            var textBlock = new TextBlock
            {
                Text = "+",
                FontSize = resolvedFontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextLineBounds = TextLineBounds.Tight,
                Padding = new Thickness(0, verticalPadding, 0, verticalPadding),
                Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0)

            };

            if (foreground != null)
            {
                textBlock.Foreground = foreground;
            }

            if (fontFamily != null)
            {
                textBlock.FontFamily = fontFamily;
            }

            return new InlineUIContainer
            {
                Child = textBlock,

            };
        }

        private static Run CreateTextRun(string text, double fontSize, FontFamily? fontFamily, Brush? foreground)
        {
            var run = new Run
            {
                Text = text
            };

            if (fontSize > 0)
            {
                run.FontSize = fontSize;
            }

            if (foreground != null)
            {
                run.Foreground = foreground;
            }

            if (fontFamily != null)
            {
                run.FontFamily = fontFamily;
            }

            return run;

        }

        private static bool ShouldTreatPlusAsConnector(string text, int index, bool allowTightConnectors)
        {
            if (text[index] != '+')
                return false;

            if (allowTightConnectors)
                return true;

            if (IsWhitespaceSeparatedPlus(text, index))
                return true;

            var leftToken = ExtractKeyToken(text, index - 1, -1);
            var rightToken = ExtractKeyToken(text, index + 1, 1);

            if (string.IsNullOrEmpty(leftToken) || string.IsNullOrEmpty(rightToken))
                return false;

            var leftStrong = IsStrongKeyToken(leftToken);
            var rightStrong = IsStrongKeyToken(rightToken);

            if (!leftStrong && !rightStrong)
                return false;

            var leftIsKey = leftStrong || IsSingleKeyToken(leftToken);
            var rightIsKey = rightStrong || IsSingleKeyToken(rightToken);

            if (!leftIsKey || !rightIsKey)
                return false;

            if (!leftStrong && !rightStrong && IsDigitToken(leftToken) && IsDigitToken(rightToken))
                return false;

            return true;
        }

        private static bool IsWhitespaceSeparatedPlus(string text, int index)
        {
            var hasSpaceBefore = index == 0 || char.IsWhiteSpace(text[index - 1]);
            var hasSpaceAfter = index >= text.Length - 1 || char.IsWhiteSpace(text[index + 1]);

            return hasSpaceBefore && hasSpaceAfter;
        }

        private static string ExtractKeyToken(string text, int startIndex, int direction)
        {
            var i = startIndex;

            while (i >= 0 && i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i += direction;
            }

            if (i < 0 || i >= text.Length)
                return string.Empty;

            var tokenStart = i;
            var tokenEnd = i;

            if (direction < 0)
            {
                var j = i;
                while (j >= 0 && !char.IsWhiteSpace(text[j]) && text[j] != '+')
                {
                    tokenStart = j;
                    j--;
                }

                return text.Substring(tokenStart, tokenEnd - tokenStart + 1);
            }
            else
            {
                var j = i;
                while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '+')
                {
                    tokenEnd = j;
                    j++;
                }

                return text.Substring(tokenStart, tokenEnd - tokenStart + 1);
            }
        }

        private static bool IsStrongKeyToken(string token)
        {
            var trimmed = TrimToken(token);
            if (string.IsNullOrEmpty(trimmed))
                return false;

            if (trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
                return true;

            if (_knownKeyTokens.Contains(trimmed))
                return true;

            if (trimmed.Length >= 2 && (trimmed[0] == 'F' || trimmed[0] == 'f') && int.TryParse(trimmed[1..], out var functionNumber) && functionNumber >= 1 && functionNumber <= 24)
                return true;

            if (trimmed.Length > 6 && trimmed.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase) && int.TryParse(trimmed[6..], out _))
                return true;

            if (trimmed.Length > 3 && trimmed.StartsWith("NUM", StringComparison.OrdinalIgnoreCase) && int.TryParse(trimmed[3..], out _))
                return true;

            return false;
        }

        private static bool IsSingleKeyToken(string token)
        {
            var trimmed = TrimToken(token);
            if (string.IsNullOrEmpty(trimmed))
                return false;

            if (trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
                return true;

            if (trimmed.Length == 1)
            {
                var ch = trimmed[0];
                return char.IsLetterOrDigit(ch) || _punctuationKeyCharacters.Contains(ch);
            }

            return false;
        }

        private static bool IsDigitToken(string token)
        {
            var trimmed = TrimToken(token);
            if (string.IsNullOrEmpty(trimmed))
                return false;

            foreach (var ch in trimmed)
            {
                if (!char.IsDigit(ch))
                    return false;
            }

            return true;
        }

        private static string TrimToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            var trimmed = token.Trim();
            var start = 0;
            var end = trimmed.Length;

            while (start < end && _tokenTrimCharacters.Contains(trimmed[start]))
            {
                start++;
            }

            while (end > start && _tokenTrimCharacters.Contains(trimmed[end - 1]))
            {
                end--;
            }

            return start == 0 && end == trimmed.Length
                ? trimmed
                : trimmed.Substring(start, end - start);
        }

        private static Inline CreateKeyInline(string keyText, double fontSize, FontFamily? fontFamily, Brush? foreground)
        {
            var baseColor = foreground is SolidColorBrush solid
                ? solid.Color
                : Color.FromArgb(255, 128, 128, 128);

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
                VerticalAlignment = VerticalAlignment.Center,
                Child = textBlock
            };

            return new InlineUIContainer
            {
                Child = border

            };
        }

        private static Inline CreateCombinationInline(string combinationText, string originalText, double fontSize, FontFamily? fontFamily, Brush? foreground)
        {
            if (string.IsNullOrEmpty(combinationText))
            {
                return new Run { Text = originalText };
            }

            var span = new Span();
            var matches = _keyRegex.Matches(combinationText);

            if (matches.Count == 0)
            {
                return new Run { Text = originalText };
            }

            var currentIndex = 0;
            foreach (Match match in matches)
            {
                if (match.Index > currentIndex)
                {
                    AddPlainTextInline(
                        span.Inlines,
                        combinationText.Substring(currentIndex, match.Index - currentIndex),
                        fontSize,
                        fontFamily,
                        foreground,
                        allowTightConnectors: true);
                }

                var keyText = match.Groups["key"].Value.Trim();
                if (!string.IsNullOrEmpty(keyText))
                {
                    span.Inlines.Add(CreateKeyInline(keyText, fontSize, fontFamily, foreground));
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < combinationText.Length)
            {
                AddPlainTextInline(
                    span.Inlines,
                    combinationText.Substring(currentIndex),
                    fontSize,
                    fontFamily,
                    foreground,
                    allowTightConnectors: true);
            }

            if (span.Inlines.Count == 0)
            {
                return new Run { Text = originalText };
            }

            return span;
        }

        public string GetPlainTextContent()
        {
            if (string.IsNullOrEmpty(Content))
                return string.Empty;

            string ReplaceKey(Match match) => match.Groups["key"].Value.Trim();

            var withoutCombinations = _combinationRegex.Replace(Content, match =>
            {
                var combinationText = match.Groups["combo"].Value;
                if (!_keyRegex.IsMatch(combinationText))
                    return match.Value;

                return _keyRegex.Replace(combinationText, ReplaceKey);
            });

            return _keyRegex.Replace(withoutCombinations, ReplaceKey);
        }

        private static bool IsInsideAnyRange(int index, int length, List<(int Start, int End)> ranges)
        {
            foreach (var (start, end) in ranges)
            {
                if (index >= start && index + length <= end)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
