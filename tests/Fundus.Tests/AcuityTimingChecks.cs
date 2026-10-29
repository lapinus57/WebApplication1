using Client.Fundus;
using EyeChat.Fundus;

// Synthetic extracts in the cabinet's writing style; no real record is copied.
internal static class AcuityTimingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var day = new DateTime(2031, 5, 12, 9, 0, 0);
        FoConsultation Visit(string id, int days, string text, string refraction = "", string codes = "") =>
            new(id, day.AddDays(days), text, refraction, codes, "act", true);

        // Deadlines written in letters, half units and longer numbers.
        foreach (var (text, months, days) in new[]
        {
            ("Refaire FO dans cinq ans", 60, 0), ("Refaire FO dans quatre mois", 4, 0), ("Refaire FO dans dix-huit mois", 18, 0),
            ("Refaire FO dans 1 an 1/2", 18, 0), ("Refaire FO dans 1an1/2", 18, 0), ("Refaire FO dans un an et demi", 18, 0),
            ("Refaire FO dans 18 mois", 18, 0), ("Refaire FO dans 120 jours", 0, 120), ("Refaire FO dans 6 mois et demi", 6, 15),
            ("FO dans cinq ans", 60, 0), ("Ctrl FO dans 6 mois", 6, 0),
        })
        {
            var source = Visit("t", -1, text);
            var due = source.Date.Date.AddMonths(months).AddDays(days);
            var result = FundusAnalysis.Analyze(new[] { source }, day, due);
            check(result?.Due == due && result.Status == FoStatus.Planned, "Deadline read from: " + text);
            check(FundusAnalysis.Analyze(new[] { source }, day, day.AddDays(1))?.Status == FoStatus.Verify, "Not prepared before the deadline: " + text);
        }

        // "FAIRE FO DE CTRL" with a separate "=> REVOIR DANS 1 AN" line.
        var control = Visit("ctrl", -2, "=> REVOIR DANS 1 AN\n      FAIRE FO DE CTRL", "Réfraction subjective :\nOD= +2,00= 10 /10; Add 2,00 =P 2\nOG= +1,50 (-0,25 à 40°)= 10 /10");
        var controlDue = control.Date.Date.AddYears(1);
        check(FundusAnalysis.Analyze(new[] { control }, day, controlDue)?.Status == FoStatus.Planned, "FAIRE FO DE CTRL is an instruction");
        check(FundusAnalysis.Analyze(new[] { control }, day, controlDue)?.Due == controlDue, "Its deadline comes from REVOIR DANS 1 AN");
        check(FundusAnalysis.Analyze(new[] { control }, day, day.AddDays(7))?.Status == FoStatus.Verify, "FO de contrôle à 1 an is not due next week");
        check(FundusAnalysis.Analyze(new[] { Visit("done", -2, "FO fait") }, day) == null, "A completed FO is still not an instruction");

        // Acuity reading.
        var lenses = Visit("lenses", -1, "", "Lentilles portées :\nOD: SOUPLE ASPHERIQUE\n        Ro: 8,60 / Ø: 14,20 / -4,00 = 9 /10\n        Réf. Compl.: +0,25 = 10 /10 = P 2FF\nOG: SOUPLE ASPHERIQUE\n        Ro: 8,60 / Ø: 14,20 / -3,50 = 8 /10\n        Réf. Compl. VP: +1,50 = P 2");
        var read = FoAcuity.Read(lenses);
        check(read.Where(m => m.Eye == "OD").Max(m => m.Tenths) == 10 && read.Where(m => m.Eye == "OG").Max(m => m.Tenths) == 8, "Eye carried over indented lens lines; near vision ignored");
        var binocular = FoAcuity.Read(Visit("ab", -1, "", "Réfraction subjective :\nOD= -1,00= 9 /10\nOG= -1,25 (-0,25 à 30°)= 8 /10\nAB = 12 /10"));
        check(binocular.Count == 2 && binocular.Single(m => m.Eye == "OG").Tenths == 8, "Binocular AB value is not given to the left eye");
        check(FoAcuity.Read(Visit("inline", -1, "AV OD 7/10 OG 10/10")).Select(m => (m.Eye, m.Tenths)).SequenceEqual(new[] { ("OD", 7m), ("OG", 10m) }), "Inline OD before value");
        check(FoAcuity.Read(Visit("postfix", -1, "AV 7/10 OD 10/10 OG")).Select(m => (m.Eye, m.Tenths)).SequenceEqual(new[] { ("OD", 7m), ("OG", 10m) }), "Inline OD after value");
        check(FoAcuity.Read(Visit("date", -1, "OD contrôle du 10/10/2030")).Count == 0, "A date is not an acuity");
        check(FoAcuity.Read(Visit("weak", -1, "", "OD= Plan= 3-4 /10f; Add 3,00 =P 5f")).Single().Tenths == 4, "Range and suffix letter");
        check(FoAcuity.Read(Visit("dark", -1, "", "OD= +2,00 (-1,50 à 120°)= noir ; Add 3,00")).Single().Label == "< 1/10", "Vision written in words");
        check(FoAcuity.Read(Visit("plan", -1, "", "OD= Plan= 6 /10F")).Single().Tenths == 6, "Plan is a sphere, not light perception");

        // Acuity drop at the latest visit.
        var before = Visit("before", -400, "", "Réfraction subjective :\nOD= -1,00= 10 /10\nOG= -1,25= 10 /10");
        var after = Visit("after", -3, "", "Réfraction subjective :\nOD= -1,00= 6 /10F\nOG= -1,25= 10 /10");
        var drop = FundusAnalysis.Analyze(new[] { before, after }, day);
        check(drop?.Status == FoStatus.Verify && drop.Reason.Contains("Baisse d’acuité : OD 10/10") && drop.Reason.Contains("6/10") && !drop.Reason.Contains("OG 10/10 le"), "Drop of one eye asks for a review");
        check(drop?.SourceId == "after" && drop.Excerpt.Contains("6 /10F"), "Drop shows the latest measure");
        check(FundusAnalysis.Analyze(new[] { before, after with { RefractionText = "OD= -1,00= 8 /10\nOG= -1,25= 10 /10" } }, day) == null, "A two-line change is not flagged");
        check(FundusAnalysis.Analyze(new[] { after, before with { Date = day.AddDays(-1) } }, day) == null, "An improvement is not flagged");
        check(FundusAnalysis.Analyze(new[] { before, after with { CcamCodes = "BGQP002" } }, day)?.Status != FoStatus.Verify, "A FO coded at the same visit answers the drop");
        var planned = FundusAnalysis.Analyze(new[] { before, after with { Text = "Prévoir FO" } }, day);
        check(planned?.Status == FoStatus.Planned && planned.Reason.Contains("Baisse d’acuité"), "An explicit instruction stays planned with the drop noted");
        check(FundusAnalysis.Analyze(new[] { before, after with { RefractionText = "OD= +1,00= noir\nOG= -1,25= 10 /10" } }, day)?.Reason.Contains("< 1/10") == true, "Drop to vision below 1/10");
        check(FundusAnalysis.Analyze(new[] { before, after }, day)?.EvidenceHash != FundusAnalysis.Analyze(new[] { before, after with { RefractionText = "OD= -1,00= 5 /10\nOG= -1,25= 10 /10" } }, day)?.EvidenceHash, "A different drop invalidates a review");

        // Table: deadline column and sorting.
        var soon = new FoPatientData("a", "Patient A", day.AddDays(5), new[] { Visit("a1", -170, "Refaire FO dans 6 mois") });
        var later = new FoPatientData("b", "Patient B", day.AddDays(1), new[] { Visit("b1", -400, "Refaire FO dans 1 an") });
        var none = new FoPatientData("c", "Patient C", day.AddDays(2), new[] { Visit("c1", -10, "Prévoir FO") });
        var rows = FoPatientRow.Analyze(new("s", new[] { soon, later, none }), day, default);
        check(FoPatientRow.Filter(rows, 4, "").Select(r => r.PatientId).SequenceEqual(new[] { "b", "c", "a" }), "Default order is the appointment");
        check(FoPatientRow.Filter(rows, 4, "", byDue: true).Select(r => r.PatientId).SequenceEqual(new[] { "b", "a", "c" }), "Deadline order puts rows without deadline last");
        check(rows.Single(r => r.PatientId == "c").DueText == "Non précisée" && rows.Single(r => r.PatientId == "b").DueText.Length == 10, "Deadline column text");
        var six = FoPatientRow.Analyze(new("s", new[] { new FoPatientData("d", "Patient D", day.AddDays(1), new[] { Visit("d1", -5 * 366, "Refaire FO dans 6 ans") }) }), day, default).Single();
        check(six.PreparationText.StartsWith("Préparation dès le"), "Six-year rule shows its preparation start");
    }
}
