using System;
using System.Net;
using System.Net.Mail;
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
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromDisplayName),
                Sender = new MailAddress(_settings.SenderAddress),
                Subject = prepared.Subject, Body = prepared.Body, IsBodyHtml = true,
                DeliveryNotificationOptions = DeliveryNotificationOptions.OnSuccess | DeliveryNotificationOptions.OnFailure | DeliveryNotificationOptions.Delay
            };
            message.To.Add(new MailAddress(prepared.Contact));
            message.Headers.Add("Content-Language", "fr");
            message.Headers.Add("Disposition-Notification-To", _settings.SenderAddress);
            using var smtp = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(_settings.UserName, _settings.Password), Timeout = 30000
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await smtp.SendMailAsync(message, timeout.Token);
        }
    }
}
