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
        // Texte automatique : suit la date, l'heure et la case tant qu'il n'a pas été modifié à la main.
        var auto = IndividualMessage.AutoText("", "", "Cabinet Müller", start, withLink: true);
        Check(auto == text, "Date et heure choisies : texte rédigé avec le cabinet et le lien");
        var later = IndividualMessage.AutoText(auto!, auto!, "Cabinet Müller", start.AddHours(2), withLink: true);
        Check(later != null && later.Contains("11h30") && later.EndsWith("[LIEN]"), "Nouvelle heure : texte mis à jour");
        Check(IndividualMessage.AutoText(auto!, auto!, "Cabinet Müller", start, withLink: false) == IndividualMessage.DefaultText("Cabinet Müller", start, false), "Case décochée : texte sans lien");
        Check(IndividualMessage.AutoText(auto + " Apportez vos lunettes.", auto!, "Cabinet Müller", start.AddDays(1), true) == null, "Texte modifié à la main : jamais remplacé");
        Check(IndividualMessage.AutoText("", "", "Cabinet Müller", null, true) == null, "Sans date ni heure : rien n'est rédigé");

        // Case « Ajouter le lien » : le texte montre toujours l'emplacement du lien.
        Check(IndividualMessage.ToggleLink("", true, "Cabinet Müller", start) == text, "Zone vide + case cochée : texte par défaut avec [LIEN]");
        Check(IndividualMessage.ToggleLink("Bonjour Léa.", true, "C", start) == "Bonjour Léa. Merci de confirmer ou d'annuler : [LIEN]", "Texte saisi : [LIEN] ajouté à la fin");
        Check(IndividualMessage.ToggleLink("Cliquez : [LIEN] merci", true, "C", start) == "Cliquez : [LIEN] merci", "[LIEN] déjà placé : texte inchangé");
        Check(IndividualMessage.ToggleLink(text, false, "C", start) == "Cabinet Müller : votre RDV du lundi 23 novembre à 9h30.", "Case décochée : lien et phrase retirés");
        Check(!IndividualMessage.HasTextBesidesLink("[LIEN]") && !IndividualMessage.HasTextBesidesLink("  : [LIEN] ") && IndividualMessage.HasTextBesidesLink(text), "Un lien seul est refusé");

        // Réponse au lien, affichée à part du statut d'envoi dans l'historique.
        var now = new DateTime(2026, 11, 23, 8, 0, 0);
        Check(MessageHistoryEntry.ReplyText("", "", "", now) == "", "Pas de lien : rien d'affiché");
        Check(MessageHistoryEntry.ReplyText("Attente", "", "2026-11-23T09:30:00", now).Contains("en attente de réponse"), "Lien envoyé, pas encore de réponse");
        Check(MessageHistoryEntry.ReplyText("Attente", "", "2026-11-23T09:30:00", now.AddHours(5)).Contains("pas de réponse"), "Lien expiré sans réponse");
        var answered = new DateTimeOffset(2026, 11, 22, 18, 10, 0, TimeSpan.Zero);
        var cancelText = MessageHistoryEntry.ReplyText("Annule", answered.ToString("O"), "2026-11-23T09:30:00", now);
        Check(cancelText.StartsWith("❌ Annulé par le patient le ") && cancelText.Contains(answered.ToLocalTime().ToString("HH:mm")), "Annulation avec date et heure locales");
        Check(MessageHistoryEntry.ReplyText("Confirme", answered.ToString("O"), "", now).StartsWith("✅ Confirmé par le patient"), "Confirmation");
        var entries = new[] { new MessageHistoryEntry { Contact = "+33612345678", Status = "Reçu", Reply = cancelText, CreatedAt = "", CreatedAtLocal = now } };
        Check(MessageExportReport.FilterHistory(entries, null, "", "annulé").Count == 1, "Recherche « annulé » dans l'historique");
        Check(MessageExportReport.HistorySheet(entries, now.Date).Rows.Single()[4] == cancelText, "Colonne « Réponse du patient » dans l'export Excel");
        return count;
    }
}
