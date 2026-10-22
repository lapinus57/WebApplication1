using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.SmsMail.Services
{
    public static class MessageSelection
    {
        public static List<PreparedMessage> Visible(IEnumerable<PreparedMessage> messages, string channel, string search)
        {
            search = search.Trim();
            return messages.Where(message => (string.IsNullOrEmpty(channel) || message.Channel == channel)
                && (message.Contact.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || message.Family.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || message.Subject.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(message => message.AppointmentStart)
                .ThenBy(message => message.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(message => message.Channel, StringComparer.Ordinal)
                .ThenBy(message => message.Contact, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<PreparedMessage> ForSending(IEnumerable<PreparedMessage> visible, string? channel = null) =>
            visible.Where(message => message.Selected && message.CanSend && (channel == null || message.Channel == channel)).ToList();
    }
}
