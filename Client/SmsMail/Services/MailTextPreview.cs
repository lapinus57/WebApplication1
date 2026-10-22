using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit.Text;

namespace Client.SmsMail.Services
{
    // Display only: the HTML body used by SMTP is never replaced.
    public static class MailTextPreview
    {
        public static string ForTable(string? html, string people)
        {
            var text = Convert(html);
            foreach (var person in people.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = person.Trim();
                if (name.Length == 0) continue;
                // Only remove a trailing time on a known person's own line.
                // The appointment time and arrival information elsewhere remain.
                text = Regex.Replace(text, @"(?m)^(?<person>(?:• )?" + Regex.Escape(name)
                    + @")\s+[—–-]\s+\d{2}:\d{2}\s*$", "${person}");
            }
            return text;
        }

        public static string Convert(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            using var reader = new StringReader(html);
            var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
            var output = new StringBuilder();
            string? ignored = null;
            while (tokenizer.ReadNextToken(out var token))
            {
                if (token is HtmlTagToken tag)
                {
                    var name = tag.Name.ToLowerInvariant();
                    if (ignored != null)
                    {
                        if (tag.IsEndTag && name == ignored) ignored = null;
                        continue;
                    }
                    if (!tag.IsEndTag && (name is "head" or "style" or "script" or "template")) { ignored = name; continue; }
                    if (name == "li") { if (output.Length > 0 && output[output.Length - 1] != '\n') output.Append('\n'); if (!tag.IsEndTag) output.Append("• "); }
                    else if (name is "p" or "h1" or "h2" or "h3" or "h4" or "blockquote") output.Append("\n\n");
                    else if (name is "br" or "div" or "tr" or "ul" or "ol" or "section" or "hr") output.Append('\n');
                    else if (name is "td" or "th") output.Append("  ");
                }
                else if (ignored == null && token is HtmlDataToken data)
                    output.Append(Regex.Replace(data.Data, @"\s+", " "));
            }
            var text = Regex.Replace(output.ToString(), @"[^\S\n]+", " ");
            text = Regex.Replace(text, @" *\n *", "\n");
            return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        }
    }
}
