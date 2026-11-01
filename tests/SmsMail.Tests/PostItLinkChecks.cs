using System.Text.Json;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

static class PostItLinkChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string Names(string text) => string.Join(" | ", PostItFamilyLinks.CitedNames(text).Select(c => string.Join(" ", c.Words)));

        // Reading the post-it (examples from the cabinet)
        Check(Names("+cédric   CS TOPO LUN GOU 1 OK \n\n+léna     CS LUN 1 OK \n\nrdv avec liam") == "CEDRIC | LENA", "+Prénom lines; 'rdv avec' free text ignored");
        Check(Names("Gabriel CS 1ok  LUN\nAubin   CS 1ok  \nAurore  CS 1ok  LUN\n\n  + Benoit dans 1 an\n   benoit c'est le mari de Aurore :") == "GABRIEL | AUBIN | AURORE | BENOIT",
            "Name followed by a cotation, +Prénom; a free sentence is ignored");
        Check(Names("CS   LUN\n1 ok\n\n+ Jean Michel\ncs lun\n1 ok") == "JEAN MICHEL", "Compound first name");
        Check(Names("Zakato  CS 1\nMigejo CS 1 LUN\nVovufo\nDanada") == "ZAKATO | MIGEJO | VOVUFO | DANADA", "A line holding only a first name");
        Check(Names("OR   \nlun  gou\n\n garde rv de novembre\n========\nDL\nGOUT\nLETTRE BO +/- reeducation MME RAUSCH\n( Jebril avec d 1 an)\nor de 20\ncf doss Dupont") == "",
            "Codes, separators, amounts, cf and free text are never names");
        Check(Names("Turgay OR 1 lun\ndl\nRamazan CS 1 OK  LUn\n+misiri CS 1ok\nBamoro OR 61\nKojono  Dupuis   6-1ok") == "TURGAY | RAMAZAN | MISIRI | BAMORO | KOJONO DUPUIS",
            "Follow-ups 1, 61, 6-1 after a name");

        // Finding the cited people among the day's appointments
        var day = new DateTime(2026, 3, 12);
        int id = 0;
        RdvInfo A(string first, string last, int minutes) => new() { Id = ++id, Prenom = first, Nom = last, DateRDV = day, HeureRDV = day.AddHours(9).AddMinutes(minutes) };
        var gabriel = A("Gabriel", "Roth", 0);
        var aubin = A("Aubin", "Roth", 15);
        var aurore = A("Aurore", "Roth-Weber", 30);
        var benoit = A("Benoit", "Muller", 200);          // husband, other surname, much later
        var lena = A("Léna", "Keller", 40);                // other surname, within one hour
        var lena2 = A("Léna", "Schmitt", 40);              // same distance: tie
        var jm = A("Jean-Michel", "Roth", 5);
        PostItAnalysis.PostIt Note(RdvInfo owner, string text) => new() { PatientCode = owner.Id, Date = owner.HeureRDV.AddMinutes(20), Nom = owner.Nom, Prenom = owner.Prenom, Text = text };
        var all = new[] { gabriel, aubin, aurore, benoit, lena, jm };

        var (links, unresolved) = PostItFamilyLinks.Build(new[] { Note(gabriel, "Gabriel CS 1ok LUN\nAubin CS 1ok\nAurore CS 1ok\n+ Benoit dans 1 an\n+Jean Michel") }, all);
        Check(links.Count == 3 && links.All(l => l.SameSurname) && links.Select(l => l.Cited).ToHashSet().SetEquals(new[] { aubin, aurore, jm }),
            "Same surname (compound surname included) links; the owner's own line is skipped");
        Check(unresolved.Single().Reason == PostItFamilyLinks.TooFar && unresolved.Single().CitedName == "BENOIT", "Other surname more than one hour apart: no link");

        (links, unresolved) = PostItFamilyLinks.Build(new[] { Note(gabriel, "CS 1 ok\n+léna") }, all);
        Check(links.Single().Cited == lena && !links.Single().SameSurname && links.Single().MinutesApart == 40, "Other surname less than one hour apart: link");
        (links, unresolved) = PostItFamilyLinks.Build(new[] { Note(gabriel, "CS 1 ok\n+léna") }, all.Append(lena2));
        Check(links.Count == 0 && unresolved.Single().Reason == PostItFamilyLinks.Ambiguous, "Two equally possible people: no link");
        (links, unresolved) = PostItFamilyLinks.Build(new[] { Note(gabriel, "CS 1 ok\n+Liam") }, all);
        Check(links.Count == 0 && unresolved.Single().Reason == PostItFamilyLinks.NoCandidate, "Nobody with that first name that day: no link");
        var otherDay = A("Aubin", "Roth", 0); otherDay.DateRDV = day.AddDays(1); otherDay.HeureRDV = otherDay.DateRDV.AddHours(9);
        (links, _) = PostItFamilyLinks.Build(new[] { Note(gabriel, "+Aubin") }, new[] { gabriel, otherDay });
        Check(links.Count == 0, "Only appointments of the same day count");
        var roth2 = A("Aubin", "Roth", 300);
        (links, _) = PostItFamilyLinks.Build(new[] { Note(gabriel, "+Aubin") }, all.Append(roth2));
        Check(links.Single().Cited == aubin, "Several namesakes with the same surname: the closest appointment wins");
        (links, _) = PostItFamilyLinks.Build(new[] { Note(aubin, "Roth Aubin CS 1 ok\n+Gabriel") }, all);
        Check(links.Single().Cited == gabriel, "The owner's line written 'Nom Prénom' is not a cited person");

        (links, _) = PostItFamilyLinks.Build(new[] { Note(gabriel, "+Aubin"), Note(aubin, "+Aurore"), Note(lena, "CS 1 ok") }, all);
        var families = PostItFamilyLinks.Families(links);
        Check(families.Count == 1 && families[0].SetEquals(new[] { gabriel.Id, aubin.Id, aurore.Id }), "Links combine into one family");

        // Report
        var postIts = new List<PostItAnalysis.PostIt> { Note(gabriel, "CS 1 ok\n+Aubin\n+léna\n+ Benoit dans 1 an"), Note(aubin, "rappeler pour devis") };
        var json = PostItAnalysis.BuildLinksReportJson(postIts, all, all.Select(r => (r.Nom, r.Prenom)), day, day, true);
        using var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary");
        Check(summary.GetProperty("consultationPostIts").GetInt32() == 1 && summary.GetProperty("links").GetInt32() == 2
            && summary.GetProperty("differentSurname").GetInt32() == 1, "Report summary counts links");
        foreach (var leak in new[] { "Gabriel", "Aubin", "Léna", "léna", "Benoit", "Roth", "Keller", "Muller" })
            Check(!json.Contains(leak), "Links report hides " + leak);
        Check(doc.RootElement.GetProperty("unresolved")[0].GetProperty("Motif").GetString() == PostItFamilyLinks.TooFar
            && doc.RootElement.GetProperty("unresolved")[0].GetProperty("Ligne").GetString()!.EndsWith(" dans 1 an"), "Unresolved cases keep their line, pseudonymized");
        var file = DiagnosticPackage.Encrypt(json, "mot de passe solide", PostItAnalysis.LinksFormat);
        Check(DiagnosticPackage.Decrypt(file, "mot de passe solide") == json, "Links report decrypts");
        return count;
    }
}
