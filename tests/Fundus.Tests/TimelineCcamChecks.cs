using Client.Fundus;
using EyeChat.Fundus;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

internal static class TimelineCcamChecks
{
    // Synthetic records only: no uploaded clinical histories or real consultation identifiers.
    public static void Run(Action<bool, string> check)
    {
        var day = new DateTime(2032, 7, 10);
        FoConsultation Visit(string id, int days, string text, string codes = "") =>
            new(id, day.AddDays(days), text, "", codes, "test-act", true);
        FoAnalysis? Analyze(params FoConsultation[] visits) => FundusAnalysis.Analyze(visits, day);
        var request = Visit("request", -100, "Prévoir FO");
        foreach (var code in new[] { "BGQP002", "BGQP003", "BGQP004", "BZQK001" })
        {
            var done = Visit("recent", -10, "RAS", code);
            var result = Analyze(request, done);
            check(result?.Status == FoStatus.ProbablyPerformed && result.LaterDate == done.Date && result.LaterExcerpt.Contains(code), "Later cabinet FO act reconciles old instruction: " + code);
            check(Analyze(done) == null, "A recorded act alone does not prescribe another examination: " + code);
            check(Analyze(done, Visit("renewal", -1, "Refaire FO"))?.Status == FoStatus.Planned, "Fresh instruction after an act remains open: " + code);
            check(Analyze(Visit("same", -1, "Refaire FO", code))?.Status == FoStatus.Planned, "Same-visit future request takes priority over coded completion: " + code);
        }
        check(Analyze(request, Visit("other", -1, "RAS", "BLQP004"))?.Status == FoStatus.Verify, "Other examinations do not fulfil FO");
        check(Analyze(request, Visit("none", -1, "RAS"))?.Status == FoStatus.Verify, "Missing CCAM cannot prove completion or cancellation");
        check(Analyze(request, Visit("future", 1, "RAS", "BZQK001"))?.Status == FoStatus.Planned, "Future coded visits cannot fulfil old instructions");
        var contradiction = Analyze(request, Visit("negated", -1, "FO non réalisé", "BZQK001"));
        check(contradiction?.Status == FoStatus.Verify && contradiction.Reason.Contains("contradictoires") && contradiction.LaterExcerpt.Contains("BZQK001"), "Negative text prevents a code from proving completion and keeps its evidence visible");
        check(Analyze(request, Visit("cancelled", -1, "FO non nécessaire", "BZQK001"))?.Status == FoStatus.Verify, "Code versus cancellation stays visible for review");
        check(Analyze(Visit("a", -1, "Refaire FO"), Visit("b", -1, "", "BZQK001"))?.Status == FoStatus.Verify, "Different visits with tied timestamps cannot establish order");
        var coded = Visit("recent", -1, "RAS", "BZQK001");
        check(Analyze(request, coded)?.EvidenceHash != Analyze(request, coded with { CcamCodes = "BLQP004" })?.EvidenceHash, "Changing a tariff code invalidates an earlier review");
        check(Analyze(request, coded)?.EvidenceHash != Analyze(request, coded with { LocalActCode = "other-act" })?.EvidenceHash, "Local act reassignment invalidates a review");
        check(Analyze(request, coded with { ActLinkAvailable = false })?.Status == FoStatus.Verify, "Unavailable linkage never supplies completion evidence");

        foreach (var report in new[] { "OCT sans dilater\nFOD : Papille normale\nFOG : Macula normale\nRevoir dans 1 an",
            "OCT non dilaté\nFOD : Papille normale\nRevoir dans 1 an", "V3FOD : Papille normale\nPas de lésion fragile" })
            check(Analyze(Visit("normal", -100, report)) == null, "Normal descriptive reports do not imply another FO");
        check(Analyze(Visit("old", -300, "Faire bilan maculaire"), Visit("latest", -1, "RAS")) == null, "A weak old workup does not displace a later silent visit");
        check(Analyze(request, Visit("latest", -1, "FOD : Papille normale"))?.Status == FoStatus.Verify, "Normal report alone does not erase an explicit outstanding request");
        var recentReason = Analyze(request, Visit("latest", -1, "RAS"));
        check(recentReason?.Reason.Contains(day.AddDays(-1).ToString("dd/MM/yyyy")) == true && recentReason.LaterDate == day.AddDays(-1), "Latest visit is visible even when the instruction is old");

        check(Analyze(Visit("risk", -400, "HTO"), coded) == null, "Cabinet OCT-FO act updates annual follow-up reference");
        check(Analyze(Visit("risk", -400, "DIABETE"), Visit("old-exam", -370, "", "BZQK001"))?.Reason.Contains("Suivi annuel") == true, "Annual rule resumes after an old coded exam");
        check(Analyze(Visit("risk", -400, "DIABETE"), Visit("not-done", -1, "FO non réalisé", "BZQK001"))?.Status == FoStatus.Verify, "Contradictory recent act does not hide annual preparation");
        var annual = new[] { Visit("risk", -800, "HTO"), new FoConsultation("exam", day.AddYears(-1).AddDays(3), "", "", "BZQK001", "act", true) };
        check(FundusAnalysis.Analyze(annual, day) == null && FundusAnalysis.Analyze(annual, day, day.AddDays(5))?.Status == FoStatus.Verify, "Annual deadline is assessed at the appointment, without reading future consultations");

        foreach (var instruction in new[] { "REFAIRE V3FODG DANS 6 ANS", "REFAIRE V3 FODG\nDANS 6 ANS" })
        {
            var source = new FoConsultation("six-years", new DateTime(2024, 3, 15), instruction);
            var atFive = FundusAnalysis.Analyze(new[] { source }, new DateTime(2029, 3, 1), new DateTime(2029, 3, 15));
            check(atFive?.Status == FoStatus.Planned && atFive.Reason.Contains("15/03/2030") && atFive.Reason.Contains("dès 5 ans"), "Six-year instruction is prepared at five years with original date visible");
            check(FundusAnalysis.Analyze(new[] { source }, new DateTime(2029, 3, 1), new DateTime(2029, 3, 14))?.Status == FoStatus.Verify, "Six-year preparation boundary is not advanced further");
            check(FundusAnalysis.Analyze(new[] { source, new FoConsultation("done", new DateTime(2029, 2, 1), "", "", "BZQK001", "act", true) }, new DateTime(2029, 3, 1), new DateTime(2029, 3, 15))?.Status == FoStatus.ProbablyPerformed, "Later examination still overrides early preparation");
        }
        var timed = Visit("timed", -1, "Refaire FO dans 6 mois");
        var due = timed.Date.AddMonths(6);
        check(FundusAnalysis.Analyze(new[] { timed }, day, due.AddDays(-1))?.Status == FoStatus.Verify && FundusAnalysis.Analyze(new[] { timed }, day, due)?.Status == FoStatus.Planned, "Shorter deadlines retain their exact boundary");
        check(FundusAnalysis.Analyze(new[] { timed }, day, due.AddDays(-1))?.EvidenceHash != FundusAnalysis.Analyze(new[] { timed }, day, due)?.EvidenceHash, "Crossing the preparation boundary invalidates manual review");
        check(FundusAnalysis.Analyze(new[] { timed }, day, due)?.EvidenceHash == FundusAnalysis.Analyze(new[] { timed }, day, due.AddDays(1))?.EvidenceHash, "Rescheduling within one eligibility phase preserves evidence hash");
        check(Analyze(Visit("periphery", -400, "V3FOD : compte rendu\nRECONTROLE PERIPHERIE OD\nREVOIR DANS 1 AN"))?.Status == FoStatus.Planned, "Retinal peripheral recheck is detected with a generic follow-up deadline");
        check(Analyze(Visit("other", -400, "Recontrôler périphérie du champ visuel")) == null, "Non-retinal peripheral instruction is not assumed to be FO");
        check(Analyze(Visit("negative", -400, "V3FOD : compte rendu\nNe pas recontrôler périphérie OD")) == null, "Negated peripheral recheck is excluded");
        check(Analyze(Visit("conditional", -400, "V3FOD : compte rendu\nRecontrôler périphérie OD si nécessaire"))?.Status == FoStatus.Verify, "Conditional peripheral recheck remains uncertain");
        check(Analyze(request, Visit("negative-v3", -1, "Pas de V3FO prévu")) == null, "V3 negation closes a prior request");
        var conflicting = new FoConsultation("conflict", day.AddDays(-1), "Refaire FO dans 6 mois", "Refaire FO dans 1 an");
        var conflict = Analyze(conflicting);
        check(conflict?.Status == FoStatus.Verify && conflict.Reason.Contains("Délais FO contradictoires") && !conflict.Reason.Contains("Échéance calculée"), "Conflicting fields do not arbitrarily choose a deadline");
        var leap = new FoConsultation("leap", new DateTime(2028, 2, 29), "Refaire FO dans 1 an");
        check(FundusAnalysis.Analyze(new[] { leap }, new DateTime(2029, 2, 27), new DateTime(2029, 2, 28))?.Status == FoStatus.Planned, "Calendar year deadlines handle leap day");

        check(FoCcam.Parse("bzqk001 + BGQP002/2 ; BZQK001") == "BGQP002 + BZQK001", "Combined CCAM values are normalized and deduplicated");
        check(FoCcam.Parse("XBGQP002 BGQP0029 local-123") == "", "Partial codes and local identifiers are not CCAM matches");
        check(FoCcam.Parse(DBNull.Value) == "", "Null tariff values remain unknown");
        var rows = FoPatientRow.Analyze(new("source", new[] { new FoPatientData("p", "Patient TEST", day.AddDays(1), new[] { request, coded, Visit("future", 3, "", "BGQP004") }) }), day, default);
        check(rows.Single().LatestConsultationDate == coded.Date && rows.Single().LatestCcamCodes == "BZQK001", "Table shows last actual visit instead of source or future visit");
        check(FoPatientRow.Filter(rows, 4, "bzqk001").Count == 1 && FoPatientRow.Filter(rows, 4, "enregistré").Count == 1, "Search covers CCAM and the displayed reason");
        CheckQuery(check);
    }

