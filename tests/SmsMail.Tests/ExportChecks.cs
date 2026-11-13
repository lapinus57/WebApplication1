using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class ExportChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var day = new DateTime(2026, 11, 2);

        // Spreadsheet
        var sheet = new XlsxSheet { Name = "Messages: [test]", Headers = new[] { "A", "B" } };
        sheet.Rows.Add(new[] { "=HYPERLINK(\"http://x\")", "ligne 1\nligne 2 & <b>" });
        sheet.Rows.Add(new[] { "contrôle\u0001", null });
        var bytes = XlsxWriter.Create(new[] { sheet, new XlsxSheet { Name = "Messages: [test]" } });
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Check(names.Contains("[Content_Types].xml") && names.Contains("xl/workbook.xml") && names.Contains("xl/worksheets/sheet2.xml"), "Workbook has its parts");
            XDocument Load(string path) { using var s = zip.GetEntry(path)!.Open(); return XDocument.Load(s); }
            var workbook = Load("xl/workbook.xml");
            var sheetNames = workbook.Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => (string)e.Attribute("name")!).ToList();
            Check(sheetNames.SequenceEqual(new[] { "Messages test", "Messages test 2" }), "Sheet names are cleaned and unique");
            var cells = Load("xl/worksheets/sheet1.xml").Descendants().Where(e => e.Name.LocalName == "c").ToList();
            Check(cells.All(c => (string?)c.Attribute("t") == "inlineStr") && !cells.Any(c => c.Descendants().Any(d => d.Name.LocalName == "f")), "Cells are text only, never formulas");
            Check(cells[2].Value == "=HYPERLINK(\"http://x\")" && cells[3].Value == "ligne 1\nligne 2 & <b>", "Text, line breaks and markup survive as plain text");
            Check(cells[4].Value == "contrôle", "Characters invalid in XML are dropped");
            Check((string)cells[5].Attribute("r")! == "B3", "Cell references follow columns and rows");
        }
        Check(XlsxWriter.Column(0) == "A" && XlsxWriter.Column(25) == "Z" && XlsxWriter.Column(26) == "AA" && XlsxWriter.Column(701) == "ZZ", "Column letters");

        // Diagnostic
        RdvInfo P(int id, string first, string last, string address, string phone, string email, int minutes) => new()
        {
            Id = id, Prenom = first, Nom = last, Adresse = address, Ville = "Hautecroix", Tel1 = phone, Email = email, DateRDV = day,
            HeureRDV = day.AddHours(9).AddMinutes(minutes), DatePriseRDV = day.AddDays(-5).AddMinutes(minutes)
        };
        var a = P(4242, "Léa", "Martinez", "12 bis rue de la Gare", "06 12 34 56 78", "lea.martinez@orange.fr", 0);
        var b = P(4243, "Hugo", "Martinez", "12 bis rue de la Gare", "0612345678", "", 15); b.Remarque = "avec Léa Martinez, tél 06.12.34.56.78";
        var c = P(4244, "Zoé", "Inconnue", "3 impasse Verte", "0000000000", "pas de mail", 30);
        var lux = P(4245, "Jean", "De Almeida", "8 route de Luxembourg", "00352 621 123 456", "", 45); lux.Remarque = "j'ai dit de venir à 9h30";
        var luxPlus = P(4246, "Ana", "De Almeida", "8 route de Luxembourg", "+352 621 123 456", "", 50);
        var settings = new SmsMailSettings { SmsOnlyWhenNoEmail = false };
        settings.Sms.ApplicationSecret = "SECRET-OVH-123"; settings.Email.Password = "MOTDEPASSE-SMTP";
        var families = EyeEmailFamilyGrouping.Build(new[] { a, b, c, lux, luxPlus });
        var messages = MessagePreparation.Prepare(new[] { a, b, c, lux, luxPlus }, day, settings, families);
        var history = new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>> { [4242] = new() { (day.AddDays(-30), day.AddDays(-30).AddHours(9), null) } };
        DiagnosticPackage.Input Input(bool pseudo) => new()
        {
            Day = day, Appointments = new[] { a, b, c, lux, luxPlus }, Families = families, Messages = messages, History = history,
            Settings = settings, Pseudonymize = pseudo, Note = "Hugo Martinez reçoit deux SMS"
        };

        var real = DiagnosticPackage.BuildJson(Input(false));
        Check(real.Contains("Martinez") && real.Contains("06 12 34 56 78"), "Unpseudonymized export keeps the real data");
        Check(!real.Contains("SECRET-OVH-123") && !real.Contains("MOTDEPASSE-SMTP"), "Secrets are never exported");

        var json = DiagnosticPackage.BuildJson(Input(true));
        foreach (var leak in new[] { "Martinez", "Léa", "Hugo", "Zoé", "Inconnue", "Gare", "Hautecroix", "lea.martinez", "12 34 56 78", "0612345678", "612345678", "4242", "Almeida", "621123456" })
            // Mot entier seulement : les pseudonymes sont des syllabes aléatoires (« Bogare »), qui contiennent
            // parfois « gare » ou « hugo » par hasard. Une vraie fuite reste un mot ou un nombre isolé.
            Check(!System.Text.RegularExpressions.Regex.IsMatch(json, @"(?<![\p{L}\d])" + System.Text.RegularExpressions.Regex.Escape(leak) + @"(?![\p{L}\d])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant), "Pseudonymized export hides " + leak);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("appointments").EnumerateArray().ToList();
        string Field(int i, string name) => rows[i].GetProperty(name).GetString()!;
        Check(Field(0, "Nom") == Field(1, "Nom") && Field(0, "Nom") != Field(2, "Nom"), "Same surname gives the same pseudonym");
        Check(Field(0, "Tel1") == Field(1, "Tel1") && Field(0, "Tel1").StartsWith("06") && Field(0, "Tel1").Length == 10, "Same phone, in any format, gives the same fake mobile");
        Check(Field(2, "Tel1") == "0000000000", "Placeholder numbers stay visible");
        Check(Field(2, "Email") == "pas de mail", "Non-address email text stays visible");
        Check(Field(0, "Email").EndsWith("@orange.fr"), "Common mail providers are kept");
        Check(Field(0, "Adresse") == Field(1, "Adresse") && Field(0, "Adresse").StartsWith("12 bis "), "Address keeps its house number and equality");
        Check(Field(1, "Remarque").StartsWith("avec " + Field(0, "Prenom") + " " + Field(0, "Nom")) && Field(1, "Remarque").Contains(Field(0, "Tel1")), "Remarks use the same pseudonyms");
        Check(rows[0].GetProperty("Id").GetInt32() == 1 && doc.RootElement.GetProperty("history").TryGetProperty("1", out _), "Patient ids are renumbered consistently with history");
        Check(doc.RootElement.GetProperty("families").GetArrayLength() == families.Count, "Families are exported");
        Check(doc.RootElement.GetProperty("messages").GetArrayLength() == messages.Count, "Messages are exported");
        var smsContact = doc.RootElement.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("Channel").GetString() == "SMS").GetProperty("Contact").GetString()!;
        Check(smsContact == "+33" + Field(0, "Tel1").Substring(1), "SMS contact uses the same fake number");
        Check(!doc.RootElement.GetProperty("note").GetString()!.Contains("Hugo"), "The note is pseudonymized too");
        Check(Field(3, "Tel1").StartsWith("00352") && Field(3, "Tel1").Length == 14 && Field(4, "Tel1") == "+" + Field(3, "Tel1").Substring(2),
            "Foreign numbers keep their country code and written form, consistently");
        var luxSms = doc.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("Channel").GetString() == "SMS" && m.GetProperty("Contact").GetString()!.StartsWith("+352"));
        Check(luxSms.GetProperty("Contact").GetString() == Field(4, "Tel1"), "The SMS contact of a foreign mobile matches its fake number");
        Check(Field(3, "Remarque") == "j'ai dit de venir à 9h30" && Field(3, "Nom").StartsWith("De "), "Name particles such as 'de' are kept in names and sentences");

        var file = DiagnosticPackage.Encrypt(json, "correct horse battery");
        var envelope = Encoding.UTF8.GetString(file);
        Check(!envelope.Contains("appointments") && envelope.Contains("AES-256-GCM"), "File content is encrypted");
        Check(DiagnosticPackage.Decrypt(file, "correct horse battery") == json, "Decryption restores the snapshot");
        try { DiagnosticPackage.Decrypt(file, "mauvais mot de passe"); throw new Exception("Wrong password accepted"); }
        catch (InvalidOperationException) { count++; }
        try { DiagnosticPackage.Encrypt(json, "court"); throw new Exception("Short password accepted"); }
        catch (InvalidOperationException) { count++; }
        if (Environment.GetEnvironmentVariable("EXPORT_SAMPLE_DIR") is { Length: > 0 } dir)
            File.WriteAllBytes(Path.Combine(dir, "exemple.xlsx"), XlsxWriter.Create(new[] { MessageExportReport.PreparedSheet(messages), MessageExportReport.FamiliesSheet(families) }));
        return count;
    }
}
