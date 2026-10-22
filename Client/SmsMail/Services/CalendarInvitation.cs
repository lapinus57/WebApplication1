using System;
using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace Client.SmsMail.Services
{
    public static class CalendarInvitation
    {
        public static string Create(PreparedMessage prepared, string organizer)
        {
            var from = new MailAddress(organizer).Address;
            var attendee = new MailAddress(prepared.Contact).Address;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(prepared.CalendarTimeZone);
            DateTime ToUtc(DateTime time)
            {
                time = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(time)) throw new InvalidOperationException("L'heure du rendez-vous n'existe pas dans le fuseau sélectionné (changement d'heure).");
                return TimeZoneInfo.ConvertTimeToUtc(time, zone);
            }
            var start = ToUtc(prepared.CalendarStart);
            var end = ToUtc(prepared.CalendarEnd);
            if (end <= start) throw new InvalidOperationException("La fin du rendez-vous doit suivre son début.");
            string Stamp(DateTime value) => value.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var result = new StringBuilder();
            void Line(string value)
            {
                // RFC 5545: at most 75 UTF-8 octets per physical line. Never
                // split a Unicode scalar; continuation whitespace counts too.
                int bytes = 0;
                foreach (var rune in value.EnumerateRunes())
                {
                    int width = rune.Utf8SequenceLength;
                    if (bytes + width > 75) { result.Append("\r\n "); bytes = 1; }
                    result.Append(rune.ToString()); bytes += width;
                }
                result.Append("\r\n");
            }
            Line("BEGIN:VCALENDAR");
            Line("PRODID:-//EyeChat//Rappels de rendez-vous//FR");
            Line("VERSION:2.0"); Line("CALSCALE:GREGORIAN"); Line("METHOD:PUBLISH");
            Line("BEGIN:VEVENT");
            Line("UID:" + Escape(prepared.Key) + "@eyechaT.local");
            Line("DTSTAMP:" + Stamp(DateTime.UtcNow));
            Line("DTSTART:" + Stamp(start)); Line("DTEND:" + Stamp(end));
            Line("SUMMARY:" + Escape(string.IsNullOrWhiteSpace(prepared.CalendarSubject) ? prepared.Subject : prepared.CalendarSubject));
            Line("DESCRIPTION:" + Escape(prepared.CalendarDescription));
            Line("LOCATION:" + Escape(prepared.CalendarLocation));
            Line("ORGANIZER:mailto:" + from);
            Line("ATTENDEE;ROLE=REQ-PARTICIPANT;RSVP=FALSE:mailto:" + attendee);
            Line("SEQUENCE:0"); Line("STATUS:CONFIRMED"); Line("TRANSP:" + (prepared.CalendarIsBusy ? "OPAQUE" : "TRANSPARENT"));
            foreach (var alarm in prepared.CalendarAlarms)
            {
                if (alarm.OffsetMinutes < 0 || alarm.OffsetMinutes > 525600) throw new InvalidOperationException("Une alerte calendrier doit être comprise entre 0 et 525600 minutes avant le rendez-vous.");
                Line("BEGIN:VALARM"); Line("TRIGGER:-PT" + alarm.OffsetMinutes.ToString(CultureInfo.InvariantCulture) + "M");
                Line("ACTION:DISPLAY"); Line("DESCRIPTION:" + Escape(alarm.Label)); Line("END:VALARM");
            }
            Line("END:VEVENT"); Line("END:VCALENDAR");
            return result.ToString();
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r\n", "\n")
            .Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
    }
}