    private static void CheckQuery(Action<bool, string> check)
    {
        using var db = new SqliteConnection("Data Source=:memory:"); db.Open();
        using (var create = db.CreateCommand())
        {
            create.CommandText = "CREATE TABLE Consultation ([N° consultation] INTEGER, [Code patient] INTEGER, [Date] TEXT, Observation TEXT, REFRACTION TEXT, [Code acte] INTEGER); " +
                "CREATE TABLE Tarif ([Code acte] INTEGER, ACTE TEXT); " +
                "INSERT INTO Tarif VALUES (11,'BZQK001'),(12,'BGQP002 + BZQK001'); " +
                "INSERT INTO Consultation VALUES (1,7,'2030-01-01','Note A','Refraction A',11),(2,7,'2031-01-01','Note B','Refraction B',NULL),(3,7,'2032-01-01','Note C','Refraction C',99),(4,8,'2030-01-01','Other patient','',12);";
            create.ExecuteNonQuery();
        }
        var map = new FoMapping { ConsultationsTable = "Consultation", ConsultationId = "N° consultation", ConsultationPatientId = "Code patient", ConsultationDate = "Date", ObservationFields = new[] { "Observation", "REFRACTION" } };
        var schema = new[] { new FoField("Consultation", "Code acte", 3), new FoField("Tarif", "Code acte", 3), new FoField("Tarif", "ACTE", 130) };
        var query = FoConsultationQuery.Create(map, schema);
        List<object[]> Read(FoConsultationQuery q, bool single = false)
        {
            using var command = db.CreateCommand(); int i = 0;
            command.CommandText = Regex.Replace(q.Sql, @"\?", _ => "$p" + i++);
            command.Parameters.AddWithValue("$p0", 7);
            if (single) command.Parameters.AddWithValue("$p1", 1);
            using var reader = command.ExecuteReader(); var result = new List<object[]>();
            while (reader.Read()) { var values = new object[reader.FieldCount]; reader.GetValues(values); result.Add(values); }
            return result;
        }
        var rows = Read(query);
        check(query.HasActs && rows.Count == 3 && (string)rows[0][5] == "BZQK001", "Actual SELECT joins local Code acte to Tarif.ACTE for the requested patient");
        check(rows[1][5] == DBNull.Value && rows[2][5] == DBNull.Value, "LEFT JOIN preserves null and unmatched act consultations");
        check((string)rows[0][2] == "Note A" && (string)rows[0][3] == "Refraction A", "Clinical column ordinals stay intact after join");
        check(Read(FoConsultationQuery.Create(map, schema, single: true), true).Count == 1, "Detail query binds both patient and consultation");
        var unavailable = FoConsultationQuery.Create(map, Array.Empty<FoField>());
        check(!unavailable.HasActs && Read(unavailable).All(r => r.Length == 4), "Missing tariff schema keeps text-only analysis usable");
        check(!FoConsultationQuery.Create(map, schema, includeActs: false).HasActs, "Patient registry import does not depend on tariffs");
        using (var duplicate = db.CreateCommand()) { duplicate.CommandText = "INSERT INTO Tarif VALUES (11,'BGQP002')"; duplicate.ExecuteNonQuery(); }
        var duplicated = Read(query);
        check(duplicated.Count(r => Convert.ToInt32(r[0]) == 1) == 2, "Ambiguous tariff linkage stays detectable by reader's duplicate consultation guard");
    }
}
