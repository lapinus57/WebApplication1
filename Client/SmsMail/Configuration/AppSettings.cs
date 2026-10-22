using System.Collections.Generic;

namespace Client.SmsMail.Configuration
{
    public sealed class AppSettings
    {
        public DatabaseSettings Database { get; set; } = new();
        public SmsSettings Sms { get; set; } = new();
        public EmailSettings Email { get; set; } = new();
    }

    public sealed class DatabaseSettings
    {
        public string? ConnectionString { get; set; }
    }

    public sealed class SmsSettings
    {
        public string ApplicationKey { get; set; } = string.Empty;
        public string ApplicationSecret { get; set; } = string.Empty;
        public string ConsumerKey { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string BalanceEndpoint { get; set; } = "https://eu.api.ovh.com/1.0/sms";
        public string TimeEndpoint { get; set; } = "https://eu.api.ovh.com/1.0/auth/time";
    }

    public sealed class EmailSettings
    {
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromDisplayName { get; set; } = string.Empty;
        public string SenderAddress { get; set; } = string.Empty;
        public string ImapHost { get; set; } = string.Empty;
        public int ImapPort { get; set; } = 993;
        public string BounceFilter { get; set; } = "mailer-daemon@googlemail.com";
    }
}

