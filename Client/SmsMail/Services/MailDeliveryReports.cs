using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Client.SmsMail.Configuration;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Client.SmsMail.Services
{
    public sealed record MailDeliveryReport(string MessageKey, string Recipient, string Status);

    public static class MailDeliveryReports
    {
        public static async Task<List<MailDeliveryReport>> ReadAsync(EmailSettings settings, DateTimeOffset since)
        {
            if (string.IsNullOrWhiteSpace(settings.ImapHost) || settings.ImapPort < 1 || settings.ImapPort > 65535
                || string.IsNullOrWhiteSpace(settings.UserName) || string.IsNullOrWhiteSpace(settings.Password)
                || string.IsNullOrWhiteSpace(settings.BounceFilter))
                throw new InvalidOperationException("Renseignez l'hôte IMAP, le port et le filtre des notifications mail. Les identifiants sont ceux du compte SMTP.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var token = timeout.Token;
            using var client = new ImapClient { Timeout = 30000 };
            await client.ConnectAsync(settings.ImapHost, settings.ImapPort, SecureSocketOptions.SslOnConnect, token);
            await client.AuthenticateAsync(settings.UserName, settings.Password, token);
            // Unlike the reference's inbox processing, never change flags or delete mail.
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, token);
            var ids = await client.Inbox.SearchAsync(SearchQuery.DeliveredAfter(since.UtcDateTime.Date)
                .And(SearchQuery.FromContains(settings.BounceFilter)), token);
            var reports = new List<MailDeliveryReport>();
            foreach (var id in ids)
            {
                var message = await client.Inbox.GetMessageAsync(id, token);
                reports.AddRange(Parse(message));
            }
            await client.DisconnectAsync(true, token);
            return reports;
        }

        public static List<MailDeliveryReport> Parse(MimeMessage message)
        {
            var original = message.BodyParts.OfType<MessagePart>().Select(p => p.Message?.MessageId)
                .FirstOrDefault(id => id != null && id.EndsWith("@eyechat.local", StringComparison.OrdinalIgnoreCase));
            if (original == null)
            {
                var raw = message.ToString() + "\n" + string.Join("\n", message.BodyParts.OfType<TextPart>().Select(p => p.Text));
                var match = Regex.Match(raw, @"(?:Original-Message-ID|Message-ID):\s*<?(?<id>[a-zA-Z0-9-]+@eyechat\.local)>?", RegexOptions.IgnoreCase);
                if (match.Success) original = match.Groups["id"].Value;
            }
            if (original == null) return new();
            var key = original.Split('@')[0];
            var result = new List<MailDeliveryReport>();
            foreach (var status in message.BodyParts.OfType<MessageDeliveryStatus>())
            foreach (var fields in status.StatusGroups)
            {
                var recipient = (fields["Final-Recipient"] ?? fields["Original-Recipient"] ?? string.Empty).Split(';').Last().Trim();
                if (!System.Net.Mail.MailAddress.TryCreate(recipient, out var address)) continue;
                var action = fields["Action"]?.Trim().ToLowerInvariant();
                var label = action switch { "failed" => "Rejeté (DSN)", "delivered" => "Reçu (DSN)", "delayed" => "Différé (DSN)", _ => null };
                if (label != null) result.Add(new MailDeliveryReport(key, address.Address.ToLowerInvariant(), label + " : " + fields["Status"]));
            }
            return result;
        }
    }
}
