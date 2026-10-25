using EyeChat.Fundus;

internal static class ExamReviewChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTime(2034, 5, 10);
        FoConsultation V(string id, DateTime date, string text, string refraction = "", string code = "") => new(id, date, text, refraction, code, "", code.Length > 0);
        ExamPrediction[] Unknown() => Enumerable.Range(0, 4).Select(_ => new ExamPrediction(null, null, 0, 0, "", Array.Empty<string>())).ToArray();
        var old = V("old", now.AddYears(-5), "FO réalisé");
        var recent = V("latest", now.AddDays(-1), "Surveillance", "Myopie -6,00");
        check(ExamReviewContext.ApplyMyopia(new[] { old, recent }, now, now, Unknown())[0].Decision == true, "Explicit strong myopia becomes due at five years");
        check(ExamReviewContext.ApplyMyopia(new[] { old, recent }, now, now.AddDays(-1), Unknown())[0].Decision == null, "Five-year boundary is not anticipated");
        check(ExamReviewContext.ApplyMyopia(new[] { recent }, now, now, Unknown())[0].Explanation.Contains("non retrouvé"), "Missing prior FO needs confirmation");
        foreach (var text in new[] { "Myopie -5,00", "Cylindre -6,00", "Pas de myopie -6,00", "Antécédent familial myopie -6,00", "Myopie +6,00" })
            check(ExamReviewContext.ApplyMyopia(new[] { old, recent with { RefractionText = text } }, now, now, Unknown())[0].Decision == null, "No unsafe threshold inference: " + text);
        check(ExamReviewContext.Summary(new[] { old, recent }, now, now, now.AddYears(-40).AddDays(1)).Contains("39 ans"), "Age respects birthday");
        check(ExamReviewContext.Summary(new[] { recent }, now, now, null).Contains("non renseigné"), "Unknown age is not invented");
        check(ExamReviewContext.History(new[] { old, recent }, now).Contains("Pourquoi ce FO/OCT"), "History requests an explanation of documented FO");
        check(!ExamReviewContext.Performed(V("negative", now, "FO non réalisé", "", "BZQK001")), "Negated realization blocks CCAM evidence");
        var extracted = ExamLearningData.Extract(new[] { old, recent }, now, now);
        check(!extracted.Text.Contains("FO réalisé") && extracted.Text.IndexOf("REFRACTION") < extracted.Text.IndexOf("Observation"), "Latest-only text has requested field order");
        var legacy = new ExamTreeModel { FeatureVersion = "exam-local-v1", Exams = Enumerable.Range(0, 4).Select(_ => new ExamTreeResult()).ToArray() };
        check(ExamDecisionTree.Report(legacy).Contains("désactivé"), "Old model requires retraining after input format change");
        var examples = Enumerable.Range(1, 40).Select(i => new ExamExample
        {
            SourceVersion = 2, Group = "01" + i.ToString("X62"), AppointmentDay = now,
            Text = i % 2 == 0 ? "Contrôle rétinien demandé" : "Contrôle rétinien non nécessaire",
            Rationale = i % 2 == 0 ? "Contrôle rétinien demandé : motif de mon Oui" : "Contrôle rétinien non nécessaire : motif de mon Non",
            HistoricalRationale = "Commentaire historique privé absent du dossier courant",
            Signals = new[] { true }.Concat(new bool[15]).ToArray(), Answers = new bool?[] { i % 2 == 0, i % 2 == 0, null, null }
        }).ToArray();
        var learned = ExamDecisionTree.Train(examples);
        check(learned.Exams[0].Root?.Feature?.StartsWith('T') == true, "Grounded free explanations can supply learned text criteria without selected annotations");
        var exported = ExamDecisionTree.Export(learned);
        check(!exported.Contains("privé") && !exported.Contains("Rationale") && !exported.Contains("motif"), "Free and historical comments are not exported");
        foreach (var e in examples) { e.Rationale = "Texte inventé absent de la consultation"; e.HistoricalRationale = "Motif historique absent"; }
        check(ExamDecisionTree.Train(examples).Exams[0].Root?.Feature == null, "Ungrounded explanation never becomes an inference fact");
        foreach (var e in examples) e.SourceVersion = 1;
        check(ExamDecisionTree.Train(examples).Exams.All(t => t.Root == null), "Legacy examples are retained but excluded until reviewed in v2");
    }
}
