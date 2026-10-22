using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RtfPipe;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace EyeChat.Fundus;

/// <summary>Read clinical rich text without rendering it or accessing external resources.</summary>
public static class FoMedicalText
{
    private const int MaxSourceLength = 1_000_000;
    private const int MaxTextLength = 100_000;
    private const int MaxDepth = 64;
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(250);
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "div", "dl", "dt", "dd", "fieldset", "figcaption", "figure",
        "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol",
        "p", "pre", "section", "table", "thead", "tbody", "tfoot", "tr", "ul"
    };
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "template", "noscript", "iframe", "object", "embed", "svg", "canvas",
        "img", "input", "button", "select", "textarea", "del", "s", "strike"
    };
    private static readonly string HtmlPattern = @"<!--|<!DOCTYPE\s+html\b|</?(?:" + string.Join("|",
        Blocks.Concat(Ignored).Concat(new[] { "html", "body", "title", "meta", "link", "span", "font", "b", "strong",
            "i", "em", "u", "ins", "br", "a", "td", "th", "sub", "sup", "code", "small", "big", "center", "label" }).Distinct()) + @")\b[^>]*>";

    static FoMedicalText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Read(object? value)
    {
        if (value == null || value == DBNull.Value) return "";
        if (value is not string source) throw new FoDataException("Le champ médical sélectionné n’est pas un champ texte.");
        if (source.Length > MaxSourceLength) throw new FoDataException("Compte rendu trop volumineux pour la conversion FO. Aucun résultat partiel n’a été enregistré.");
        try
        {
            var trimmed = source.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            string text;
            if (trimmed.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRtf(trimmed);
                // Only parse the document. No native editor, browser, attachment extraction or image loading.
                var html = Rtf.ToHtml(trimmed, new RtfHtmlSettings { ImageUriGetter = _ => "", AttachmentRenderer = (_, _) => { } });
                text = HtmlText(html);
            }
            else if (IsHtml(source)) text = HtmlText(source);
            else text = source; // Plain medical comparisons such as "PIO < 20" must remain untouched.
            if (text.Length > MaxTextLength) throw new FoDataException("Texte médical trop volumineux pour l’analyse FO. Aucun résultat partiel n’a été enregistré.");
            return text;
        }
        catch (FoDataException) { throw; }
        catch
        {
            // Parser exceptions can contain patient data. Do not expose, retain or log the original exception.
            throw new FoDataException("Impossible de convertir un compte rendu RTF/HTML en texte. Son format est invalide ou trop complexe ; aucun résultat partiel n’a été enregistré.");
        }
    }

    private static bool IsHtml(string text) => Regex.IsMatch(text, HtmlPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);

    private static string HtmlText(string html)
    {
        if (html.Length > 4 * MaxSourceLength) throw new FoDataException("Compte rendu trop complexe pour la conversion FO. Aucun résultat partiel n’a été enregistré.");
        var document = new HtmlDocument { OptionMaxNestedChildNodes = MaxDepth, OptionReadEncoding = false };
        document.LoadHtml(html); // LoadHtml never downloads links, images or stylesheets.
        var output = new StringBuilder();
        var stack = new Stack<(HtmlNode Node, bool End, bool Pre)>();
        stack.Push((document.DocumentNode, false, false));
        void Break()
        {
            if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
        }
        while (stack.Count > 0)
        {
            var (node, end, pre) = stack.Pop();
            if (output.Length > 2 * MaxTextLength) throw new FoDataException("Texte médical trop volumineux pour la conversion FO. Aucun résultat partiel n’a été enregistré.");
            if (node.NodeType == HtmlNodeType.Comment) continue;
            if (node.NodeType == HtmlNodeType.Text)
            {
                var value = HtmlEntity.DeEntitize(((HtmlTextNode)node).Text);
                output.Append(pre ? value : Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit));
                continue;
            }
            if (end)
            {
                if (Blocks.Contains(node.Name)) Break();
                else if (node.Name is "td" or "th") output.Append(' ');
                continue;
            }
            if (Ignored.Contains(node.Name) || IsHidden(node)) continue;
            // RtfPipe represents \tab as an empty inline block. Keep a separator between its words.
            if (node.Name == "span" && !node.HasChildNodes && Regex.IsMatch(node.GetAttributeValue("style", ""),
                @"(?:^|;)\s*display\s*:\s*inline-block\b", RegexOptions.IgnoreCase, RegexLimit)) output.Append(' ');
            if (Blocks.Contains(node.Name) || node.Name == "br") Break();
            pre = pre || node.Name == "pre" || Regex.IsMatch(node.GetAttributeValue("style", ""),
                @"(?:^|;)\s*white-space\s*:\s*(?:pre(?:-wrap|-line)?|break-spaces)\b", RegexOptions.IgnoreCase, RegexLimit);
            stack.Push((node, true, pre));
            for (int i = node.ChildNodes.Count - 1; i >= 0; i--) stack.Push((node.ChildNodes[i], false, pre));
        }
        var lines = output.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n').Split('\n');
        return string.Join("\n", lines.Select(line => Regex.Replace(line, @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit).Trim()).Where(line => line.Length > 0));
    }

    private static bool IsHidden(HtmlNode node) => node.Attributes["hidden"] != null || Regex.IsMatch(
        node.GetAttributeValue("style", ""),
        @"(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*(?:hidden|collapse)|text-decoration(?:-line)?\s*:[^;]*\bline-through)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);

    // Bound nesting and reject truncated RTF before passing it to the library. Escaped braces and binary
    // payloads are not groups. This is structural validation only; RtfPipe handles RTF text/encodings.
    private static void ValidateRtf(string source)
    {
        int depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '{')
            {
                if (++depth > MaxDepth) throw new FormatException();
            }
            else if (c == '}')
            {
                if (--depth < 0) throw new FormatException();
                if (depth == 0 && source.AsSpan(i + 1).Trim().Length != 0) throw new FormatException();
            }
            else if (c == '\\')
            {
                if (++i >= source.Length) throw new FormatException();
                if (source[i] == '\'')
                {
                    if (i + 2 >= source.Length || !byte.TryParse(source.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) throw new FormatException();
                    i += 2;
                }
                else if (char.IsAsciiLetter(source[i]))
                {
                    int start = i;
                    while (i < source.Length && char.IsAsciiLetter(source[i])) i++;
                    var word = source[start..i];
                    int numberStart = i;
                    if (i < source.Length && source[i] == '-') i++;
                    while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                    var number = source[numberStart..i];
                    if (i < source.Length && source[i] == ' ') i++;
                    if (word == "bin")
                    {
                        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0 || length > source.Length - i) throw new FormatException();
                        i += length;
                    }
                    i--;
                }
            }
        }
        if (depth != 0) throw new FormatException();
    }
}
