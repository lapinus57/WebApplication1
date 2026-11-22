using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using Client.SmsMail.Configuration;

namespace Client.SmsMail.Services
{
    /// <summary>
    /// Message à une seule personne (onglet « Message individuel »), avec en option le lien
    /// « Confirmer / Annuler ». N'entre pas dans les rappels groupés : sa clé est unique, il n'est
    /// donc jamais bloqué par un rappel déjà envoyé, mais il figure dans l'historique commun.
    /// </summary>
    public static class IndividualMessage
    {
        public const string LinkTag = "[LIEN]";
        /// <summary>Longueur d'un lien https://rdv.xxx.workers.dev/XXXXXXXX, pour estimer la taille du SMS avant l'envoi.</summary>
        public const int EstimatedLinkLength = 42;

        public static string DefaultText(string cabinet, DateTime start, bool withLink)
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            var text = $"{(string.IsNullOrWhiteSpace(cabinet) ? "Cabinet" : cabinet.Trim())} : votre RDV du {start.ToString("dddd d MMMM", culture)} à {start.ToString("H'h'mm", culture)}.";
            return withLink ? text + " Merci de confirmer ou d'annuler : " + LinkTag : text;
        }

        /// <summary>Texte tel qu'il partira, avec un lien fictif de la bonne longueur (compteur de caractères).</summary>
        public static string ForMetrics(string text, bool withLink)
            => withLink ? WithLink(text, new string('x', EstimatedLinkLength)) : text;

        /// <summary>Place le lien à l'emplacement de [LIEN], ou à la fin du texte si la balise a été effacée.</summary>
        public static string WithLink(string text, string url)
        {
            text = (text ?? string.Empty).TrimEnd();
            return text.Contains(LinkTag, StringComparison.OrdinalIgnoreCase)
                ? text.Replace(LinkTag, url, StringComparison.OrdinalIgnoreCase)
                : (text.Length == 0 ? url : text + " " + url);
        }

        public static PreparedMessage Create(SmsMailSettings settings, string channel, string person, string phone, string email,
            DateTime appointmentStart, string text, string subject)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) throw new InvalidOperationException("Le message est vide.");
            string contact;
            if (channel == "SMS")
            {
                contact = MessagePreparation.NormalizePhone(phone ?? string.Empty);
                if (contact.Length == 0) throw new InvalidOperationException("Numéro mobile invalide : utilisez 06/07 ou un numéro international +/00.");
            }
            else if (channel == "Mail")
            {
                contact = (email ?? string.Empty).Trim();
                if (!MailAddress.TryCreate(contact, out var address) || !string.Equals(address.Address, contact, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Adresse mail invalide.");
            }
            else throw new InvalidOperationException("Choisissez SMS ou mail.");

            person = (person ?? string.Empty).Trim();
            return new PreparedMessage
            {
                Channel = channel, Contact = contact, Family = person, PeopleDisplay = person,
                AppointmentStart = appointmentStart, CalendarStart = appointmentStart,
                Key = "individuel-" + Guid.NewGuid().ToString("N"), ReservationKeys = new List<string>(),
                Subject = string.IsNullOrWhiteSpace(subject) ? (string.IsNullOrWhiteSpace(settings.Cabinet) ? "Votre rendez-vous" : "Votre rendez-vous — " + settings.Cabinet.Trim()) : subject.Trim(),
                // Le texte est saisi en clair : en mail, il est encodé et ses retours à la ligne conservés.
                Body = channel == "Mail" ? ToHtml(text) : text,
                CabinetDisplay = settings.Cabinet, AddressDisplay = settings.CabinetAddress,
                ProviderService = channel == "SMS" ? settings.Sms.ServiceName : settings.Email.SmtpHost,
                IncludeCalendar = false, CalendarAlarms = new(), Status = "À envoyer"
            };
        }

        public static string ToHtml(string text)
            => "<p>" + string.Join("<br>", WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Split('\n')) + "</p>";

        /// <summary>Lien à insérer : brut en SMS, cliquable en mail (le texte du mail est déjà encodé).</summary>
        public static string LinkFor(string channel, string url)
            => channel == "Mail" ? $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a>" : url;
    }
}
