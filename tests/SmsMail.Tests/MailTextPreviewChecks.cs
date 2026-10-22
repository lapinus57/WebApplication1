using Client.SmsMail.Configuration;
using Client.SmsMail.Services;

static class MailTextPreviewChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var source = "<html><head><style>p{color:red}</style><title>Titre technique</title></head><body><p>Dr Ren&#233; MULLER &amp; son cabinet</p><p>Date : 23 octobre<br>Heure : 11:20</p><ul><li>Alice</li><li>Bob</li></ul><script>alert('masqué')</script><!-- commentaire --></body></html>";
        var text = MailTextPreview.Convert(source);
        Check(text.Contains("René MULLER & son cabinet"), "HTML entities become readable accents and symbols");
        Check(text.Contains("\n\nDate :") && text.Contains("octobre\nHeure :"), "Paragraphs and line breaks are preserved");
        Check(text.Contains("• Alice\n• Bob"), "List items remain separate readable lines");
        Check(!text.Contains('<') && !text.Contains("color:red") && !text.Contains("masqué") && !text.Contains("commentaire") && !text.Contains("Titre technique"), "Markup, head, CSS, scripts and comments are omitted");
        Check(MailTextPreview.Convert("<p title='a > b'>Bonjour <strong>Alice</strong></p>") == "Bonjour Alice", "Quoted tag attributes do not leak into text");
        Check(MailTextPreview.Convert("<p>Bonjour<br>Camille") == "Bonjour\nCamille", "HTML fragments and missing closing tags are supported");
        Check(MailTextPreview.Convert(null) == "" && MailTextPreview.Convert("") == "", "Empty previews are supported");
        Check(MailTextPreview.Convert("<p>Arrivée &lt; 11:30</p>") == "Arrivée < 11:30", "Literal encoded comparison is not mistaken for a tag");
        var settings = new SmsMailSettings();
        var mail = TestMessagePreparation.Create(settings, "test@example.com", "", new DateTime(2026,10,22)).Single();
        var body = mail.Body;
        var rendered = MailTextPreview.Convert(body);
        Check(rendered.Contains("MESSAGE DE TEST") && rendered.Contains("Rappel de Rendez-vous") && rendered.Contains("10:00") && rendered.Contains("Utilisateur TEST"), "Actual cabinet template stays complete and readable");
        Check(!rendered.Contains("<table") && !rendered.Contains("style="), "Layout table and styles disappear");
        Check(mail.Body == body && mail.Body.Contains("<table"), "Preview conversion never replaces the outgoing HTML body");
        return count;
    }
}
