using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Client.SmsMail.Services
{
    /// <summary>Résultat d'un envoi auprès d'OVH ou du serveur SMTP. Accepted=false signifie « à vérifier ».</summary>
    public sealed record SendResult(string Status, long? ProviderId, bool Accepted);

    public sealed record SendProgress(int Done, int Total, PreparedMessage Current);

    public sealed record SendOutcome(int Accepted, int Skipped, int Uncertain, int NotStarted, bool Stopped);

    /// <summary>
    /// Envoie les messages un par un, hors du fil de l'interface : réservation dans l'historique, envoi, puis
    /// enregistrement du résultat. L'arrêt demandé n'interrompt jamais un message en cours (son résultat serait
    /// inconnu et il pourrait être renvoyé) : il prend effet avant le message suivant.
    /// </summary>
    public sealed class MessageSender
    {
        public const string UncertainStatus = "Résultat non confirmé — vérifier le compte OVH/SMTP avant tout nouvel essai";
        public const string AlreadyHandledStatus = "Déjà traité pour ce contact ou cette famille";

        private readonly IMessageHistory _history;
        private readonly Func<PreparedMessage, Task<SendResult>> _send;

        public MessageSender(IMessageHistory history, Func<PreparedMessage, Task<SendResult>> send)
        {
            _history = history;
            _send = send;
        }

        public async Task<SendOutcome> SendAsync(IReadOnlyList<PreparedMessage> messages, IProgress<SendProgress>? progress, CancellationToken stop)
        {
            int accepted = 0, skipped = 0, uncertain = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (stop.IsCancellationRequested)
                    return new SendOutcome(accepted, skipped, uncertain, messages.Count - i, true);
                var message = messages[i];
                progress?.Report(new SendProgress(i, messages.Count, message));

                if (!await _history.ReserveAsync(message).ConfigureAwait(false))
                {
                    skipped++;
                    message.Selected = false; message.CanSend = false; message.Status = AlreadyHandledStatus;
                    continue;
                }
                SendResult result;
                try { result = await _send(message).ConfigureAwait(false); }
                catch (Exception) { result = new SendResult(UncertainStatus, null, false); }
                if (result.Accepted) accepted++; else uncertain++;

                // Enregistrement séparé de l'envoi : un échec d'écriture ne doit jamais
                // provoquer un second envoi d'un message déjà accepté.
                await _history.UpdateAsync(message.Key, result.Status, result.ProviderId).ConfigureAwait(false);
                message.CanSend = false;
                message.Status = result.Status;
                message.Selected = false;
            }
            return new SendOutcome(accepted, skipped, uncertain, 0, false);
        }
    }
}
