using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Client.SmsMail.Services;

public sealed class XlsxSheet
{
    public string Name { get; init; } = "Feuille";
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> Widths { get; init; } = Array.Empty<double>();
    public List<IReadOnlyList<string?>> Rows { get; } = new();
}

// Minimal Office Open XML workbook: text cells only, so imported names or remarks
// can never become formulas. Opens in Excel, LibreOffice and Google Sheets.
public static class XlsxWriter
{
    private const int MaxCellLength = 32767;

    public static byte[] Create(IReadOnlyList<XlsxSheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("Au moins une feuille est nécessaire.", nameof(sheets));
        var names = UniqueSheetNames(sheets);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))
                + "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + string.Concat(names.Select((n, i) => $"<sheet name=\"{Escape(n)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
                + "</sheets><definedNames>"
                + string.Concat(sheets.Select((s, i) => s.Headers.Count == 0 ? string.Empty
                    : $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Escape(names[i].Replace("'", "''"))}'!$A$1:${Column(s.Headers.Count - 1)}${s.Rows.Count + 1}</definedName>"))
                + "</definedNames></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>");
            // Style 1: bold header on a light fill. Style 2: wrapped text aligned to the top.
            Add(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>"
                + "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE9EFF4\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf></cellXfs>"
                + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
                + "</styleSheet>");
            for (int i = 0; i < sheets.Count; i++) Add(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(sheets[i]));
        }
        return stream.ToArray();
    }

    private static string Sheet(XlsxSheet sheet)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (sheet.Headers.Count > 0)
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        var columnCount = Math.Max(sheet.Headers.Count, sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Count));
        if (columnCount > 0)
        {
            xml.Append("<cols>");
            for (int c = 0; c < columnCount; c++)
            {
                var width = c < sheet.Widths.Count ? sheet.Widths[c] : 18;
                xml.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            xml.Append("</cols>");
        }
        xml.Append("<sheetData>");
        int rowNumber = 1;
        if (sheet.Headers.Count > 0) AppendRow(xml, rowNumber++, sheet.Headers, 1);
        foreach (var row in sheet.Rows) AppendRow(xml, rowNumber++, row, 2);
        xml.Append("</sheetData>");
        if (sheet.Headers.Count > 0) xml.Append($"<autoFilter ref=\"A1:{Column(sheet.Headers.Count - 1)}{rowNumber - 1}\"/>");
        return xml.Append("</worksheet>").ToString();
    }

    private static void AppendRow(StringBuilder xml, int rowNumber, IReadOnlyList<string?> values, int style)
    {
        xml.Append($"<row r=\"{rowNumber}\">");
        for (int c = 0; c < values.Count; c++)
        {
            var value = values[c] ?? string.Empty;
            if (value.Length > MaxCellLength) value = value.Substring(0, MaxCellLength - 1) + "…";
            xml.Append($"<c r=\"{Column(c)}{rowNumber}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">")
                .Append(Escape(value.Replace("\r\n", "\n"))).Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    public static string Column(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static List<string> UniqueSheetNames(IReadOnlyList<XlsxSheet> sheets)
    {
        var result = new List<string>();
        foreach (var sheet in sheets)
        {
            var name = new string((sheet.Name ?? string.Empty).Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim('\'', ' ');
            if (name.Length == 0) name = "Feuille";
            if (name.Length > 31) name = name.Substring(0, 31);
            var candidate = name;
            for (int n = 2; result.Contains(candidate, StringComparer.OrdinalIgnoreCase); n++)
                candidate = name.Substring(0, Math.Min(name.Length, 28)) + " " + n;
            result.Add(candidate);
        }
        return result;
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!XmlConvert.IsXmlChar(ch) && !char.IsSurrogate(ch)) continue;
            builder.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => ch.ToString() });
        }
        return builder.ToString();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
