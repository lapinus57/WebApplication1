using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using System.Text.Json;

static class ReminderTimeChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var day = new DateTime(2026, 10, 23);
        var rows = new[]
        {
            new RdvInfo { Id = 501, ReferenceFamilyNumber = 88, Nom = "Test", Prenom = "Alice", DateRDV = day, HeureRDV = day.AddHours(11.5), Tel1 = "0612345678", Email = "alice@example.com" },
            new RdvInfo { Id = 502, ReferenceFamilyNumber = 88, Nom = "Test", Prenom = "Bob", DateRDV = day, HeureRDV = day.AddHours(18), Tel1 = "0612345678", Email = "alice@example.com" }
        };
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        var before = MessagePreparation.Prepare(rows, day, settings);
        Check(before.Single(m => m.Channel == "SMS").Body.Contains("11:30"), "Time replacements are disabled by default");
        var oldSignature = MessagePreparation.PreparationSignature(settings);
        settings.AdjustReminderTimes = true;
        var after = MessagePreparation.Prepare(rows, day, settings);
        var mail = after.Single(m => m.Channel == "Mail");
        Check(mail.Body.Contains("11:20") && mail.Body.Contains("17:50") && !mail.Body.Contains("11:30"), "HTML individual times are replaced");
        Check(after.Single(m => m.Channel == "SMS").Body.Contains("11:20"), "SMS uses same adjusted start");
        Check(mail.CalendarStart == day.AddHours(11).AddMinutes(20) && mail.CalendarEnd == day.AddHours(18).AddMinutes(20), "ICS range and duration use adjusted times");
        Check(CalendarInvitation.Create(mail, "cabinet@example.com").Contains("DTSTART:20261023T092000Z"), "ICS retains Paris timezone conversion");
        Check(rows[0].HeureRDV == day.AddHours(11.5) && rows[1].HeureRDV == day.AddHours(18), "Original appointments are not mutated");
        Check(after.Select(m => m.Key).SequenceEqual(before.Select(m => m.Key)), "Time changes cannot bypass contact reservation");
        Check(after.Single(m => m.Channel == "SMS").ReservationKeys.SequenceEqual(before.Single(m => m.Channel == "SMS").ReservationKeys), "Family reservation survives time changes");
        Check(oldSignature != MessagePreparation.PreparationSignature(settings), "Toggle invalidates prepared messages");
        oldSignature = MessagePreparation.PreparationSignature(settings);
        settings.ReminderTimeRules.Add(new() { From = new(11, 20, 0), To = new(11, 10, 0) });
        Check(MessagePreparation.Prepare(rows, day, settings).First().CalendarStart.Hour == 11 && MessagePreparation.Prepare(rows, day, settings).First().CalendarStart.Minute == 20, "Rules do not cascade");
        Check(oldSignature != MessagePreparation.PreparationSignature(settings), "Rule edits invalidate prepared messages");
        var roundtrip = JsonSerializer.Deserialize<SmsMailSettings>(JsonSerializer.Serialize(settings))!;
        Check(roundtrip.AdjustReminderTimes && roundtrip.ReminderTimeRules[0].To == new TimeSpan(11, 20, 0), "Time rules survive settings serialization");
        var rules = ReminderTimes.Validate(settings.ReminderTimeRules);
        Check(ReminderTimes.Apply(day.AddHours(9), rules) == day.AddHours(9), "Unmatched appointment is unchanged");
        try { ReminderTimes.Validate(new[] { new AppointmentTimeRule(), new AppointmentTimeRule() }); throw new Exception("Duplicate rule allowed"); }
        catch (InvalidOperationException) { count++; }
        try { ReminderTimes.Validate(new[] { new AppointmentTimeRule { To = TimeSpan.FromDays(1) } }); throw new Exception("Invalid time allowed"); }
        catch (InvalidOperationException) { count++; }
        Check(mail.AppointmentStart == day.AddHours(11.5) && mail.AppointmentTimeDisplay == "11:30", "RDV column keeps the original appointment time");
        Check(mail.ArrivalHint == "Arrivée 11:20", "Adjusted arrival is clearly distinguished from appointment time");
        Check(mail.PeopleDisplay == "Alice Test" + Environment.NewLine + "Bob Test", "People column lists names only, one per line");
        Check(mail.BodyDisplay.Contains("11:20") && !mail.BodyDisplay.Contains("<table"), "Message column displays readable text with announced time");
        var interleaved = MessagePreparation.Prepare(new[]
        {
            new RdvInfo { Id = 11, ReferenceFamilyNumber = 11, Prenom = "A", Nom = "Premier", DateRDV = day, HeureRDV = day.AddHours(11.5), Tel1 = "0611111111" },
            new RdvInfo { Id = 22, ReferenceFamilyNumber = 22, Prenom = "B", Nom = "Second", DateRDV = day, HeureRDV = day.AddHours(11).AddMinutes(25), Tel1 = "0622222222" }
        }, day, settings);
        var sorted = MessageSelection.Visible(interleaved, "SMS", "");
        Check(sorted.Select(m => m.AppointmentTimeDisplay).SequenceEqual(new[] { "11:25", "11:30" }), "Table sorts by real time rather than adjusted arrival");
        Check(sorted.All(m => m.BodyDisplay == m.Body), "SMS message column preserves message text exactly");
        return count;
    }
}
