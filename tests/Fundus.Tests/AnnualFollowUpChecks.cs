using Client.Fundus;
using EyeChat.Fundus;

internal static class AnnualFollowUpChecks
{
    public static void Run(Action<bool, string> check, DateTime day)
    {
        FoAnalysis? Analyze(string text, string refraction = "") => FundusAnalysis.Analyze(
            new[] { new FoConsultation("1", day.AddDays(-1), text, refraction) }, day);
        FoAnalysis? Timeline(params (int Days, string Text)[] visits) => FundusAnalysis.Analyze(
            visits.Select((v, i) => new FoConsultation(i.ToString(), day.AddDays(v.Days), v.Text)), day);

        foreach (var text in new[]
        {
            "DIABÈTE", "Patient diabétique", "Patiente diabétique de type 2", "Diabète non insulinodépendant",
            "Diabète non traité", "DNID", "DID", "Antécédent personnel de diabète",
            "Diabète ancien", "Diabète depuis 2020", "HTO ancienne traitée",
            "SURVEILLANCE OPHTALMOLOGIQUE CHEZ UN DIABETIQUE", "PAS DE RÉTINOPATHIE DIABÉTIQUE",
            "Rétinopathie diabétique minime", "HTO", "SURVEILLANCE TRAITEMENT DE HTO", "H.T.O.",
            "Hypertension oculaire", "Hypertonie oculaire traitée", "Hypertension intra-oculaire",
            "Tension oculaire élevée", "HTO non traitée", "HTO équilibrée sous traitement"
        })
        {
            var result = Analyze(text);
            check(result?.Status == FoStatus.Verify && result.Reason.Contains("Suivi annuel") && result.Excerpt == text,
                "Annual context keeps evidence and asks for review: " + text);
            check(Analyze("", text)?.Status == FoStatus.Verify, "Annual context also read from REFRACTION: " + text);
        }

        foreach (var text in new[]
        {
            "Non diabétique", "Patient pas diabétique", "Pas de diabète", "Absence de diabète", "Diabète : non",
            "Pas d’antécédent de diabète", "Absence d'antécédent d'HTO", "Sans antécédent de diabète",
            "Diabète exclu", "Sans HTO", "Pas d’HTO", "Pas d'hypertension oculaire", "HTO absente", "HTO non confirmée",
            "HTO ?", "Suspicion de diabète", "Diabète possible", "Rechercher une HTO", "Dépistage du diabète",
            "HTO non exclue", "Antécédents familiaux de diabète", "Mère diabétique", "HTO chez le père",
            "ATCD FAMILIAUX :\nDIABETE\nHTO", "Ancien diabète gestationnel", "Diabète résolu", "Prédiabète",
            "ATCD FAMILIAUX :\nDIABETE : OUI\nHTO : OUI",
            "Pré-diabétique", "Traitement anti-diabétique", "Diabète insipide",
            "HTA", "Hypertension artérielle", "Tension", "Tonus élevé", "CONTRÔLE TONUS", "TOD 16 TOG 15",
            "CONTRÔLE TONUS\nAVEC TRAITEMENT", "REVOIR TONUS DS 6 MOIS", "TRAITEMENT HYPOTENSEUR"
        })
            check(Analyze(text) == null, "No annual condition inferred: " + text);

        check(Analyze("Pas de diabète ni HTO") == null, "Both coordinated negations are excluded");
        check(Analyze("Pas de diabète ; HTO")?.Reason.Contains("HTO") == true, "Negation stays scoped to its condition");
        check(Analyze("ATCD FAMILIAUX :\nDIABETE\nATCD PERSONNELS :\nHTO")?.Reason.Contains("HTO") == true, "Personal heading ends family history section");
        check(Analyze("DIABÈTE", "HTO")?.Reason.Contains("contexte diabétique et HTO") == true, "Two conditions in the same consultation share one explained row");
        check(Analyze("DIABÈTE\nOCT\nFOD : Papille normale")?.Reason.Contains("Suivi annuel") == true, "Annual reason remains visible alongside imaging descriptions");
        check(Analyze("DIABÈTE", "Non diabétique") == null, "Contradictory clinical fields do not establish diabetes");
        check(Timeline((-800, "DIABETE"), (-1, "RAS"))?.Reason.Contains("Date du dernier FO à vérifier") == true, "Silent visits do not erase chronic context or invent an exam date");
        check(Timeline((-800, "DIABETE"), (-1, "Non diabétique")) == null, "Later explicit correction removes diabetes context");
        check(Timeline((-800, "DIABETE\nHTO"), (-1, "Pas de diabète"))?.Reason.Contains("HTO") == true, "Removing one condition preserves the other");
        check(Timeline((1, "HTO")) == null, "Future consultation does not establish a condition");
        check(Analyze("HTO\nFO non nécessaire") == null, "Same-visit explicit cancellation overrides annual rule");
        check(Timeline((-800, "HTO"), (-1, "FO non nécessaire")) == null, "Later cancellation overrides chronic context");
        check(Timeline((-800, "HTO"), (-10, "FO non nécessaire"), (-1, "HTO"))?.Status == FoStatus.Verify, "Fresh context after cancellation prompts a new review");
        check(Analyze("DIABÈTE\nREFAIRE FO DANS 6 MOS")?.Status == FoStatus.Planned, "Explicit shorter interval keeps priority over annual rule");
        check(Timeline((-800, "DIABETE\nFO prochaine fois"), (-1, "RAS"))?.Reason.Contains("Consultation ultérieure") == true, "Unresolved explicit instruction keeps its chronology");

        check(Analyze("DIABETE\nFO réalisé") == null, "Recent completed FO does not start an annual alert immediately");
        check(Timeline((-800, "HTO"), (-30, "FO réalisé")) == null, "Recent completion without prior request defers annual context");
        check(Timeline((-800, "HTO\nFO prochaine fois"), (-30, "FO réalisé"))?.Status == FoStatus.ProbablyPerformed, "Recent completed episode keeps its original state");
        check(Timeline((-800, "HTO\nFO prochaine fois"), (-400, "FO réalisé"))?.Status == FoStatus.Verify, "An old completed episode can return for annual review");
        check(Timeline((-800, "DIABETE"), (-400, "FO réalisé"), (-1, "RAS"))?.Reason.Contains("vérifier l’échéance") == true, "Annual reopening refers to recorded completion rather than silent follow-up");
        check(Timeline((-800, "DIABETE"), (-400, "FO réalisé"), (-30, "FO réalisé")) == null, "Latest explicit completion is the annual reference");
        check(Timeline((-800, "DIABETE"), (-1, "FO réalisé en 2020"))?.Status == FoStatus.Verify, "Historical completion is not dated as a new examination");
        check(Timeline((-800, "HTO"), (-1, "FO non réalisé"))?.Status == FoStatus.Verify, "Negated completion cannot defer review");

        var completedOn = day.AddYears(-1).Date;
        var visits = new[] { new FoConsultation("risk", completedOn.AddDays(-1), "DIABETE\nFO prochaine fois"), new FoConsultation("done", completedOn, "FO réalisé") };
        var before = FundusAnalysis.Analyze(visits, day.AddDays(-1));
        var anniversary = FundusAnalysis.Analyze(visits, day);
        check(before?.Status == FoStatus.ProbablyPerformed && anniversary?.Status == FoStatus.Verify, "Review reopens at calendar year boundary");
        check(anniversary?.EvidenceHash != before?.EvidenceHash, "Annual reopening invalidates previous completed-episode review");
        check(FundusAnalysis.Analyze(visits, day.AddDays(1))?.EvidenceHash == anniversary?.EvidenceHash, "Annual hash remains stable while evidence and eligibility are unchanged");
        var leap = new[] { new FoConsultation("leap", new DateTime(2024, 2, 29), "HTO\nFO réalisé") };
        check(FundusAnalysis.Analyze(leap, new DateTime(2025, 2, 27)) == null &&
            FundusAnalysis.Analyze(leap, new DateTime(2025, 2, 28))?.Status == FoStatus.Verify, "Annual interval handles leap day");

        var data = new FoDataSet("test", new[]
        {
            new FoPatientData("1", "Patient TEST A", day.AddHours(1), new[] { new FoConsultation("1", day.AddDays(-1), "DIABETE") }),
            new FoPatientData("2", "Patient TEST B", day.AddHours(2), new[] { new FoConsultation("2", day.AddDays(-1), "HTO") }),
            new FoPatientData("3", "Patient TEST C", day.AddHours(3), new[] { new FoConsultation("3", day.AddDays(-1), "HTA") })
        });
        var rows = FoPatientRow.Analyze(data, day, default);
        check(rows.Count == 2 && FoPatientRow.Filter(rows, 0, "").Count == 2 && FoPatientRow.Filter(rows, 1, "").Count == 0,
            "Native default list includes annual reviews but never labels them prescribed FO");
        check(rows.All(r => r.ReasonText.Contains("Suivi annuel")), "Annual reason is exposed in native table");
        var rich = FoMedicalText.Read("<p>DIABÈTE</p><p>HTO</p>");
        check(Analyze(rich)?.Reason.Contains("contexte diabétique et HTO") == true, "Annual analysis consumes readable HTML extraction");
    }
}
