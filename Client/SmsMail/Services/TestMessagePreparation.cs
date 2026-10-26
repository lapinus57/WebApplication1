using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public static class TestMessagePreparation
    {
        public static List<PreparedMessage> Create(SmsMailSettings settings, string email, string phone, DateTime referenceDay)
        {
            email = email.Trim();
            phone = phone.Trim();
            if (email.Length == 0 && phone.Length == 0)
                throw new InvalidOperationException("Saisissez une adresse mail, un numéro mobile, ou les deux.");
            if (email.Length > 0 && (!MailAddress.TryCreate(email, out var address)
                || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Saisissez une seule adresse mail valide.");
            var normalizedPhone = MessagePreparation.NormalizePhone(phone);
            if (phone.Length > 0 && normalizedPhone.Length == 0)
                throw new InvalidOperationException("Numéro mobile invalide : utilisez 06/07 ou un numéro international +/00.");
            if (email.Length > 0 && string.IsNullOrWhiteSpace(settings.EmailTemplate))
                throw new InvalidOperationException("Le modèle mail est vide.");
            if (phone.Length > 0 && string.IsNullOrWhiteSpace(settings.SmsTemplate))
                throw new InvalidOperationException("Le modèle SMS est vide.");

            var day = referenceDay.Date.AddDays(1);
            var examples = new List<RdvInfo>();
            // Separate examples ensure that both channels are tested even when
            // the production fallback rule suppresses SMS for mail recipients.
            if (email.Length > 0) examples.Add(Example(email, string.Empty));
            if (phone.Length > 0) examples.Add(Example(string.Empty, normalizedPhone));
            // A test explicitly targets the entered contact, regardless of family-size filters.
            var testSettings = JsonSerializer.Deserialize<SmsMailSettings>(JsonSerializer.Serialize(settings))!;
            testSettings.SmsMinimumFamilySize = 1;
            var messages = examples.SelectMany(example => MessagePreparation.Prepare(new[] { example }, day, testSettings)).ToList();
            foreach (var message in messages)
            {
                message.Key = "test-" + Guid.NewGuid().ToString("N");
                message.ReservationKeys.Clear();
                if (message.Channel == "Mail")
                {
                    var trace = EyeChat.Messaging.MailTrace.Parse(message.TraceJson);
                    trace.IsTest = true;
                    foreach (var person in trace.People) person.DestinationReason = "Adresse saisie explicitement dans le formulaire de test ; patient fictif.";
                    message.TraceJson = JsonSerializer.Serialize(trace);
                }
                message.Subject = "[TEST] " + settings.Subject;
                message.CalendarSubject = "[TEST] " + settings.CalendarSubject;
                message.Body = message.Channel == "Mail"
                    ? "<p><strong>MESSAGE DE TEST — rendez-vous fictif, aucune réservation réelle.</strong></p>" + message.Body
                    : "[TEST - RDV fictif] " + message.Body;
                message.CalendarDescription = "MESSAGE DE TEST — rendez-vous fictif, aucune réservation réelle.";
                message.Status = "Test à envoyer";
            }
            return messages;

            RdvInfo Example(string mail, string mobile) => new()
            {
                Nom = "TEST", Prenom = "Utilisateur", DateRDV = day, HeureRDV = day.AddHours(10),
                Email = mail, Tel1 = mobile
            };
        }
    }
}

