using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services;

// Spreadsheet versions of the prepared list and of one day of history.
// Mail bodies are converted to readable text: no HTML reaches the file.
public static class MessageExportReport
{
    public static XlsxSheet PreparedSheet(IReadOnlyList<PreparedMessage> rows)
    {
        var sheet = new XlsxSheet
        {
            Name = "Messages",
            Headers = new[] { "RDV", "Venue annoncée", "Canal", "Personnes", "Destinataire", "Statut", "Sélection", "Message / résumé", "Horaires ajustés" },
            Widths = new double[] { 8, 10, 7, 28, 30, 26, 11, 60, 40 }
        };
        foreach (var row in rows.OrderBy(r => r.AppointmentStart).ThenBy(r => r.Family).ThenBy(r => r.Channel))
            sheet.Rows.Add(new[]
            {
                row.AppointmentTimeDisplay,
                row.CalendarStart == default || row.CalendarStart == row.AppointmentStart ? string.Empty : row.CalendarStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                row.Channel, row.PeopleDisplay, row.Contact, row.Status,
                row.CanSend ? (row.Selected ? "Coché" : "Non coché") : "Bloqué",
                row.Channel == "Mail" ? MailText(row.Body) : row.Body,
                row.TimeExplanation
            });
        return sheet;
    }

    public static XlsxSheet FamiliesSheet(IReadOnlyList<RdvFamille> families)
    {
        var sheet = new XlsxSheet
        {
            Name = "Familles",
            Headers = new[] { "Famille", "Personnes", "Nombre", "Téléphones", "E-mails", "Regroupée par" },
            Widths = new double[] { 9, 40, 9, 22, 32, 34 }
        };
        foreach (var family in families.OrderBy(f => f.Numero))
            sheet.Rows.Add(new[]
            {
                family.Numero.ToString(CultureInfo.InvariantCulture), family.MembersDisplay,
                family.PersonCount.ToString(CultureInfo.InvariantCulture), family.PhonesDisplay, family.EmailsDisplay, family.GroupingDisplay
            });
        return sheet;
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

    public static XlsxSheet HistorySheet(IReadOnlyList<MessageHistoryEntry> rows, DateTime day)
    {
        // Enforce the selected sending day, even if a caller passes unfiltered rows.
        var sheet = new XlsxSheet
        {
            Name = "Envois " + day.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Headers = new[] { "Envoi", "Canal", "Destinataire", "Statut", "Message envoyé" },
            Widths = new double[] { 8, 7, 30, 30, 80 }
        };
        foreach (var row in rows.Where(r => r.CreatedAtLocal?.Date == day.Date).OrderBy(r => r.CreatedAtLocal))
            sheet.Rows.Add(new[]
            {
                row.CreatedAtLocal?.ToString("HH:mm", CultureInfo.InvariantCulture), row.Channel, row.Contact, row.Status,
                row.Channel == "Mail" ? MailText(row.Body) : row.Body
            });
        return sheet;
    }

    private static string MailText(string body) =>
        string.Join("\n", MailTextPreview.Convert(body).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
