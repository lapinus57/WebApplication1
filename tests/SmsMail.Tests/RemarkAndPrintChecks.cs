using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class RemarkAndPrintChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var day = new DateTime(2026, 10, 30);
        var person = new RdvInfo { Id = 700, Nom = "Exemple", Prenom = "Alice", DateRDV = day,
            HeureRDV = day.AddHours(16).AddMinutes(45), Tel1 = "0612345678", Email = "example@example.com" };
        var rules = new Dictionary<TimeSpan, TimeSpan> { [new(16, 45, 0)] = new(16, 35, 0), [new(16, 0, 0)] = new(15, 50, 0) };
        foreach (var remark in new[] { "j'ai dit de venir à 16h00", "J’AI DIT DE VENIR À 16H00", "arriver à 16:00", "Convoquée à 16h", "arrivée pour 16 heures", "venir à 16h00 au lieu de 16h45", "se présenter à 16h00" })
        {
            person.Remarque = remark;
            var decision = RemarkArrivalTime.Resolve(person, rules);
            Check(!decision.NeedsReview && decision.Time == day.AddHours(16), "Explicit instruction takes priority without cascading: " + remark);
        }
        foreach (var remark in new[] { "appel à 16h00", "venir avec les lunettes", "ne pas venir à jeun", "" })
        {
            person.Remarque = remark;
            var decision = RemarkArrivalTime.Resolve(person, rules);
            Check(!decision.NeedsReview && decision.Time == day.AddHours(16).AddMinutes(35), "Unrelated remark retains general rule: " + remark);
        }
        foreach (var remark in new[] { "ne pas venir à 16h00", "venir à 16h00 ou 16h30", "venir vers 16h00", "venir à 25h00", "venir à 16h65", "venir à 16h00 ?", "venir demain à 16h00", "venir le 31/10 à 16h00", "venir à 16h00. arrivée à 15h00", "venir à 16h00, appel à 15h00", "venir à 16h001", "venir à 16h00 au lieu de 17h00", "venir à 16h00 le 31", "souhaite venir à 16h00" })
        {
            person.Remarque = remark;
            var decision = RemarkArrivalTime.Resolve(person, rules);
            Check(decision.NeedsReview && decision.Time == person.HeureRDV, "Ambiguous remark cannot silently adjust time: " + remark);
        }
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false, UseEyeEmailFamilies = false };
        person.Remarque = "";
        var baseline = MessagePreparation.Prepare(new[] { person }, day, settings);
        person.Remarque = "j'ai dit de venir à 16h00";
        var messages = MessagePreparation.Prepare(new[] { person }, day, settings);
        Check(messages.Count == 2 && messages.All(m => m.Body.Contains("16:00") && !m.Body.Contains("16:45")), "Mail and SMS announce remark time with general rules disabled");
        Check(messages.All(m => m.AppointmentTimeDisplay == "16:45" && m.ArrivalHint == "Arrivée 16:00"), "Table keeps original time and labels arrival");
        Check(messages.All(m => m.CanSend && m.Selected && m.TimeExplanation.Contains("Remarque")), "Clear adjustment is sendable and explained");
        Check(messages.Select(m => m.Key).SequenceEqual(baseline.Select(m => m.Key)), "Remark cannot bypass duplicate keys");
        Check(messages.Single(m => m.Channel == "SMS").ReservationKeys.SequenceEqual(baseline.Single(m => m.Channel == "SMS").ReservationKeys), "Family guards survive remark adjustment");
        Check(person.HeureRDV == day.AddHours(16).AddMinutes(45), "Input appointment remains unchanged");
        Check(CalendarInvitation.Create(messages.Single(m => m.Channel == "Mail"), "cabinet@example.com").Contains("DTSTART:20261030T150000Z"), "ICS matches announced Paris winter time");
        person.Remarque = "venir à 16h00 ou 16h30";
        var blocked = MessagePreparation.Prepare(new[] { person }, day, settings);
        Check(blocked.All(m => !m.CanSend && !m.Selected && m.Status.Contains("vérifier")), "Ambiguous time blocks both channels");
        foreach (var message in blocked) message.Selected = true;
        Check(MessageSelection.ForSending(blocked).Count == 0, "Checking a blocked message cannot send it");
        var second = new RdvInfo { Id = 701, Nom = "Exemple", Prenom = "Bob", DateRDV = day, HeureRDV = day.AddHours(17), Email = person.Email, Tel1 = person.Tel1 };
        Check(MessagePreparation.Prepare(new[] { second, person }, day, settings).All(m => !m.CanSend), "An ambiguous family member blocks the entire grouped message");

        messages[0].Contact = "<img src='https://example.com/track'>";
        var prepared = MessagePrintReport.Prepared(messages, day);
        Check(prepared.Contains("16:45") && prepared.Contains("16:00") && prepared.Contains("RDV / venue"), "Print separates scheduled and announced times");
        Check(prepared.Contains("&lt;img") && !prepared.Contains("<img"), "Print encodes imported HTML");
        Check(prepared.Contains("A4 landscape") && prepared.Contains("table-header-group"), "Print uses landscape and repeated headers");
        var entries = new List<MessageHistoryEntry>
        {
            new() { Channel = "Mail", Contact = "a@example.com", CreatedAtLocal = day, CreatedAt = "30/10/2026 00:00", Body = "<p>Bonjour</p><img src='https://example.com/track'><script>alert(1)</script>", Status = "Accepté" },
            new() { Channel = "SMS", Contact = "+33612345678", CreatedAtLocal = day.AddHours(23).AddMinutes(59), CreatedAt = "30/10/2026 23:59", Body = "Rappel <test>", Status = "Livré" },
            new() { Channel = "Mail", Contact = "previous@example.com", CreatedAtLocal = day.AddTicks(-1), Body = "HIER" },
            new() { Channel = "SMS", Contact = "next", CreatedAtLocal = day.AddDays(1), Body = "DEMAIN" }
        };
        Check(MessagePrintReport.FilterHistory(entries, day, "", "").Count == 2, "Day filter includes local midnight to 23:59 only");
        Check(MessagePrintReport.FilterHistory(entries, day, "Mail", " A@EXAMPLE ").Count == 1, "History combines day, channel and search");
        Check(MessagePrintReport.FilterHistory(entries, null, "", "").Count == 4, "Clearing day restores all entries");
        var report = MessagePrintReport.History(entries, day);
        Check(!report.Contains("HIER") && !report.Contains("DEMAIN"), "History print enforces selected day independently");
        Check(report.Contains("Bonjour") && !report.Contains("<script") && !report.Contains("<img") && !report.Contains("https://example.com"), "Mail print contains readable text and no active resources");
        Check(report.Contains("Rappel &lt;test&gt;") && report.Contains("Livr"), "SMS body is encoded and status preserved");
        Check(report.Contains("default-src 'none'") && report.Contains("Jour d’envoi"), "Report blocks resources and identifies day semantics");

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var history = new MessageHistory(path);
            await history.ReserveAsync(messages[0]);
            var entry = (await history.LoadAsync()).Single();
            Check(entry.CreatedAtLocal.HasValue && Math.Abs((entry.CreatedAtLocal.Value - DateTime.Now).TotalSeconds) < 30, "Existing UTC history timestamps load as typed local dates");
            Check(MessagePrintReport.FilterHistory(new[] { entry }, entry.CreatedAtLocal!.Value.Date, "", "").Count == 1, "Persisted entry is printable without parsing localized display text");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
        return count;
    }
}
