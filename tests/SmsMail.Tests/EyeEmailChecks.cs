using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using System.Text.Json;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using Client.SmsMail.Services.Grouping;
using Microsoft.Data.Sqlite;
using MimeKit;

static class EyeEmailChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("EyeEmail: " + message); checks++; }
        var day = new DateTime(2026, 10, 23);
        RdvInfo Person(int id, string first, string last, int hour = 9) => new()
        { Id = id, Prenom = first, Nom = last, DateRDV = day, HeureRDV = day.AddHours(hour), DatePriseRDV = day.AddDays(-id).AddHours(8) };

        var table = new DataTable();
        foreach (var name in new[] { "CodePatient", "Nom", "Prenom", "Date", "Heure", "Tel", "Email", "PatientEmail", "E-mail 2", "Remarque", "Annule" }) table.Columns.Add(name, typeof(object));
        table.Rows.Add(41, "Dupont", "Alice", day, day.AddHours(9), "06 12 34 56 78 (maman)", "", "alice@example.com", "copie@example.com", "imprimé à 11h15", false);
        table.Rows.Add(42, "Annulé", "Patient", day, day.AddHours(9), "", "", "", "", "", true);
        using (var reader = table.CreateDataReader())
        {
            var columns = EyeEmailRecordMapper.BuildOrdinalLookup(reader);
            reader.Read(); var row = EyeEmailRecordMapper.MapAppointment(reader, columns, day)!;
            Check(row.Id == 41 && row.Prenom == "Alice", "column aliases");
            Check(row.Email == "alice@example.com" && row.AdditionalEmails.Contains("copie@example.com"), "empty Email falls back to PatientEmail, copies retained");
            Check(row.Tel1Info == "maman" && !row.Tel1.Contains('('), "phone notes separated");
            Check(row.HeureRDV.TimeOfDay == new TimeSpan(11, 15, 0), "printed appointment time from remark");
            reader.Read(); Check(EyeEmailRecordMapper.MapAppointment(reader, columns, day) == null, "explicit cancellation excluded");
        }
        var first = Person(1, "Alice", "Dupont"); first.Email = "alice@example.com";
        var duplicate = Person(1, "Alice", "Dupont", 10); duplicate.Email = "copie@example.com"; duplicate.SMSSendTel1 = true;
        var merged = EyeEmailRecordMapper.ConsolidateDuplicateAppointments(new() { duplicate, first });
        Check(merged.Count == 1 && merged[0].HeureRDV.Hour == 9 && merged[0].AdditionalEmails.Contains("copie@example.com") && merged[0].SMSSendTel1, "same patient/day consolidation keeps contacts and flags");
        Check(EyeEmailRecordMapper.ConsolidateDuplicateAppointments(new() { Person(0, "A", "A"), Person(0, "B", "B") }).Count == 2, "anonymous imports remain distinct");

        var a = Person(1, "Alice", "Dupont"); a.Email = "parent@example.com"; a.Adresse = "12 rue des Fleurs"; a.Ville = "Metz";
        var b = Person(2, "Bob", "Martin", 10); b.Tel1 = "0612345678"; b.Adresse = a.Adresse; b.Ville = a.Ville;
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = true };
        var families = EyeEmailFamilyGrouping.Build(new[] { a, b });
        Check(families.Count == 1 && families[0].RDVs.Count == 2, "same address/city family pass");
        var prepared = MessagePreparation.Prepare(new[] { a, b }, day, settings);
        Check(prepared.Count == 1 && prepared[0].Channel == "Mail" && prepared[0].Family.Contains("Bob"), "SMS fallback is evaluated for the whole family");
        settings.SmsOnlyWhenNoEmail = false;
        Check(MessagePreparation.Prepare(new[] { a, b }, day, settings).Count == 2, "both channels contain full family");
        var c = Person(3, "Camille", "Durand"); c.Remarque = "avec Jean Martin";
        var d = Person(4, "Jean", "Martin");
        Check(EyeEmailFamilyGrouping.Build(new[] { c, d }).Count == 1, "avec remark grouping");
        var e = Person(5, "Camille", "Müller"); var f = Person(6, "Jules", "Muller"); f.DatePriseRDV = e.DatePriseRDV.AddMinutes(8);
        Check(EyeEmailFamilyGrouping.Build(new[] { e, f }).Count == 1, "name and creation proximity with accents");
        Check(EyeEmailFamilyGrouping.Build(new[] { Person(11, "A", "A"), Person(12, "B", "B") }).Count == 2, "empty addresses do not merge unrelated people");
        a.ReferenceFamilyNumber = 10; b.ReferenceFamilyNumber = 20;
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "explicit distinct EyeEmail memberships take precedence over heuristics");
        a.ReferenceFamilyNumber = 10; b.ReferenceFamilyNumber = 10;
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Single().RDVs.Count == 2, "known EyeEmail memberships");
        var h1 = Person(51, "A", "A"); var h2 = Person(52, "B", "B");
        h2.HeureRDV = h1.HeureRDV.AddMinutes(15); h2.DatePriseRDV = h1.DatePriseRDV.AddMinutes(1);
        var history = new Dictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>>
        {
            [51] = new() { (day.AddYears(-1), day.AddHours(9), day.AddHours(8)) },
            [52] = new() { (day.AddYears(-1), day.AddHours(9).AddMinutes(15), day.AddHours(8).AddMinutes(5)) }
        };
        var histFamilies = new List<RdvFamille>(); int number = 1;
        GroupingByDate.Regrouper(new(new[] { h1, h2 }), histFamilies, ref number, history, (_, _) => false);
        Check(histFamilies.Count == 0, "historical grouping cannot happen without user approval");
        GroupingByDate.Regrouper(new(new[] { h1, h2 }), histFamilies, ref number, history, (_, _) => true);
        Check(histFamilies.Count == 1 && histFamilies[0].RDVs.Count == 2, "confirmed historical grouping");
        var rendered = MessagePreparation.Render("[date] [datejourL] [DatejourC] [Cabinet]", new[] { h1 }, "Cabinet", true);
        Check(rendered.Contains("vendredi 23 octobre 2026") && rendered.Contains("23/10/2026") && !rendered.Contains('['), "EyeEmail aliases and case-insensitive tokens");
        Check(JsonSerializer.Deserialize<SmsOutgoingResponse>("{\"id\":5000000000}")!.Id == 5000000000L, "64-bit outgoing IDs");

        var mailSettings = new EmailSettings { FromAddress = "cabinet@example.com", SenderAddress = "cabinet@example.com", SmtpHost = "smtp.example.com" };
        settings = new SmsMailSettings { SendCalendarInvites = false };
        var mailPerson = Person(80, "Alice", "Dupont"); mailPerson.Email = "alice@example.com";
        var message = MessagePreparation.Prepare(new[] { mailPerson }, day, settings).Single();
        using (var mail = new MailService(mailSettings).CreateMessage(message)) Check(mail.Attachments.Count == 0, "ICS can be disabled");
        settings.SendCalendarInvites = true; settings.CalendarIsBusy = false;
        settings.Alerts.Add(new AlertConfiguration { Label = "La veille", OffsetMinutes = 1440 });
        message = MessagePreparation.Prepare(new[] { mailPerson }, day, settings).Single();
        var calendar = CalendarInvitation.Create(message, mailSettings.FromAddress);
        Check(calendar.Contains("METHOD:PUBLISH") && calendar.Contains("TRANSP:TRANSPARENT"), "EyeEmail calendar publication and busy option");
        Check(calendar.Contains("BEGIN:VALARM") && calendar.Contains("TRIGGER:-PT1440M") && calendar.Contains("La veille"), "calendar alarms");
        settings.SendCalendarAlarms = false;
        Check(!CalendarInvitation.Create(MessagePreparation.Prepare(new[] { mailPerson }, day, settings).Single(), mailSettings.FromAddress).Contains("VALARM"), "calendar alarms can be disabled independently");
        var signature = MessagePreparation.PreparationSignature(settings); settings.Sms.ServiceName = "new-account";
        Check(signature != MessagePreparation.PreparationSignature(settings), "provider change invalidates prepared messages");
        signature = MessagePreparation.PreparationSignature(settings); settings.Alerts[0].OffsetMinutes = 60;
        Check(signature != MessagePreparation.PreparationSignature(settings), "alarm change invalidates prepared messages");

        var mime = new MimeMessage(); mime.From.Add(MailboxAddress.Parse("mailer-daemon@example.com"));
        var returned = new MimeMessage(); returned.MessageId = message.Key + "@eyechat.local"; returned.Body = new TextPart("plain") { Text = "original" };
        var status = new MessageDeliveryStatus();
        status.StatusGroups.Add(new HeaderList { new Header("Reporting-MTA", "dns; example.com") });
        status.StatusGroups.Add(new HeaderList { new Header("Final-Recipient", "rfc822; alice@example.com"), new Header("Action", "failed"), new Header("Status", "5.1.1") });
        mime.Body = new Multipart("report") { new TextPart("plain") { Text = "Delivery failed" }, status, new MessagePart("rfc822") { Message = returned } };
        var report = MailDeliveryReports.Parse(mime).Single();
        Check(report.MessageKey == message.Key && report.Recipient == "alice@example.com" && report.Status.Contains("Rejeté"), "DSN matched by original Message-ID and recipient");
        var unrelated = new MimeMessage { Body = new TextPart("plain") { Text = "alice@example.com failed" } };
        Check(MailDeliveryReports.Parse(unrelated).Count == 0, "unmatched bounce never changes a patient's history");

        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        var targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            using (var source = new SqliteConnection("Data Source=" + sourcePath))
            {
                source.Open(); using var cmd = source.CreateCommand();
                cmd.CommandText = "CREATE TABLE MailLog (Id INTEGER, ToAddress TEXT, Body TEXT, Status TEXT, SentAt TEXT); INSERT INTO MailLog VALUES (1,'archive@example.com','archive','Envoyé','2025-10-23 10:00:00');";
                cmd.ExecuteNonQuery();
            }
            var log = new MessageHistory(targetPath);
            Check(await log.ImportEyeEmailAsync(sourcePath) == 1 && await log.ImportEyeEmailAsync(sourcePath) == 0, "EyeEmail history import is idempotent");
            Check((await log.LoadAsync()).Single().Status.Contains("[EyeEmail]"), "imported history clearly labelled");
            message.ProviderService = mailSettings.SmtpHost; await log.ReserveAsync(message);
            Check(await log.ApplyReportAsync(report, "other-smtp.example.com") == 0, "DSN cannot update another account");
            Check(await log.ApplyReportAsync(report with { Recipient = "other@example.com" }, mailSettings.SmtpHost) == 0, "DSN cannot update another recipient");
            Check(await log.ApplyReportAsync(report, mailSettings.SmtpHost) == 1, "matching DSN updates history");
            Check(await log.EarliestMailAsync(mailSettings.SmtpHost) != null, "mailbox scans start at relevant local history");
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(sourcePath); File.Delete(targetPath); }
        return checks;
    }
}
