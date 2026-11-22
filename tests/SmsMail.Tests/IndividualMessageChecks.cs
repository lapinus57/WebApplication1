using Client.SmsMail.Configuration;
using Client.SmsMail.Services;

// Onglet « Message individuel » : préparation du message et insertion du lien « Confirmer / Annuler ».
static class IndividualMessageChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var settings = new SmsMailSettings { Cabinet = "Cabinet Müller", CabinetAddress = "1 rue des Lilas" };
        settings.Sms.ServiceName = "sms-test"; settings.Email.SmtpHost = "smtp.test";
        var start = new DateTime(2026, 11, 23, 9, 30, 0);

        var text = IndividualMessage.DefaultText(settings.Cabinet, start, withLink: true);
        Check(text == "Cabinet Müller : votre RDV du lundi 23 novembre à 9h30. Merci de confirmer ou d'annuler : [LIEN]", "Texte par défaut avec le lien");
        Check(!IndividualMessage.DefaultText(settings.Cabinet, start, withLink: false).Contains("[LIEN]"), "Texte sans lien");
        Check(MessagePreparation.SmsMetrics(IndividualMessage.ForMetrics(text, true)).StartsWith((text.Length - 6 + IndividualMessage.EstimatedLinkLength) + " caractères"),
            "Le compteur compte la vraie longueur du lien");

        var sms = IndividualMessage.Create(settings, "SMS", " Léa MARTIN ", "06 12 34 56 78", "", start, text, "");
        Check(sms.Contact == "+33612345678" && sms.PeopleDisplay == "Léa MARTIN" && sms.ProviderService == "sms-test", "SMS : numéro normalisé, personne, compte");
        Check(sms.Key.StartsWith("individuel-") && sms.ReservationKeys.Count == 0, "Clé unique : jamais bloqué par un rappel groupé");
        Check(sms.Key != IndividualMessage.Create(settings, "SMS", "", "0612345678", "", start, text, "").Key, "Deux messages individuels ont deux clés");
        Check(IndividualMessage.WithLink(sms.Body, IndividualMessage.LinkFor("SMS", "https://rdv.x.workers.dev/Ab3dEf7h")).EndsWith("annuler : https://rdv.x.workers.dev/Ab3dEf7h"),
            "SMS : [LIEN] remplacé par le lien");
        Check(IndividualMessage.WithLink("Bonjour, merci de répondre.", "https://l") == "Bonjour, merci de répondre. https://l", "Balise effacée : lien ajouté à la fin");

        var mail = IndividualMessage.Create(settings, "Mail", "Léa", "", "lea@example.fr", start, "Bonjour <Lea>\nMerci : [LIEN]", "");
        Check(mail.Body == "<p>Bonjour &lt;Lea&gt;<br>Merci : [LIEN]</p>" && mail.Subject == "Votre rendez-vous — Cabinet Müller" && !mail.IncludeCalendar,
            "Mail : texte encodé, retours à la ligne, objet par défaut, sans invitation");
        Check(IndividualMessage.WithLink(mail.Body, IndividualMessage.LinkFor("Mail", "https://l/x?a=1&b=2")).Contains("<a href=\"https://l/x?a=1&amp;b=2\">"), "Mail : lien cliquable et encodé");

        foreach (var (channel, phone, email, body, reason) in new[]
        {
            ("SMS", "0123456789", "", "x", "fixe"), ("Mail", "", "pas-une-adresse", "x", "mail invalide"),
            ("SMS", "0612345678", "", "  ", "message vide"), ("Fax", "0612345678", "", "x", "canal inconnu")
        })
        {
            try { IndividualMessage.Create(settings, channel, "", phone, email, start, body, ""); Check(false, "Refus attendu : " + reason); }
            catch (InvalidOperationException) { count++; }
        }
        return count;
    }
}
