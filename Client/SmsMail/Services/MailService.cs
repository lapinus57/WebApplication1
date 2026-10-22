using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Client.SmsMail.Configuration;

namespace Client.SmsMail.Services
{
    public sealed class MailService
    {
        private readonly EmailSettings _settings;
        public MailService(EmailSettings settings) => _settings = settings;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || _settings.SmtpPort < 1 || _settings.SmtpPort > 65535
                || !MailAddress.TryCreate(_settings.FromAddress, out _) || !MailAddress.TryCreate(_settings.SenderAddress, out _)
                || string.IsNullOrWhiteSpace(_settings.UserName) || string.IsNullOrWhiteSpace(_settings.Password))
                throw new InvalidOperationException("Complétez les paramètres SMTP (hôte, port, compte et expéditeur).");
        }

        public async Task SendAsync(PreparedMessage prepared)
        {
            Validate();
            using var message = CreateMessage(prepared);
            using var smtp = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(_settings.UserName, _settings.Password), Timeout = 30000
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await smtp.SendMailAsync(message, timeout.Token);
        }

        public MailMessage CreateMessage(PreparedMessage prepared)
        {
            var calendar = prepared.IncludeCalendar ? CalendarInvitation.Create(prepared, _settings.FromAddress) : null;
            var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromDisplayName),
                Sender = new MailAddress(_settings.SenderAddress),
                Subject = prepared.Subject, Body = prepared.Body, IsBodyHtml = true,
                DeliveryNotificationOptions = DeliveryNotificationOptions.OnSuccess | DeliveryNotificationOptions.OnFailure | DeliveryNotificationOptions.Delay
            };
            message.To.Add(new MailAddress(prepared.Contact));
            message.Headers.Add("Content-Language", "fr");
            message.Headers.Add("Message-ID", "<" + prepared.Key + "@eyechat.local>");
            message.Headers.Add("Disposition-Notification-To", _settings.SenderAddress);
            message.BodyEncoding = Encoding.UTF8;
            message.SubjectEncoding = Encoding.UTF8;
            if (calendar != null)
            {
                var contentType = new ContentType("text/calendar") { CharSet = "utf-8" };
                contentType.Parameters.Add("method", "PUBLISH");
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(calendar, contentType));
                var attachment = Attachment.CreateAttachmentFromString(calendar, "rendez-vous.ics", Encoding.UTF8, "text/calendar");
                attachment.ContentType.Parameters.Add("method", "PUBLISH");
                message.Attachments.Add(attachment);
            }
            return message;
        }
    }
}

