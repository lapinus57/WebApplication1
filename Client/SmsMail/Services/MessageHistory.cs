using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EyeChat.SmsMail;

namespace Client.SmsMail.Services
{
    public sealed class MessageHistoryEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public DateTime? CreatedAtLocal { get; set; }
        public long? ProviderId { get; set; }
        public string ProviderService { get; set; } = string.Empty;

        /// <summary>Date affichée calculée sur le poste (heure et format locaux), à partir de la date UTC enregistrée.</summary>
        public static MessageHistoryEntry From(HistoryRecord record)
        {
            var date = DateTimeOffset.Parse(record.CreatedAtUtc, CultureInfo.InvariantCulture).ToLocalTime();
            return new MessageHistoryEntry
            {
                Key = record.Key, Channel = record.Channel, Contact = record.Contact, Body = record.Body, Status = record.Status,
                CreatedAtLocal = date.DateTime, CreatedAt = date.ToString("g"),
                ProviderId = record.ProviderId, ProviderService = record.ProviderService
            };
        }
    }

    /// <summary>Historique des SMS et mails : sur le serveur pour tous les postes, ou local (tests, ancien historique).</summary>
    public interface IMessageHistory
    {
        Task<bool> ReserveAsync(PreparedMessage message);
        Task UpdateAsync(string key, string status, long? providerId = null);
        Task<HashSet<string>> LoadReservedKeysAsync();
        Task<int> ApplyReportAsync(MailDeliveryReport report, string smtpHost);
        Task<DateTimeOffset?> EarliestMailAsync(string smtpHost);
        Task<int> ImportAsync(IReadOnlyList<HistoryRecord> records, IReadOnlyList<HistoryReservationKey> reservations);
        Task<List<MessageHistoryEntry>> LoadAsync();
    }

    public static class MessageHistoryExtensions
    {
        public static HistoryReservation ToReservation(this PreparedMessage message) => new()
        {
            Key = message.Key, ReservationKeys = message.ReservationKeys.ToList(), Channel = message.Channel,
            Contact = message.Contact, Body = message.Body, ProviderService = message.ProviderService
        };

        /// <summary>Importe une archive EyeEmail (lue sur ce poste) dans l'historique.</summary>
        public static async Task<int> ImportEyeEmailAsync(this IMessageHistory history, string path)
            => await history.ImportAsync(await SmsMailHistoryStore.ReadEyeEmailAsync(path), Array.Empty<HistoryReservationKey>());
    }

    /// <summary>Historique dans un fichier SQLite de ce poste (ancien fonctionnement, conservé pour les tests et la migration).</summary>
    public sealed class MessageHistory : IMessageHistory
    {
        private readonly SmsMailHistoryStore _store;
        public MessageHistory(string path) => _store = new SmsMailHistoryStore(path);

        public SmsMailHistoryStore Store => _store;
        public Task<bool> ReserveAsync(PreparedMessage message) => _store.ReserveAsync(message.ToReservation());
        public Task UpdateAsync(string key, string status, long? providerId = null) => _store.UpdateAsync(key, status, providerId);
        public async Task<HashSet<string>> LoadReservedKeysAsync() => (await _store.LoadReservedKeysAsync()).ToHashSet();
        public Task<int> ApplyReportAsync(MailDeliveryReport report, string smtpHost) => _store.ApplyReportAsync(report.MessageKey, report.Recipient, report.Status, smtpHost);
        public Task<DateTimeOffset?> EarliestMailAsync(string smtpHost) => _store.EarliestMailAsync(smtpHost);
        public Task<int> ImportAsync(IReadOnlyList<HistoryRecord> records, IReadOnlyList<HistoryReservationKey> reservations) => _store.ImportAsync(records, reservations);
        public async Task<List<MessageHistoryEntry>> LoadAsync() => (await _store.LoadAsync()).Select(MessageHistoryEntry.From).ToList();
    }
}
