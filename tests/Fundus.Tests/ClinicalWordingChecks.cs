using Client.Fundus;
using EyeChat.Fundus;

internal static class ClinicalWordingChecks
{
    // Short, fictitious fixtures: no real clinical histories, measurements or treatments are stored here.
    public static void Run(Action<bool, string> check, DateTime day)
    {
        FoAnalysis? Analyze(string text, string refraction = "") => FundusAnalysis.Analyze(
            new[] { new FoConsultation("1", day.AddDays(-1), text, refraction) }, day);
        FoAnalysis? Timeline(params (int Days, string Text)[] visits) => FundusAnalysis.Analyze(
            visits.Select((v, i) => new FoConsultation(i.ToString(), day.AddDays(v.Days), v.Text)), day);

        foreach (var text in new[]
        {
            "REFAIRE EXAMEN DES FO DANS 1 AN", "=> REFAIRE FO DANS 1 AN", "REFAIRE FO DANS 6 MOS",
            "REFAIRE FO DS 6 MOIS", "Refaire l’examen des fonds d’œil dans un an", "Refaire un fond d'oeil dans 6 mois",
            "RECONTRÔLER LES FO DANS 6 MOIS", "Renouveler le contrôle du FO dans 1 an", "FO dans 6 mois",
            "FO DS 6 MOS", "FO à 6 mois", "Refaire F.O. dans 1 an", "Refaire FOD dans 6 mois", "Refaire FO"
        })
            check(Analyze(text)?.Status == FoStatus.Planned, "Renewal or scheduled FO recognized: " + text);

        const string delayed = "=> REFAIRE EXAMEN DES FO\n       DANS 1 AN";
        var planned = Analyze(delayed);
        check(planned?.Status == FoStatus.Planned && planned.Excerpt.Contains("DANS 1 AN"), "Delay on next line is retained in evidence");
        check(planned?.Reason.Contains("délai") == true, "Renewal explains that the interval must be checked");
        check(Analyze("REFAIRE\nEXAMEN DES FO\nDANS 6 MOS")?.Status == FoStatus.Planned, "Wrapped renewal instruction is recognized");
        check(Analyze("REFAIRE\nEXAMEN DES FO")?.Excerpt == "REFAIRE\nEXAMEN DES FO", "Wrapped action retains its verb even without an interval");
        check(Analyze("Contrôler les FO la prochaine fois en 2027")?.Status == FoStatus.Planned, "Explicit future year is not a historical plan");
        check(Analyze("PAS DE RETINOPATHIE DIABETIQUE\n=> REFAIRE EXAMEN DES FO DANS 1 AN")?.Status == FoStatus.Planned, "Negative disease finding does not cancel a renewal");
        check(Analyze("PAS DE RETINOPATHIE DIABETIQUE => REFAIRE FO DANS 1 AN")?.Status == FoStatus.Planned, "An arrow separates a negative disease finding from the next instruction");
        check(Analyze("REVOIR TONUS DS 6 MOIS\nREFAIRE FO DANS 6 MOS\nSURVEILLER EVOLUTION DE LA CATARACTE")?.Excerpt == "REFAIRE FO DANS 6 MOS", "FO evidence is separated from other follow-up instructions");
        check(Analyze("", "REFAIRE EXAMEN DES FO DANS 1 AN")?.Status == FoStatus.Planned, "Renewal is also read from REFRACTION");

        foreach (var text in new[]
        {
            "NE PAS REFAIRE FO", "Pas refaire examen des FO dans 1 an", "Ne plus refaire FO",
            "Inutile de refaire FO", "Pas besoin de refaire FO", "Avait prévu de refaire FO dans 1 an",
            "Refaire OCT dans 6 mois", "Revoir tonus ds 6 mois", "Surveiller évolution de la cataracte",
            "SURVEILLANCE TRAITEMENT", "TONUS MESURÉ PAR AIR", "SURVEILLANCE OPHTALMOLOGIQUE CHEZ UN DIABÉTIQUE"
        })
            check(Analyze(text)?.Status != FoStatus.Planned, "No automatic FO prescription from a negation, history or other exam: " + text);
        check(Timeline((-8, "FO prochaine fois"), (-1, "Ne pas refaire FO")) == null, "Explicit negative renewal closes a prior request");
        check(Analyze("Refaire FO dans 6 mois si nécessaire")?.Status == FoStatus.Verify, "Conditional renewal remains uncertain");
        check(Analyze("Refaire FO dans 6 mois ?")?.Status == FoStatus.Verify, "Questioned renewal remains uncertain");

        foreach (var text in new[]
        {
            "FOD : Papille normale", "FOG : Macula normale", "V3FOD : Papille normale", "FO D : Papille normale",
            "OCT", "OCT Non dilaté", "FAIRE BILAN MACULAIRE", "SERA REVUE EN BILAN COMPATARATIF\nDANS 1 MOIS\nACUITE CV 24 OCT",
            "SERA REVU EN BILAN COMPARATIF\nDANS 1 MOIS\nOCT"
        })
        {
            var result = Analyze(text);
            check(result?.Status == FoStatus.Verify && result.Reason.Length > 0, "Contextual mention is explained, not prescribed: " + text);
        }
        check(Analyze("", "FOD : Papille normale\nFOG : Papille normale")?.Status == FoStatus.Verify, "Context in REFRACTION is included");
        check(Analyze("FAIRE BILAN MACULAIRE\nOCT\nFOD : Papille normale")?.Excerpt == "FAIRE BILAN MACULAIRE", "Workup instruction takes precedence over descriptive evidence");
        check(Analyze("BILAN COMPATARATIF\nDANS 1 MOIS", "OCT")?.Excerpt.Contains("DANS 1 MOIS") == true, "Comparative workup keeps delay and can use imaging context from the other field");
        check(Analyze("FOD : Papille normale\nFOG : Macula normale")?.Excerpt.Contains("FOG") == true, "Both eye report labels are visible in the evidence");

        foreach (var text in new[]
        {
            "TONUS MESURÉ PAR AIR", "CONTRÔLE TONUS\nAVEC TRAITEMENT", "REVOIR TONUS DS 6 MOIS",
            "SURVEILLER EVOLUTION DE LA CATARACTE", "SURVEILLANCE TRAITEMENT", "Pas de OCT", "OCT non indiqué",
            "Bilan maculaire annulé", "Ancien OCT", "FOD normal en 2020", "Rendez-vous le 23 oct. 2026",
            "Compte rendu oct 2026", "Bilan comparatif dans 1 mois", "OCTOBRE"
        })
            check(Analyze(text) == null, "No contextual false positive: " + text);

        check(Analyze("OCT\nFOD : Papille normale\nREFAIRE FO DANS 1 AN")?.Status == FoStatus.Planned, "Same-field report never overrides an explicit future FO");
        check(Analyze("REFAIRE FO DANS 1 AN", "OCT\nFOG : Papille normale")?.Status == FoStatus.Planned, "Other-field report never overrides an explicit future FO");
        check(Analyze("OCT\nFOD : Papille normale\nFO non nécessaire") == null, "Same-visit explicit cancellation overrides weak context");
        check(Timeline((-8, "OCT\nFOD : Papille normale"), (-1, "FO non nécessaire")) == null, "Later cancellation removes earlier context");
        check(Timeline((-8, "FO prochaine fois"), (-1, "OCT\nFOD : Papille normale"))?.Status == FoStatus.Verify, "A descriptive report alone is not proof that the requested FO was done");
        check(Timeline((-8, "FO prochaine fois"), (-1, "FO réalisé\nOCT"))?.Status == FoStatus.ProbablyPerformed, "Explicit completion retains priority over OCT context");
        check(Timeline((-8, "OCT"), (-1, "FAIRE BILAN MACULAIRE"))?.SourceId == "1", "Most recent contextual source is shown");
        check(Timeline((1, "OCT")) == null, "Future dated context is ignored");

        var data = new FoDataSet("test", new[]
        {
            new FoPatientData("1", "Patient TEST A", day.AddHours(1), new[] { new FoConsultation("1", day.AddDays(-1), "FOD : Papille normale") }),
            new FoPatientData("2", "Patient TEST B", day.AddHours(2), new[] { new FoConsultation("2", day.AddDays(-1), "REFAIRE FO DANS 6 MOS") }),
            new FoPatientData("3", "Patient TEST C", day.AddHours(3), new[] { new FoConsultation("3", day.AddDays(-1), "CONTRÔLE TONUS") })
        });
        var rows = FoPatientRow.Analyze(data, day, default);
        check(rows.Count == 2 && FoPatientRow.Filter(rows, 2, "").Single().PatientId == "1", "Native list includes contextual patient only in Verify");
        check(FoPatientRow.Filter(rows, 1, "").Single().PatientId == "2", "Native planned filter includes only the explicit instruction");
    }
}
