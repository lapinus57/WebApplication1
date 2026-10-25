using EyeChat.Fundus;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ExamLearningChecks
{
    // Entirely synthetic fixtures, unrelated to uploaded patient records.
    public static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string name)
        {
            try { action(); check(false, name); }
            catch (FoDataException) { check(true, name); }
        }
        var day = new DateTime(2032, 7, 10);
        bool[] Signals() { var flags = new bool[ExamLearningData.SignalNames.Length]; flags[0] = true; return flags; }
        ExamExample Example(int id, bool test, bool yes) => new()
        {
            Group = (test ? "00" : "01") + id.ToString("X62"), AppointmentDay = day, Signals = Signals(),
            Text = yes ? "Contrôle rétinien demandé." : "Contrôle rétinien non nécessaire.",
            Answers = new bool?[] { yes, !yes, yes, null },
            Annotations = Enumerable.Range(0, 3).Select(i => new ExamAnnotation(i, i == 1 ? !yes : yes,
                yes ? "Contrôle rétinien demandé" : "Contrôle rétinien non nécessaire")).ToList()
        };
        var examples = Enumerable.Range(1, 40).Select(i => Example(i, false, i % 2 == 0))
            .Concat(Enumerable.Range(41, 20).Select(i => Example(i, true, i % 2 == 0))).ToArray();
        var model = ExamDecisionTree.Train(examples);
        var positive = ExamDecisionTree.Predict(model, examples[1].Text, Signals());
        var negative = ExamDecisionTree.Predict(model, examples[0].Text, Signals());
        for (int i = 0; i < 3; i++)
        {
            check(model.Exams[i].Root?.Yes + model.Exams[i].Root?.No == 40 && model.Exams[i].TestCount == 20, "Patients are separated between training and test");
            check(model.Exams[i].FalsePositive == 0 && model.Exams[i].FalseNegative == 0 && model.Exams[i].Tested, "Learned text distinctions generalize to held-out synthetic patients");
            check(positive[i].Decision == true && negative[i].Decision == false, "Each examination learns its own human labels, including No");
            check(positive[i].Excerpts.Any() && negative[i].Excerpts.Any(), "Yes and No show a matched explanatory passage");
            check(model.Exams[i].Questions == 0, "Supported held-out cases are counted as direct classifications");
        }
        check(model.Exams[3].Root == null && positive[3].Decision == null && positive[3].Suggested == null, "Unreviewed target stays unknown rather than being learned as No");
        check(negative[0].Excerpts.Any(s => s.Contains("non nécessaire", StringComparison.Ordinal)), "Negative explanation prefers the annotated phrase including its negation");
        check(ExamDecisionTree.Predict(null, "RAS", Signals()).All(p => !p.Decision.HasValue), "No supplied model means no automatic classification");
        check(ExamDecisionTree.Predict(model, "Texte entièrement inconnu", Signals()).All(p => !p.Decision.HasValue), "Absence of learned evidence alone never authorizes a direct answer");
        var absent = new bool[Signals().Length];
        check(ExamDecisionTree.Predict(model, examples[1].Text, absent).All(p => !p.Decision.HasValue), "Missing consultations require confirmation");
        var conflicting = Signals(); conflicting[15] = true;
        check(ExamDecisionTree.Predict(model, examples[1].Text, conflicting)[0].Decision == null, "FO chronology contradiction requires confirmation despite a pure leaf");

        var annualFlags = Signals(); annualFlags[4] = annualFlags[13] = true;
        check(ExamDecisionTree.Predict(null, "", annualFlags)[0].Decision == true, "Annual diabetes rule needs no trained model");
        annualFlags[4] = false; annualFlags[5] = true;
        check(ExamDecisionTree.Predict(null, "", annualFlags)[1].Decision == true, "Annual HTO applies to unified FO/OCT");
        annualFlags[6] = true;
        check(ExamDecisionTree.Predict(null, "", annualFlags)[0].Decision == null, "Recent exam prevents automatic annual preparation");
        annualFlags[6] = false; annualFlags[15] = true;
        check(ExamDecisionTree.Predict(null, "", annualFlags)[0].Decision == null, "Contradictions override annual rule");
        var json = ExamDecisionTree.Export(model);
        var imported = ExamDecisionTree.Import(json);
        check(ExamDecisionTree.Export(imported) == json, "Validated model roundtrip retains only the trained DTO");
        check(ExamDecisionTree.Predict(imported, examples[1].Text, Signals()).Select(p => p.Decision).SequenceEqual(positive.Select(p => p.Decision)), "Exported model runs with the same decisions on a production workstation");
        check(!json.Contains("Contrôle", StringComparison.OrdinalIgnoreCase) && !json.Contains("rétinien", StringComparison.OrdinalIgnoreCase) && !json.Contains("demandé") && examples.All(e => !json.Contains(e.Group)), "Model contains no raw excerpts or patient group identifiers");
        check(!json.Contains("Annotations") && !json.Contains("AppointmentDay") && !json.Contains("Signals"), "Export does not serialize training dossiers");
        check(ExamDecisionTree.Report(model).Contains("faux Non") && ExamDecisionTree.Report(model).Contains("Questions sur les tests"), "Report includes both error types and abstentions");

        // Test notes/annotations/labels can change evaluation, never the learned vocabulary or tree.
        var altered = examples.Select(e => Example(int.Parse(e.Group[2..], System.Globalization.NumberStyles.HexNumber), ExamLearningData.IsTest(e.Group), e.Answers[0] == true)).ToArray();
        foreach (var e in altered.Where(e => ExamLearningData.IsTest(e.Group)))
        {
            e.Text = "Terme exclusif au jeu réservé.";
            e.Annotations = Enumerable.Range(0, 3).Select(i => new ExamAnnotation(i, e.Answers[i]!.Value, e.Text)).ToList();
        }
        var changedTest = ExamDecisionTree.Train(altered);
        check(JsonSerializer.Serialize(model.Exams.Select(e => e.Root)) == JsonSerializer.Serialize(changedTest.Exams.Select(e => e.Root)), "Held-out text and annotations do not leak into feature discovery or fitted trees");
        check(changedTest.Exams[0].FalseNegative + changedTest.Exams[0].FalsePositive > 0 && !changedTest.Exams[0].Tested, "Bad validation disables direct classification");
        check(changedTest.Exams[0].Questions == 20, "Failed validation exposes every test case as needing confirmation");
        check(ExamDecisionTree.Predict(changedTest, examples[1].Text, Signals())[0].Decision == null, "A pure training leaf cannot override failed held-out validation");

        var small = examples.Take(10).Concat(examples.Skip(40)).ToArray();
        var smallModel = ExamDecisionTree.Train(small);
        check(smallModel.Exams[0].Tested && ExamDecisionTree.Predict(smallModel, examples[1].Text, Signals())[0].Decision == null, "Only five supporting training cases remain uncertain even with perfect test accuracy");
        var noTests = ExamDecisionTree.Train(examples.Take(40).ToArray());
        check(noTests.Exams[0].TestCount == 0 && !ExamDecisionTree.Predict(noTests, examples[1].Text, Signals())[0].Decision.HasValue, "Training accuracy cannot substitute for held-out patients");
        var empty = ExamDecisionTree.Train(Array.Empty<ExamExample>());
        check(empty.Exams.All(e => e.Root == null), "Empty collection yields no trained model");
        Reject(() => ExamDecisionTree.Train(examples.Append(examples[0]).ToArray()), "Duplicate patient cannot enter both splits");
        Reject(() => ExamDecisionTree.Train(Enumerable.Repeat(examples[0], 1001).ToArray()), "Bounded training dataset");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { ExamDecisionTree.Train(examples, cancel.Token); check(false, "Cancelled training stops"); }
            catch (OperationCanceledException) { check(true, "Cancelled training stops"); }
        }

        var changed = Example(1, false, true);
        var oldGroup = changed.Group; changed.Text += " Nouvelle consultation.";
        check(!ExamLearningData.IsTest(changed.Group) && oldGroup == changed.Group, "Patient partition survives changes to the clinical text");
        changed.Annotations[0] = new(0, false, "Contrôle rétinien demandé");
        Reject(() => ExamLearningData.Validate(changed), "Annotation label must match the human answer");
        changed.Annotations[0] = new(0, true, "Passage absent");
        Reject(() => ExamLearningData.Validate(changed), "Annotation must be an exact part of the displayed source");
        changed.Annotations.Clear(); changed.AppointmentDay = default;
        Reject(() => ExamLearningData.Validate(changed), "Review must identify its appointment date");
        check(ExamLearningData.Digest("RAS", Signals(), day) != ExamLearningData.Digest("RAS", Signals(), day.AddDays(1)), "Another appointment day invalidates manual reuse");
        check(ExamLearningData.TextFeatures("Contrôle demandé").Keys.ToHashSet().SetEquals(ExamLearningData.TextFeatures("CONTROLE DEMANDE").Keys), "Accents and case do not defeat learned words");
        check(!ExamLearningData.TextFeatures("dans 6 mois").Keys.ToHashSet().SetEquals(ExamLearningData.TextFeatures("dans 1 mois").Keys), "Annotated numeric intervals remain distinct");
        check(ExamLearningData.TextFeatures("V3F BZQK001").Values.Contains("V3F") && ExamLearningData.TextFeatures("V3F BZQK001").Values.Contains("BZQK001"), "Exam names and act codes stay complete tokens");

        string Mutate(Action<JsonObject> mutation) { var obj = JsonNode.Parse(json)!.AsObject(); mutation(obj); return obj.ToJsonString(); }
        Reject(() => ExamDecisionTree.Import("{}"), "Missing model metadata is rejected");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o.Remove("Version"))), "Missing version is not silently defaulted");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Version"] = 99)), "Incompatible model version");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["FeatureVersion"] = "other")), "Incompatible feature extraction version");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["UnexpectedClinicalText"] = "private")), "Unknown JSON properties cannot smuggle extra content into an export");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Exams"]![0]!["Questions"] = 999)), "Question count cannot exceed test set");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Exams"]![0]!["Root"]!["Feature"] = "S999")), "Unknown structural feature rejected");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Exams"]![0]!["Root"]!["Feature"] = "raw medical text")), "Text criteria must be fixed hashes");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Exams"]![0]!["Root"]!["Yes"] = 999)), "Inconsistent parent and child counts rejected");
        Reject(() => ExamDecisionTree.Import(Mutate(o => o["Exams"]![0]!["Root"] = null)), "Test scores require an actual tree");
        Reject(() => ExamDecisionTree.Import(new string(' ', 256001)), "Oversized JSON import rejected");
        var cyclic = ExamDecisionTree.Import(json); cyclic.Exams[0].Root!.Present = cyclic.Exams[0].Root;
        Reject(() => ExamDecisionTree.Export(cyclic), "Cycles fail validation without recursing indefinitely");
        Reject(() => ExamDecisionTree.Predict(model, new string('a', ExamLearningData.MaxText + 1), Signals()), "Oversized inference input rejected");

        var visits = new[]
        {
            new FoConsultation("old", day.AddDays(-4), "Note ancienne ignorée pour les mots", ""),
            new FoConsultation("three", day.AddDays(-3), "Observation numéro trois", ""),
            new FoConsultation("two", day.AddDays(-2), "Observation numéro deux", ""),
            new FoConsultation("latest", day.AddDays(-1), "Prévoir OCT. Prévoir Skiacol.", "Refaire V3FO", "BZQK001", "synthetic", true),
            new FoConsultation("future", day.AddDays(1), "Note du futur", "")
        };
        var extracted = ExamLearningData.Extract(visits, day, day.AddDays(10));
        check(!extracted.Text.Contains("Note ancienne") && !extracted.Text.Contains("Note du futur") && extracted.Text.Contains("Observation numéro trois"), "Text training uses the latest three available visits only");
        check(extracted.Text.IndexOf("Prévoir OCT", StringComparison.Ordinal) < extracted.Text.IndexOf("Observation numéro deux", StringComparison.Ordinal), "Latest source is displayed first");
        check(extracted.Signals[6] && extracted.Signals[7] && extracted.Signals[8] && extracted.Signals[9] && extracted.Signals[10], "Structured features use the confirmed local act and latest exam requests");
        var negated = ExamLearningData.Extract(new[] { new FoConsultation("negated", day.AddDays(-1), "Ne pas prévoir OCT. Skiacol non nécessaire.", "Pas de V3FO à faire") }, day, day);
        check(!negated.Signals[8] && !negated.Signals[9] && !negated.Signals[10], "Negation does not become a positive request feature");
        var contradictoryVisits = new[]
        {
            new[] { new FoConsultation("fields", day.AddDays(-1), "Pas de FO", "Prévoir FO") },
            new[] { new FoConsultation("dates", day.AddDays(-1), "Refaire FO dans 6 mois", "Refaire FO dans 1 an") },
            new[] { new FoConsultation("code", day.AddDays(-1), "FO non réalisé", "", "BZQK001", "act", true) },
            new[] { new FoConsultation("tie-one", day.AddDays(-1), "Prévoir FO"), new FoConsultation("tie-two", day.AddDays(-1), "FO réalisé") }
        };
        foreach (var conflictVisits in contradictoryVisits)
        {
            var conflictInput = ExamLearningData.Extract(conflictVisits, day, day);
            check(FundusAnalysis.Analyze(conflictVisits, day)?.HasConflict == true && conflictInput.Signals[15], "Structured contradiction propagates regardless of display wording");
            check(ExamDecisionTree.Predict(model, examples[1].Text, conflictInput.Signals)[0].Decision == null, "Recognized clinical conflict vetoes an otherwise supported FO decision");
        }
        var resolved = contradictoryVisits[0].Append(new FoConsultation("new-plan", day, "Prévoir FO"));
        check(!ExamLearningData.Extract(resolved, day, day).Signals[15], "A later unambiguous explicit plan can resolve an old conflict");
        var completed = contradictoryVisits[0].Append(new FoConsultation("done", day, "FO réalisé"));
        check(!ExamLearningData.Extract(completed, day, day).Signals[15], "A later unambiguous completion can resolve an old conflict");
        Reject(() => ExamLearningData.Extract(new[] { new FoConsultation("huge", day.AddDays(-1), new string('a', ExamLearningData.MaxText)) }, day, day), "Oversized notes are rejected rather than silently truncated");
    }
}
