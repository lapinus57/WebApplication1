using System.Text.Json;
using Client.SmsMail.Services;

static class PostItChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }

        // Real wording sent by the cabinet (one block = one post-it).
        var consultation = new[]
        {
            "OR   \nlun  gou\n\n garde rv de novembre",
            " Gisèle  OR  1ok\n \nPatrick  OR  1ok \n\ndans 1an avec geroge",
            "cs lun len *2   1 ok",
            "+cédric   CS TOPO LUN GOU 1 OK \n\n+léna     CS LUN 1 OK \n\nrdv avec liam",
            "lunettes\n\nor   1 an",
            "Gabriel CS 1ok  LUN\nAubin   CS 1ok  \nAurore  CS 1ok  LUN\n\n  + Benoit dans 1 an\n   benoit c'est le mari de Aurore :",
            "( Jebril avec d 1 an)\ncs lun\n 1 ok",
            "Turgay OR 1 lun\ndl\n\nLETTRE BO +/- reeducation MME RAUSCH",
            "cs 6 1  ok\n gou",
            "V3F",
            "4-8-12 ok"
        };
        foreach (var sample in consultation) Check(PostItAnalysis.IsConsultationPostIt(sample), "Consultation post-it is kept: " + sample.Split('\n')[0]);
        foreach (var sample in new[] { "Rappeler Mme Dupont pour le devis", "Ordonnance à faxer", "Courrier reçu", "", "   ", "Corriger l'adresse" })
            Check(!PostItAnalysis.IsConsultationPostIt(sample), "Other post-it is left out: " + sample);

        // Query built from the real schema
        var tables = new[] { "tPostIT", "Patients", "RDV" };
        IReadOnlyCollection<string> Columns(string t) => t switch
        {
            "tPostIT" => new[] { "IdPI", "CodePat", "DatePI", "MessageText" },
            "Patients" => new[] { "Code Patient", "Nom", "Prénom" },
            _ => Array.Empty<string>()
        };
        var query = PostItAnalysis.BuildQuery(tables, Columns, true);
        Check(query.Contains("FROM [tPostIT] t LEFT JOIN [Patients] p ON t.[CodePat] = p.[Code Patient]") && query.Contains("p.[Prénom] AS PatientPrenom")
            && query.Contains("t.[DatePI] >= ? AND t.[DatePI] < ?"), "Post-it query joins patient names and filters the dates");
        Check(!PostItAnalysis.BuildQuery(tables, Columns, false).Contains("JOIN"), "Query without patients for a type mismatch fallback");
        Check(PostItAnalysis.BuildNamesQuery(tables, Columns) == "SELECT DISTINCT [Nom] AS PatientNom, [Prénom] AS PatientPrenom FROM [Patients]", "Names query");
        try { PostItAnalysis.BuildQuery(new[] { "PostIt2" }, Columns, true); throw new Exception("Missing table accepted"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("PostIt2"), "Missing table error lists similar tables"); }

        // RTF storage
        Check(PostItAnalysis.PlainText(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 Gis\'e8le OR 1ok\par Patrick CS\par}") == "Gisèle OR 1ok\nPatrick CS", "RTF post-it becomes plain text");
        Check(PostItAnalysis.PlainText("texte simple") == "texte simple", "Plain text is unchanged");

        // Export
        var day = new DateTime(2026, 10, 5, 11, 20, 0);
        var postIts = new List<PostItAnalysis.PostIt>
        {
            new() { PatientCode = 8812, Date = day, Nom = "Roth", Prenom = "Gabriel", Text = consultation[5] },
            new() { PatientCode = 8813, Date = day.AddDays(1), Nom = "Keller", Prenom = "Mathis", Text = consultation[3] },
            new() { PatientCode = 8812, Date = day.AddDays(2), Nom = "Roth", Prenom = "Gabriel", Text = "Rappeler pour le devis" }
        };
        var names = new[] { ("Roth", "Gabriel"), ("Roth", "Aubin"), ("Roth", "Aurore"), ("Muller", "Benoit"), ("Keller", "Cédric"), ("Keller", "Léna"), ("Weber", "Liam"), ("Mari", "Paul"), ("Lun", "Or") };
        var json = PostItAnalysis.BuildJson(postIts, names, day.Date, day.Date.AddDays(3), true);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("postits").EnumerateArray().ToList();
        Check(doc.RootElement.GetProperty("totalPostIts").GetInt32() == 3 && rows.Count == 2, "Only consultation post-it are exported");
        foreach (var leak in new[] { "Gabriel", "Aubin", "Aurore", "Benoit", "benoit", "Cédric", "cédric", "Léna", "liam", "Roth", "Keller", "8812", "devis" })
            Check(!json.Contains(leak), "Post-it export hides " + leak);
        var text = rows[0].GetProperty("Texte").GetString()!;
        Check(text.Contains(" CS 1ok  LUN") && text.Contains("c'est le mari de ") && text.Contains("dans 1 an"), "Codes, follow-up and relationship words stay readable");
        Check(rows[1].GetProperty("Texte").GetString()!.Contains("CS TOPO LUN GOU 1 OK") && rows[1].GetProperty("Texte").GetString()!.Contains("rdv avec "), "Codes are never taken for names");
        Check(text.StartsWith(rows[0].GetProperty("Prenom").GetString()!), "The patient's own first name uses the same pseudonym in the text");
        Check(rows[0].GetProperty("Patient").GetInt32() == 1 && rows[0].GetProperty("Date").GetString() == "2026-10-05 11:20", "Patient code renumbered, date kept");
        var clear = PostItAnalysis.BuildJson(postIts, names, day.Date, day.Date.AddDays(3), false);
        Check(clear.Contains("benoit c'est le mari de Aurore") && clear.Contains("8812"), "Unpseudonymized export keeps the text as written");

        var file = DiagnosticPackage.Encrypt(json, "mot de passe solide", PostItAnalysis.Format);
        Check(DiagnosticPackage.Decrypt(file, "mot de passe solide") == json, "Post-it export decrypts");
        if (Environment.GetEnvironmentVariable("EXPORT_SAMPLE_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "postit.eyediag"), file);
        return count;
    }
}
