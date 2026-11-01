using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class GroupingChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var day = new DateTime(2026, 11, 2);
        RdvInfo P(int id, string first, string last, string address, string phone, int minutes, int creation) => new()
        {
            Id = id, Prenom = first, Nom = last, Adresse = address, Ville = "Ville fictive", Tel1 = phone, DateRDV = day,
            HeureRDV = day.AddHours(9).AddMinutes(minutes), DatePriseRDV = day.AddDays(-20).AddMinutes(creation)
        };
        RdvFamille FamilyOf(List<RdvFamille> families, RdvInfo rdv) => families.Single(f => f.RDVs.Contains(rdv));
        var noHistory = new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>();

        var a = P(1, "Alice", "Un", "1 rue A", "0611111111", 0, 0);
        var b = P(2, "Bruno", "Deux", "2 rue B", "0622222222", 10, 500);
        var c = P(3, "Camille", "Trois", "3 rue C", "0611111111", 20, 900); c.Tel2 = "0622222222";
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b, c }).Count == 1, "A contact shared with two families merges them, whatever the order");
        Check(FamilyOf(EyeEmailFamilyGrouping.Build(new[] { a, b, c }), a).GroupingDisplay == "Téléphone ou e-mail", "Contact families show their method");
        b.Tel1 = ""; b.Email = "parent@example.com"; c.Tel2 = ""; c.Email = "PARENT@example.com";
        Check(EyeEmailFamilyGrouping.Build(new[] { a, b, c }).Count == 1, "Phone and email links combine");

        var paul = P(10, "Paul", "Dupont", "5 rue X", "0633333333", 0, 0); paul.Remarque = "avec Martin";
        var lea = P(11, "Léa", "Martin", "9 rue Y", "0644444444", 15, 3000);
        var jean = P(12, "Jean", "Martin", "8 rue Z", "0655555555", 300, 6000);
        var families = EyeEmailFamilyGrouping.Build(new[] { paul, lea, jean });
        Check(FamilyOf(families, paul).RDVs.Count == 2 && FamilyOf(families, paul).RDVs.Contains(lea), "A remark name joins the closest homonym only");
        Check(FamilyOf(families, jean).RDVs.Count == 1, "The unrelated homonym stays alone");
        Check(FamilyOf(families, paul).GroupingDisplay == "Remarque", "Remark families are not labelled as address matches");

        var anne = P(20, "Anne", "Durand", "5 rue X", "0633333333", 0, 0); anne.Remarque = "avec sa fille";
        var sy = P(21, "Moussa", "Sy", "9 rue Y", "0644444444", 15, 3000);
        Check(EyeEmailFamilyGrouping.Build(new[] { anne, sy }).Count == 2, "Words like 'sa fille' never fuzzy-match a short surname");

        var eva = P(30, "Eva", "Petit", "1 rue P", "0611111111", 0, 0); eva.Remarque = "avec Hugo Grand";
        var hugo = P(31, "Hugo", "Grand", "2 rue G", "0622222222", 15, 4);
        var zoe = P(32, "Zoé", "Autre", "3 rue Q", "0633333333", 30, 2);
        var asked = false;
        EyeEmailFamilyGrouping.Build(new[] { eva, hugo, zoe }, noHistory, (_, _) => { asked = true; return false; });
        Check(asked, "Families built from a remark are offered at the booking-time step");

        var u1 = P(40, "Ugo", "Un", "1 rue U", "0000000000", 0, 0); u1.Email = "u1@example.com";
        var u2 = P(41, "Vera", "Deux", "2 rue V", "0000000000", 200, 5000);
        Check(EyeEmailFamilyGrouping.Build(new[] { u1, u2 }).Count == 2, "A placeholder number does not link patients");
        var mail = MessagePreparation.Prepare(new[] { u1, u2 }, day, new SmsMailSettings()).Single(m => m.Channel == "Mail");
        Check(!mail.Body.Contains("Vera"), "A placeholder number cannot disclose another patient in a mail");
        u1.Tel1 = u2.Tel1 = "0600000000";
        Check(EyeEmailFamilyGrouping.Build(new[] { u1, u2 }).Count == 2, "0600000000 is a placeholder too");
        u1.Tel1 = u2.Tel1 = "00";
        Check(EyeEmailFamilyGrouping.Build(new[] { u1, u2 }).Count == 2, "Truncated numbers do not link patients");
        u1.Email = u2.Email = "pas de mail";
        Check(EyeEmailFamilyGrouping.Build(new[] { u1, u2 }).Count == 2, "Text that is not an address does not link patients");

        var h1 = P(50, "Hélène", "Cinq", "1 rue H", "", 0, 0);
        var h2 = P(51, "Igor", "Six", "2 rue I", "", 15, 0);
        h1.DatePriseRDV = h2.DatePriseRDV = default;
        var past = day.AddDays(-30);
        var history = new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>
        {
            [50] = new() { (past, past.AddHours(9), past.AddHours(8.9)) },
            [51] = new() { (past, past.AddHours(9.25), past.AddHours(8.95)) }
        };
        asked = false;
        EyeEmailFamilyGrouping.Build(new[] { h1, h2 }, history, (_, _) => { asked = true; return true; });
        Check(!asked, "Two missing creation dates are not a shared booking time");

        RdvInfo K(int id, string first, string phone, int minutes, int known) => new()
        {
            Id = id, Prenom = first, Nom = "Lambert", Adresse = "1 rue K", Ville = "Ville fictive", Tel1 = phone, DateRDV = day,
            HeureRDV = day.AddHours(9).AddMinutes(minutes), DatePriseRDV = day.AddDays(-20).AddMinutes(minutes), ReferenceFamilyNumber = known
        };
        var mother = K(60, "Mère", "0611223344", 0, 17);
        var child1 = K(61, "Enfant1", "", 10, 17);
        var child2 = K(62, "Enfant2", "0611223344", 20, 0);
        families = EyeEmailFamilyGrouping.Build(new[] { mother, child1, child2 });
        Check(families.Count == 1 && families[0].RDVs.Count == 3, "A new member sharing a phone joins the EyeEmail family");
        foreach (var size in new[] { 1, 2, 3 })
        {
            var sms = MessagePreparation.Prepare(new[] { mother, child1, child2 }, day,
                new SmsMailSettings { SmsOnlyWhenNoEmail = false, SmsMinimumFamilySize = size }, families);
            Check(sms.Count == 1 && sms[0].PeopleDisplay.Contains("Enfant2"), $"Threshold {size}: the new member is in the family SMS");
        }
        child2.Tel1 = ""; child2.Adresse = "9 rue Ailleurs"; child2.Remarque = "avec Mère Lambert";
        Check(EyeEmailFamilyGrouping.Build(new[] { mother, child1, child2 }).Count == 1, "A remark can name a member of an EyeEmail family");
        var solo = K(63, "Seul", "", 0, 18);
        families = EyeEmailFamilyGrouping.Build(new[] { solo });
        Check(families.Count == 1 && families[0].GroupingDisplay == "Famille EyeEmail", "A known family with one person today is kept");
        var other = K(64, "Cousin", "0611223344", 30, 19); other.Adresse = "5 rue Autre";
        child2.Tel1 = "0611223344"; child2.Remarque = "";
        families = EyeEmailFamilyGrouping.Build(new[] { mother, child1, other, child2 });
        Check(families.Count == 2 && FamilyOf(families, other) != FamilyOf(families, mother), "Two EyeEmail families sharing a phone stay separate");
        Check(FamilyOf(families, child2) == FamilyOf(families, mother) || FamilyOf(families, child2) == FamilyOf(families, other),
            "The new member joins one of them, never both");
        other.Remarque = "avec Mère Lambert";
        Check(EyeEmailFamilyGrouping.Build(new[] { mother, child1, other }).Count == 2, "A remark cannot merge two EyeEmail families");

        // "Regroupés par" column
        var settingsAll = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        string Reason(IEnumerable<RdvInfo> rows, string channel, string contact) =>
            MessagePreparation.Prepare(rows, day, settingsAll).Single(m => m.Channel == channel && m.Contact == contact).GroupingReason;
        var r1 = P(70, "Rose", "Lune", "4 rue R", "0611112222", 0, 0);
        var r2 = P(71, "Rémi", "Lune", "9 rue S", "06 11 11 22 22", 15, 100);
        Check(Reason(new[] { r1, r2 }, "SMS", "+33611112222") == "Même téléphone : 06 11 11 22 22", "Shared phone is named, whatever its format");
        r2.Tel1 = "0633334444"; r2.Adresse = "4 rue R"; r1.Email = r2.Email = "lune@example.com";
        var reason = Reason(new[] { r1, r2 }, "Mail", "lune@example.com");
        Check(reason.Contains("Même e-mail : lune@example.com") && reason.Contains("Même adresse : 4 rue R, Ville fictive"), "Email and address reasons are listed together");
        r1.Email = r2.Email = ""; r2.Adresse = "9 rue S"; r2.Remarque = "avec Rose Lune"; r2.Tel1 = "0633334444";
        reason = Reason(new[] { r1, r2 }, "SMS", "+33611112222");
        Check(reason == "Remarque de Rémi Lune : « avec Rose Lune »", "A remark link quotes the remark and its author");
        r2.Remarque = "";
        Check(Reason(new[] { r1, r2 }, "SMS", "+33611112222") == GroupingExplanation.Alone, "A person alone is shown as such");
        r1.ReferenceFamilyNumber = r2.ReferenceFamilyNumber = 30;
        Check(Reason(new[] { r1, r2 }, "SMS", "+33611112222") == "Famille EyeEmail", "EyeEmail families are named");
        r1.ReferenceFamilyNumber = r2.ReferenceFamilyNumber = 0; r2.Tel1 = r1.Tel1;
        settingsAll.UseEyeEmailFamilies = false;
        Check(Reason(new[] { r1, r2 }, "SMS", "+33611112222") == "Même téléphone : 06 11 11 22 22", "Without family grouping, a shared number still explains the shared SMS");
        var sheetRow = MessageExportReport.PreparedSheet(MessagePreparation.Prepare(new[] { r1, r2 }, day, settingsAll)).Rows.Single(r => r[2] == "SMS");
        Check(sheetRow[5] == "Même téléphone : 06 11 11 22 22", "The spreadsheet has the reason column");
        return count;
    }
}
