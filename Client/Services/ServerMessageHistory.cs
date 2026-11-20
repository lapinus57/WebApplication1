using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.SmsMail.Services;
using EyeChat.SmsMail;
using Microsoft.AspNetCore.SignalR.Client;

namespace Client.Services
{
    /// <summary>
    /// Historique des SMS et mails stocké sur le serveur (data\sms-mail-history.db) : visible depuis tous les postes,
    /// et la réservation anti-doublon est commune (un rappel envoyé depuis un poste ne peut pas repartir d'un autre).
    /// Sans connexion au serveur, chaque appel échoue avec un message explicite : aucun envoi ne part sans réservation.
    /// </summary>
    public sealed class ServerMessageHistory : IMessageHistory
    {
        // Le hub accepte des messages de 2 Mo. En JSON, le HTML d'un mail grossit beaucoup (« < » devient « < ») :
        // les imports partent donc par lots estimés à 1 Mo au plus.
        private const int MaxBatchBytes = 1_000_000;

        private readonly Func<HubConnection> _connection;
        public ServerMessageHistory(Func<HubConnection> connection) => _connection = connection;

        public Task<bool> ReserveAsync(PreparedMessage message)
            => _connection().InvokeAsync<bool>("SmsMailReserve", message.ToReservation());

        public Task UpdateAsync(string key, string status, long? providerId = null)
            => _connection().InvokeAsync("SmsMailUpdate", key, status, providerId);

        public async Task<HashSet<string>> LoadReservedKeysAsync()
            => (await _connection().InvokeAsync<List<string>>("SmsMailReservedKeys")).ToHashSet();

        public Task<int> ApplyReportAsync(MailDeliveryReport report, string smtpHost)
            => _connection().InvokeAsync<int>("SmsMailApplyReport", report.MessageKey, report.Recipient, report.Status, smtpHost);

        public Task<DateTimeOffset?> EarliestMailAsync(string smtpHost)
            => _connection().InvokeAsync<DateTimeOffset?>("SmsMailEarliestMail", smtpHost);

        public async Task<int> ImportAsync(IReadOnlyList<HistoryRecord> records, IReadOnlyList<HistoryReservationKey> reservations)
        {
            int imported = 0;
            foreach (var batch in Batches(records, r => 6 * (r.Body.Length + r.Contact.Length + r.Status.Length) + 400))
                imported += await _connection().InvokeAsync<int>("SmsMailImport", batch, new List<HistoryReservationKey>());
            foreach (var batch in Batches(reservations, r => 2 * (r.ReservationKey.Length + r.MessageKey.Length) + 60))
                await _connection().InvokeAsync<int>("SmsMailImport", new List<HistoryRecord>(), batch);
            return imported;
        }

        public async Task<List<MessageHistoryEntry>> LoadAsync()
            => (await _connection().InvokeAsync<List<HistoryRecord>>("SmsMailLoad")).Select(MessageHistoryEntry.From).ToList();

        public static IEnumerable<List<T>> Batches<T>(IEnumerable<T> items, Func<T, int> estimatedBytes)
        {
            var batch = new List<T>();
            long size = 0;
            foreach (var item in items)
            {
                var itemSize = estimatedBytes(item);
                if (batch.Count > 0 && size + itemSize > MaxBatchBytes)
                {
                    yield return batch;
                    batch = new List<T>();
                    size = 0;
                }
                batch.Add(item);
                size += itemSize;
            }
            if (batch.Count > 0) yield return batch;
        }
    }
}
