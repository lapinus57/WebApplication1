using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using EyeChat.SmsMail;

namespace ChatServeur.SmsMail;

/// <summary>Lecture de l'agenda pour l'onglet SMS et mail : le serveur lit Access (lecture seule) avec ses propres réglages.</summary>
public interface ISmsMailAgenda
{
    Task<List<RdvInfo>> DayAsync(DateTime day);
    Task<List<RdvInfo>> RangeAsync(DateTime from, DateTime to);
    Task<List<AgendaVisit>> HistoryAsync(DateTime today);
    Task<SmsMailPostIts> PostItsAsync(DateTime from, DateTime to);
}

/// <summary>
/// Envoi des SMS (OVH) et des mails (SMTP) par le serveur, avec les réglages du serveur. Un message n'est envoyé que s'il a été
/// réservé dans l'historique commun et n'a encore aucun résultat : un poste ne peut ni renvoyer un message, ni en envoyer un
/// sans trace. Le résultat est écrit dans l'historique par le serveur lui-même.
/// </summary>
public sealed class SmsMailDelivery(SmsMailServerSettings settings, SmsMailHistoryStore history, ILogger<SmsMailDelivery> logger)
{
    public const string NotConfigured = "Réglages SMS et mail absents du serveur : enregistrez-les depuis l'onglet SMS et mail d'un poste.";

    private SmsMailSettings Settings() => settings.Load() ?? throw new InvalidOperationException(NotConfigured);

    /// <summary>Vérifie les réglages et fabrique les mails avant un envoi (même contrôle qu'avant, côté serveur).</summary>
    public void Validate(IReadOnlyList<PreparedMessage> messages)
    {
        var s = Settings();
        if (messages.Any(m => m.Channel == "SMS")) new SmsService(s.Sms).Validate();
        if (messages.Any(m => m.Channel == "Mail"))
        {
            var mail = new MailService(s.Email);
            mail.Validate();
            foreach (var m in messages.Where(m => m.Channel == "Mail")) using (mail.CreateMessage(m)) { }
        }
    }

    /// <summary>Le service et l'hôte utilisés pour l'historique (clés de réservation), sans secret.</summary>
    public (string SmsService, string SmtpHost) Services()
    {
        var s = Settings();
        return (s.Sms.ServiceName, s.Email.SmtpHost);
    }

    public async Task<SmsMailSendResult> SendAsync(PreparedMessage message, bool test)
    {
        var record = await history.GetAsync(message.Key);
        if (record == null || record.Status != SmsMailHistoryStore.ReservedStatus
            || !string.Equals(record.Contact, message.Contact, StringComparison.OrdinalIgnoreCase) || record.Channel != message.Channel)
            return new SmsMailSendResult { Status = MessageSender.AlreadyHandledStatus };
        var s = Settings();
        var prefix = test ? "[TEST] " : "";
        SmsMailSendResult result;
        try
        {
            if (message.Channel == "SMS")
            {
                var response = await new SmsService(s.Sms).SendSmsAsync(message.Contact, message.Body);
                result = response.Ids.Length > 0 && response.ValidReceivers.Length > 0
                    ? new SmsMailSendResult { Status = prefix + "Accepté par OVH — réception en attente", ProviderId = response.Ids[0], Accepted = true }
                    : new SmsMailSendResult { Status = prefix + (test ? "Non accepté par OVH — vérifier le compte" : "Non accepté par OVH — vérifier le compte avant tout nouvel essai") };
            }
            else
            {
                await new MailService(s.Email).SendAsync(message);
                result = new SmsMailSendResult { Status = prefix + "Accepté par SMTP — réception non confirmée", Accepted = true };
            }
        }
        catch (Exception ex)
        {
            // Le message d'erreur d'OVH ou du serveur mail ne contient pas de données patient ; il aide à régler le compte.
            logger.LogWarning("SER31: Envoi {Channel} non confirmé ({Type}) : {Message}", message.Channel, ex.GetType().Name, ex.Message);
            result = new SmsMailSendResult { Status = test ? "[TEST] Résultat non confirmé — vérifier le compte avant un nouvel essai. " + ex.Message : MessageSender.UncertainStatus };
        }
        await history.UpdateAsync(message.Key, result.Status, result.ProviderId);
        return result;
    }

    public async Task<decimal> BalanceAsync() => await new SmsService(Settings().Sms).GetSmsBalanceAsync();

    /// <summary>Statuts OVH des SMS en attente : (actualisés, indisponibles).</summary>
    public async Task<(int Updated, int Errors)> RefreshSmsStatusesAsync()
    {
        var s = Settings();
        var pending = (await history.LoadAsync()).Where(h => h.ProviderId.HasValue && h.ProviderService == s.Sms.ServiceName
            && !h.Status.StartsWith("Reçu", StringComparison.Ordinal)).ToList();
        if (pending.Count == 0) return (0, 0);
        var sms = new SmsService(s.Sms);
        int updated = 0, errors = 0;
        foreach (var entry in pending)
        {
            try
            {
                var detail = await sms.GetSmsOutgoingAsync(entry.ProviderId!.Value);
                var status = detail.DeliveredAt.HasValue || detail.DeliveryReceipt == 1
                    ? "Reçu — " + (detail.DeliveredAt?.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "confirmation OVH")
                    : "OVH : " + (detail.Status ?? "réception en attente");
                await history.UpdateAsync(entry.Key, status);
                updated++;
            }
            catch { errors++; }
        }
        return (updated, errors);
    }

    /// <summary>Retours mail (IMAP, lecture seule) rapprochés de l'historique. -1 si aucun mail n'a été envoyé depuis ce compte.</summary>
    public async Task<int> ApplyMailReportsAsync()
    {
        var s = Settings();
        var since = await history.EarliestMailAsync(s.Email.SmtpHost);
        if (!since.HasValue) return -1;
        int matched = 0;
        foreach (var report in await MailDeliveryReports.ReadAsync(s.Email, since.Value))
            matched += await history.ApplyReportAsync(report.MessageKey, report.Recipient, report.Status, s.Email.SmtpHost);
        return matched;
    }
}
