using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class FamilySmsChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var day = new DateTime(2026, 10, 23);
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        var first = new RdvInfo { Id = 701, ReferenceFamilyNumber = 80, Prenom = "Alice", Nom = "Test", DateRDV = day, HeureRDV = day.AddHours(9), Tel1 = "0612345678", Tel2 = "0698765432" };
        var second = new RdvInfo { Id = 702, ReferenceFamilyNumber = 80, Prenom = "Bob", Nom = "Test", DateRDV = day, HeureRDV = day.AddHours(10), Tel1 = "0677889900" };
        List<PreparedMessage> Prepare(params RdvInfo[] rows) => MessagePreparation.Prepare(rows, rows[0].DateRDV, settings).Where(m => m.Channel == "SMS").ToList();
        var initial = Prepare(first, second);
        Check(initial.Count == 1, "A family with three mobile numbers gets one SMS");
        Check(initial[0].Contact == "+33612345678", "Valid Tel1 is preferred");
        Check(initial[0].Family.Contains("Alice") && initial[0].Family.Contains("Bob"), "Family SMS contains all people");
        Check(Prepare(second, first)[0].Contact == initial[0].Contact, "Input order does not change selected phone");
        var directory = Path.Combine(Path.GetTempPath(), "family-sms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var history = new MessageHistory(Path.Combine(directory, "history.db"));
            Check(await history.ReserveAsync(initial[0]), "First family send is reserved");
            first.Tel1 = "0601020304"; first.Tel2 = ""; second.Tel1 = "0605060708";
            Check(!await new MessageHistory(Path.Combine(directory, "history.db")).ReserveAsync(Prepare(first, second)[0]), "Changed phone remains blocked after reopening history");
            Check(!await history.ReserveAsync(Prepare(second)[0]), "Same known family is blocked with only another member loaded");
            var reserved = await history.LoadReservedKeysAsync();
            Check(Prepare(second)[0].ReservationKeys.Any(reserved.Contains), "UI can mark a family as already processed");
            first.DateRDV = second.DateRDV = day.AddDays(1);
            Check(await history.ReserveAsync(Prepare(first, second)[0]), "Another appointment day can be reminded");
            first.DateRDV = second.DateRDV = day;
            // Simulate a send created by the old version on a secondary number.
            var legacyHistory = new MessageHistory(Path.Combine(directory, "legacy.db"));
            var otherPhone = Prepare(second)[0]; otherPhone.ReservationKeys.Clear();
            Check(await legacyHistory.ReserveAsync(otherPhone), "Legacy contact-only entry is recorded");
            Check(!await legacyHistory.ReserveAsync(Prepare(first, second)[0]), "Old secondary-phone send blocks a new family send");
            var remaining = await legacyHistory.LoadAsync();
            Check(remaining.Count == 1, "Rejected family reservation adds no phantom history entry");
            Check(await legacyHistory.ReserveAsync(new PreparedMessage { Key = "independent", Channel = "SMS", Contact = "+33600000000", ReservationKeys = new() { Prepare(first, second)[0].Key } }), "Conflicting reservation rolls back newly inserted guards");
            first.Tel1 = "invalide"; first.Tel2 = "0611223344";
            Check(Prepare(first)[0].Contact == "+33611223344", "Tel2 is used when Tel1 is invalid");
            var test = TestMessagePreparation.Create(settings, "", "0611223344", day)[0];
            Check(test.ReservationKeys.Count == 0 && test.Key.StartsWith("test-"), "Test SMS never reserves real family guards");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
        var families = Enumerable.Range(1, 4).SelectMany(size => Enumerable.Range(0, size).Select(index => new RdvInfo
        {
            Id = size * 100 + index, ReferenceFamilyNumber = size, Nom = "Famille" + size, Prenom = "Personne" + index,
            DateRDV = day, HeureRDV = day.AddHours(9 + size), Tel1 = "061234567" + size,
            Email = index == 0 ? "famille" + size + "@example.com" : ""
        })).ToArray();
        settings.SmsOnlyWhenNoEmail = false;
        for (int minimum = 1; minimum <= 3; minimum++)
        {
            settings.SmsMinimumFamilySize = minimum;
            var messages = MessagePreparation.Prepare(families, day, settings);
            Check(messages.Count(m => m.Channel == "SMS") == 5 - minimum, "Minimum SMS family size filters only small families");
            Check(messages.Count(m => m.Channel == "Mail") == 4, "Mail sends are unaffected by SMS size threshold");
        }
        var signature = MessagePreparation.PreparationSignature(settings);
        settings.SmsMinimumFamilySize = 2;
        Check(signature != MessagePreparation.PreparationSignature(settings), "Changing family threshold invalidates preparation");
        settings.SmsOnlyWhenNoEmail = true;
        Check(!MessagePreparation.Prepare(families, day, settings).Any(m => m.Channel == "SMS"), "One email on any family member excludes SMS when enabled");
        families.Single(r => r.Id == 300).Email = "invalid";
        Check(MessagePreparation.Prepare(families, day, settings).Count(m => m.Channel == "SMS") == 1, "Invalid email does not suppress eligible family SMS");
        var duplicate = new RdvInfo { Id = 999, ReferenceFamilyNumber = 999, Nom = "Unique", Prenom = "Patient", DateRDV = day, HeureRDV = day.AddHours(9), Tel1 = "0612345678" };
        Check(!MessagePreparation.Prepare(new[] { duplicate, duplicate }, day, settings).Any(m => m.Channel == "SMS"), "Duplicate appointments do not increase family person count");
        settings.SmsMinimumFamilySize = 3;
        Check(TestMessagePreparation.Create(settings, "", "0612345678", day).Single().Channel == "SMS" && settings.SmsMinimumFamilySize == 3, "Explicit SMS test bypasses threshold without changing settings");
        return count;
    }
}
