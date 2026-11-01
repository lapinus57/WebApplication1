using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class HomonymChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var day = new DateTime(2026, 11, 2);
        RdvInfo Person(int id, string first, string address, string phone, int minutes) => new()
        {
            Id = id, Prenom = first, Nom = "Exemple", Adresse = address, Ville = "Ville fictive",
            Tel1 = phone, DateRDV = day, HeureRDV = day.AddHours(9).AddMinutes(minutes),
            DatePriseRDV = day.AddDays(-10).AddHours(8).AddMinutes(minutes / 10)
        };
        var a = Person(901, "Alice", "12 rue Alpha", "0611111111", 0);
        var b = Person(902, "Bruno", "7 avenue Beta", "0622222222", 15);
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "Homonyms with different addresses and mobiles stay separate despite nearby appointments and creation times");
        Check(EyeEmailFamilyGrouping.Build(new[] { b, a }).Count == 2, "Input order cannot merge homonyms");
        var sms = MessagePreparation.Prepare(new[] { a, b }, day, settings);
        Check(sms.Count == 2 && sms.All(m => m.Channel == "SMS"), "Both unrelated people receive their own SMS");
        Check(sms.Single(m => m.Contact == "+33611111111").PeopleDisplay == "Alice Exemple", "Alice SMS lists only Alice");
        Check(sms.Single(m => m.Contact == "+33622222222").PeopleDisplay == "Bruno Exemple", "Bruno SMS lists only Bruno");
        Check(!sms.Single(m => m.Contact == "+33611111111").Body.Contains("Bruno"), "SMS body excludes unrelated homonym");
        a.Email = "alice@example.com"; b.Email = "bruno@example.com";
        var messages = MessagePreparation.Prepare(new[] { a, b }, day, settings);
        Check(messages.Count == 4, "Two separate mails and two separate SMS");
        Check(!messages.Single(m => m.Contact == a.Email).Body.Contains("Bruno"), "Alice mail does not disclose Bruno");
        Check(!messages.Single(m => m.Contact == b.Email).Body.Contains("Alice"), "Bruno mail does not disclose Alice");
        b.Email = ""; settings.SmsOnlyWhenNoEmail = true;
        messages = MessagePreparation.Prepare(new[] { a, b }, day, settings);
        Check(messages.Count == 2 && messages.Any(m => m.Channel == "SMS" && m.Contact == "+33622222222"), "Alice mail must not suppress Bruno SMS");
        a.Email = ""; settings.SmsMinimumFamilySize = 2;
        Check(MessagePreparation.Prepare(new[] { a, b }, day, settings).Count == 0, "Unrelated homonyms must not satisfy the two-person SMS threshold");
        settings.SmsMinimumFamilySize = 1;
        b.HeureRDV = day.AddHours(18);
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "Creation proximity alone cannot merge homonyms");
        a.DatePriseRDV = b.DatePriseRDV = default; b.HeureRDV = a.HeureRDV.AddMinutes(15);
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "Appointment proximity alone cannot merge homonyms");
        b.Adresse = "2 rue Alpha";
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "House number 2 is not house number 12");
        a.HeureRDV = b.HeureRDV.AddMinutes(15);
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "House number distinction holds in reverse time order");
        b.Adresse = a.Adresse;
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 1, "Identical address and city still group");
        b.Adresse = "7 avenue Beta"; b.Tel1 = a.Tel1;
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 1, "Shared phone still groups");
        b.Tel1 = "0622222222"; a.Email = b.Email = "family@example.com";
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 1, "Shared email still groups");
        a.Email = b.Email = ""; a.ReferenceFamilyNumber = b.ReferenceFamilyNumber = 17;
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 1, "Explicit family membership still takes priority");
        a.ReferenceFamilyNumber = b.ReferenceFamilyNumber = 0;
        var c = Person(903, "Camille", "12 rue Alpha", a.Tel1, 20);
        var families = EyeEmailFamilyGrouping.Build(new[] { a, b, c });
        Check(families.Count == 2 && families.Single(f => f.RDVs.Contains(b)).RDVs.Count == 1, "Homonym cannot attach to an established contact-based family");
        a.Adresse = b.Adresse = ""; a.Tel1 = b.Tel1 = "";
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b }).Count == 2, "Missing contacts do not make a name sufficient evidence");
        return count;
    }
}
