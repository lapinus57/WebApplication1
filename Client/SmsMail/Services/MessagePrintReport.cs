using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Client.SmsMail.Services;

// In-memory document: no scripts, links, remote resources or raw mail HTML.
public static class MessagePrintReport
{
    private static string Cell(string? text) => "<td>" + WebUtility.HtmlEncode(text ?? string.Empty) + "</td>";
    public static string Prepared(IReadOnlyList<PreparedMessage> rows, DateTime day)
    {
        var body = new StringBuilder();
        foreach (var row in rows.OrderBy(r => r.AppointmentStart).ThenBy(r => r.Family).ThenBy(r => r.Channel))
            body.Append("<tr>").Append(Cell(row.AppointmentTimeDisplay + (row.ArrivalHint.Length > 0 ? "\n" + row.ArrivalHint : "")))
                .Append(Cell(row.Channel)).Append(Cell(row.PeopleDisplay)).Append(Cell(row.Contact))
                .Append(Cell(row.TableSummary + (row.TimeExplanation.Length > 0 ? "\n" + row.TimeExplanation : "")))
                .Append(Cell(row.Status + (row.CanSend ? (row.Selected ? "\nCoché" : "\nNon coché") : ""))).Append("</tr>");
        return Document("Rappels — rendez-vous du " + day.ToString("dd/MM/yyyy"),
            $"{rows.Count} message(s) affiché(s) · {rows.Count(r => r.Channel == "Mail")} mail(s) · {rows.Count(r => r.Channel == "SMS")} SMS. Les filtres de l’écran sont appliqués ; toutes les lignes visibles sont incluses.",
            new[] { "RDV / venue", "Canal", "Personnes", "Destinataire", "Résumé / message", "Statut / sélection" }, body.ToString());
    }

    public static List<MessageHistoryEntry> FilterHistory(IEnumerable<MessageHistoryEntry> entries, DateTime? day, string channel, string search)
    {
        search = search.Trim();
        return entries.Where(h => (!day.HasValue || h.CreatedAtLocal?.Date == day.Value.Date)
            && (channel.Length == 0 || h.Channel == channel)
            && (h.Contact.Contains(search, StringComparison.OrdinalIgnoreCase)
                || h.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
                || h.CreatedAt.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(h => h.CreatedAtLocal).ThenBy(h => h.Contact).ToList();
    }

    public static string History(IReadOnlyList<MessageHistoryEntry> rows, DateTime day)
    {
        // Enforce the selected sending day, even if a caller passes unfiltered rows.
        var selected = rows.Where(r => r.CreatedAtLocal?.Date == day.Date).OrderBy(r => r.CreatedAtLocal).ToList();
        var body = new StringBuilder();
        foreach (var row in selected)
            body.Append("<tr>").Append(Cell(row.CreatedAtLocal?.ToString("HH:mm", CultureInfo.InvariantCulture)))
                .Append(Cell(row.Channel)).Append(Cell(row.Contact)).Append(Cell(row.Status))
                .Append(Cell(row.Channel == "Mail" ? MailTextPreview.Convert(row.Body) : row.Body)).Append("</tr>");
        return Document("Historique — envois du " + day.ToString("dd/MM/yyyy"),
            $"{selected.Count} envoi(s) · {selected.Count(r => r.Channel == "Mail")} mail(s) · {selected.Count(r => r.Channel == "SMS")} SMS. Jour d’envoi en heure locale du poste, et non jour du rendez-vous. Filtres de l’écran appliqués. Statuts tels qu’enregistrés.",
            new[] { "Envoi", "Canal", "Destinataire", "Statut", "Message envoyé" }, body.ToString());
    }

    private static string Document(string title, string summary, string[] headers, string rows) =>
        "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\">"
        + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">"
        + "<title>" + WebUtility.HtmlEncode(title) + "</title><style>"
        + "@page{size:A4 landscape;margin:12mm}body{font:11px Arial,sans-serif;color:#172b3a;margin:16px}"
        + "h1{font-size:20px;margin:0 0 8px}p{line-height:1.4}table{width:100%;border-collapse:collapse;table-layout:fixed}"
        + "thead{display:table-header-group}th{text-align:left;background:#e9eff4}th,td{border:1px solid #aebac4;padding:7px;vertical-align:top;overflow-wrap:anywhere;white-space:pre-wrap}"
        + "th:first-child{width:8%}th:nth-child(2){width:6%}tr{break-inside:avoid}footer{margin-top:12px;font-size:10px;color:#455666}"
        + "@media print{body{margin:0}th{print-color-adjust:exact}}"
        + "</style></head><body><h1>" + WebUtility.HtmlEncode(title) + "</h1><p>" + WebUtility.HtmlEncode(summary)
        + "</p><table><thead><tr>" + string.Concat(headers.Select(h => "<th>" + WebUtility.HtmlEncode(h) + "</th>"))
        + "</tr></thead><tbody>" + rows + "</tbody></table><footer>Document confidentiel — usage du cabinet. Impression préparée le "
        + DateTime.Now.ToString("dd/MM/yyyy à HH:mm", CultureInfo.GetCultureInfo("fr-FR")) + "</footer></body></html>";
}
