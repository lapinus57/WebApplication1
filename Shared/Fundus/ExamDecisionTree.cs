using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace EyeChat.Fundus;

public sealed class ExamTreeNode
{
    public string? Feature { get; set; }
    public string? SupportingFeature { get; set; }
    public int Yes { get; set; }
    public int No { get; set; }
    public ExamTreeNode? Present { get; set; }
    public ExamTreeNode? Absent { get; set; }
}
public sealed class ExamTreeResult
{
    public ExamTreeNode? Root { get; set; }
    public int TruePositive { get; set; }
    public int TrueNegative { get; set; }
    public int FalsePositive { get; set; }
    public int FalseNegative { get; set; }
    public int Questions { get; set; }
    [JsonIgnore] public int TestCount => TruePositive + TrueNegative + FalsePositive + FalseNegative;
    [JsonIgnore] public bool Tested => TruePositive + FalseNegative >= 5 && TrueNegative + FalsePositive >= 5 &&
        (double)TruePositive / (TruePositive + FalseNegative) >= .9 && (double)TrueNegative / (TrueNegative + FalsePositive) >= .9;
}
public sealed class ExamTreeModel
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string FeatureVersion { get; set; } = "exam-local-v1";
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public DateTime TrainedUtc { get; set; } = DateTime.UtcNow;
    [JsonRequired] public ExamTreeResult[] Exams { get; set; } = Array.Empty<ExamTreeResult>();
}
public sealed record ExamPrediction(bool? Decision, bool? Suggested, int SupportingExamples, double Agreement,
    string Explanation, IReadOnlyList<string> Excerpts);

public static class ExamDecisionTree
{
    private const int MinLeaf = 5;
    private const int MaxDepth = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, MaxDepth = 24, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Case(HashSet<string> Features, bool Yes);

    public static ExamTreeModel Train(IReadOnlyList<ExamExample> examples, CancellationToken token = default)
    {
        if (examples.Count > 1000) throw new FoDataException("Limite de 1 000 dossiers d’apprentissage atteinte.");
        foreach (var e in examples) ExamLearningData.Validate(e);
        if (examples.Select(e => e.Group).Distinct().Count() != examples.Count)
            throw new FoDataException("Un patient ne peut figurer qu’une fois dans le jeu d’apprentissage/test.");
        var model = new ExamTreeModel { Exams = new ExamTreeResult[4] };
        for (int exam = 0; exam < 4; exam++)
        {
            token.ThrowIfCancellationRequested();
            var train = examples.Where(e => !ExamLearningData.IsTest(e.Group) && e.Answers[exam].HasValue).ToList();
            // Vocabulary discovery sees only training annotations, never test patients or test annotations.
            var vocabulary = train.SelectMany(e =>
                {
                    token.ThrowIfCancellationRequested();
                    return e.Annotations.Where(a => a.Exam == exam)
                        .SelectMany(a => ExamLearningData.TextFeatures(a.Text).Keys).Distinct();
                })
                .GroupBy(k => k).Where(g => g.Count() >= MinLeaf).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(128).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            var features = Enumerable.Range(0, ExamLearningData.SignalNames.Length).Select(i => "S" + i.ToString(CultureInfo.InvariantCulture)).Concat(vocabulary).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var rows = new List<Case>();
            foreach (var e in train) { token.ThrowIfCancellationRequested(); rows.Add(new(ExamLearningData.Features(e, vocabulary), e.Answers[exam]!.Value)); }
            var result = new ExamTreeResult();
            if (rows.Count >= MinLeaf * 2)
            {
                result.Root = Build(rows, features, 0, token);
                // A leaf needs a present, outcome-associated cue, not only absent words.
                void Anchors(ExamTreeNode node, List<Case> local)
                {
                    if (node.Feature != null) { Anchors(node.Present!, local.Where(r => r.Features.Contains(node.Feature)).ToList()); Anchors(node.Absent!, local.Where(r => !r.Features.Contains(node.Feature)).ToList()); return; }
                    bool outcome = node.Yes >= node.No;
                    node.SupportingFeature = features.Where(k => k != "S0" && k != "S14")
                        .Select(k => (Key: k, Local: local.Count(r => r.Features.Contains(k)), Global: rows.Where(r => r.Features.Contains(k)).ToList()))
                        .Where(x => x.Local >= MinLeaf && x.Global.Count > 0 && (double)x.Global.Count(r => r.Yes == outcome) / x.Global.Count >= .9)
                        .OrderByDescending(x => x.Local).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key).FirstOrDefault();
                }
                Anchors(result.Root, rows);
            }
            foreach (var e in examples.Where(e => ExamLearningData.IsTest(e.Group) && e.Answers[exam].HasValue))
            {
                token.ThrowIfCancellationRequested();
                if (result.Root == null) continue;
                var leaf = Leaf(result.Root, ExamLearningData.Features(e, vocabulary));
                bool yes = leaf.Yes >= leaf.No, actual = e.Answers[exam]!.Value;
                if (yes && actual) result.TruePositive++;
                else if (yes) result.FalsePositive++;
                else if (actual) result.FalseNegative++;
                else result.TrueNegative++;
            }
            model.Exams[exam] = result;
        }
        foreach (var example in examples.Where(e => ExamLearningData.IsTest(e.Group)))
        {
            token.ThrowIfCancellationRequested();
            var predictions = Predict(model, example.Text, example.Signals);
            for (int i = 0; i < 4; i++) if (model.Exams[i].Root != null && example.Answers[i].HasValue && !predictions[i].Decision.HasValue) model.Exams[i].Questions++;
        }
        return model;
    }

    private static ExamTreeNode Build(List<Case> rows, string[] candidates, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var node = new ExamTreeNode { Yes = rows.Count(r => r.Yes), No = rows.Count(r => !r.Yes) };
        if (depth >= MaxDepth || node.Yes == 0 || node.No == 0 || rows.Count < MinLeaf * 2) return node;
        double Gini(int yes, int count) { double p = (double)yes / count; return 2 * p * (1 - p); }
        double bestGain = 1e-9;
        foreach (var key in candidates)
        {
            token.ThrowIfCancellationRequested();
            var yesRows = rows.Where(r => r.Features.Contains(key)).ToList();
            int present = yesRows.Count, absent = rows.Count - present;
            if (present < MinLeaf || absent < MinLeaf) continue;
            int positive = yesRows.Count(r => r.Yes);
            double gain = Gini(node.Yes, rows.Count) - (present * Gini(positive, present) + absent * Gini(node.Yes - positive, absent)) / rows.Count;
            if (gain > bestGain) { bestGain = gain; node.Feature = key; }
        }
        if (node.Feature != null)
        {
            var remaining = candidates.Where(k => k != node.Feature).ToArray();
            node.Present = Build(rows.Where(r => r.Features.Contains(node.Feature)).ToList(), remaining, depth + 1, token);
            node.Absent = Build(rows.Where(r => !r.Features.Contains(node.Feature)).ToList(), remaining, depth + 1, token);
        }
        return node;
    }

    private static ExamTreeNode Leaf(ExamTreeNode node, ISet<string> keys)
    {
        while (node.Feature != null) node = keys.Contains(node.Feature) ? node.Present! : node.Absent!;
        return node;
    }

    public static ExamPrediction[] Predict(ExamTreeModel? model, string text, bool[] signals)
    {
        if (signals.Length != ExamLearningData.SignalNames.Length || text.Length > ExamLearningData.MaxText)
            throw new FoDataException("Données incompatibles avec le modèle.");
        if (model == null) return Enumerable.Range(0, 4).Select(_ => new ExamPrediction(null, null, 0, 0, "Aucun modèle actif : réponse à renseigner.", Array.Empty<string>())).ToArray();
        Validate(model);
        var textual = new HashSet<string>(StringComparer.Ordinal);
        void Collect(ExamTreeNode? n) { if (n == null) return; if (n.SupportingFeature?.StartsWith('T') == true) textual.Add(n.SupportingFeature); if (n.Feature == null) return; if (n.Feature.StartsWith('T')) textual.Add(n.Feature); Collect(n.Present); Collect(n.Absent); }
        foreach (var tree in model.Exams) Collect(tree.Root);
        var matches = ExamLearningData.TextFeatures(text, textual);
        var keys = new HashSet<string>(matches.Keys, StringComparer.Ordinal);
        for (int i = 0; i < signals.Length; i++) if (signals[i]) keys.Add("S" + i.ToString(CultureInfo.InvariantCulture));
        return model.Exams.Select((tree, exam) =>
        {
            if (tree.Root == null) return new ExamPrediction(null, null, 0, 0, "Pas assez de réponses d’entraînement pour cet examen.", Array.Empty<string>());
            var node = tree.Root;
            var reasons = new List<string>(); var excerpts = new List<string>(); bool supportingSignal = false;
            while (node.Feature != null)
            {
                bool present = keys.Contains(node.Feature);
                if (node.Feature.StartsWith('S'))
                {
                    int index = int.Parse(node.Feature[1..], CultureInfo.InvariantCulture);
                    reasons.Add(ExamLearningData.SignalNames[index] + " : " + (present ? "oui" : "non"));
                }
                else if (present)
                {
                    excerpts.Add(matches[node.Feature]); reasons.Add("Passage retrouvé : « " + matches[node.Feature] + " »");
                }
                else reasons.Add("Un indice textuel appris n’est pas retrouvé.");
                node = present ? node.Present! : node.Absent!;
            }
            if (node.SupportingFeature is { } support && keys.Contains(support))
            {
                supportingSignal = true;
                if (support.StartsWith('T')) { excerpts.Add(matches[support]); reasons.Add("Indice associé à cette réponse : « " + matches[support] + " »"); }
                else reasons.Add("Indice associé à cette réponse : " + ExamLearningData.SignalNames[int.Parse(support[1..], CultureInfo.InvariantCulture)]);
            }
            int total = node.Yes + node.No; bool suggested = node.Yes >= node.No;
            double agreement = total == 0 ? 0 : (double)Math.Max(node.Yes, node.No) / total;
            bool ready = tree.Tested && total >= 10 && agreement >= .9 && supportingSignal && signals[0] && !(exam == 0 && signals[15]);
            string why = ready ? "Classement local" : !tree.Tested ? "Résultats de test encore insuffisants pour classer directement" : "Doute : réponse à confirmer";
            why += $". Accord dans la feuille : {Math.Max(node.Yes, node.No)}/{total} exemples (ce n’est pas une probabilité clinique).";
            return new ExamPrediction(ready ? suggested : null, suggested, total, agreement, why + "\n" + string.Join("\n", reasons), excerpts.Distinct().Take(4).ToArray());
        }).ToArray();
    }

    public static string Export(ExamTreeModel model) { Validate(model); return JsonSerializer.Serialize(model, JsonOptions); }
    public static ExamTreeModel Import(string json)
    {
        if (json.Length > 256_000) throw new FoDataException("Le fichier de modèle dépasse la taille autorisée.");
        try { var model = JsonSerializer.Deserialize<ExamTreeModel>(json, JsonOptions) ?? throw new JsonException(); Validate(model); return model; }
        catch (FoDataException) { throw; }
        catch { throw new FoDataException("Modèle invalide ou incompatible. Aucun modèle actif n’a été remplacé."); }
    }
    public static void Validate(ExamTreeModel model)
    {
        if (model.Version != 1 || model.FeatureVersion != "exam-local-v1" || model.Exams == null || model.Exams.Length != 4 || model.Id == Guid.Empty)
            throw new FoDataException("Version de modèle incompatible.");
        foreach (var tree in model.Exams)
        {
            if (tree == null || new[] { tree.TruePositive, tree.TrueNegative, tree.FalsePositive, tree.FalseNegative }.Any(n => n < 0 || n > 1000))
                throw new FoDataException("Résultats de test invalides.");
            if (tree.TestCount > 1000 || tree.Questions < 0 || tree.Questions > tree.TestCount || (tree.Root == null && tree.TestCount != 0)) throw new FoDataException("Effectifs de test invalides.");
            bool FeatureValid(string key)
            {
                bool signal = key.StartsWith('S') && int.TryParse(key[1..], out int index) && index >= 0 && index < ExamLearningData.SignalNames.Length && key == "S" + index.ToString(CultureInfo.InvariantCulture);
                return signal || Regex.IsMatch(key, @"\AT[A-F0-9]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            int count = 0;
            void Walk(ExamTreeNode? n, int depth)
            {
                if (n == null || ++count > 31 || depth > MaxDepth || n.Yes < 0 || n.No < 0 || n.Yes + (long)n.No > 1000 || n.Yes + n.No < 1)
                    throw new FoDataException("Arbre de décision invalide.");
                if (n.SupportingFeature != null && !FeatureValid(n.SupportingFeature)) throw new FoDataException("Indice de feuille invalide.");
                if (n.Feature == null) { if (n.Present != null || n.Absent != null) throw new FoDataException("Feuille invalide."); return; }
                if (!FeatureValid(n.Feature) || n.SupportingFeature != null) throw new FoDataException("Critère incompatible.");
                Walk(n.Present, depth + 1); Walk(n.Absent, depth + 1);
                if (n.Yes != n.Present!.Yes + n.Absent!.Yes || n.No != n.Present.No + n.Absent.No) throw new FoDataException("Effectifs de l’arbre incohérents.");
            }
            if (tree.Root != null) Walk(tree.Root, 0);
        }
    }

    public static string Report(ExamTreeModel? model)
    {
        if (model == null) return "Aucun modèle entraîné ou importé.";
        Validate(model);
        var lines = new List<string> { $"Modèle {model.Id:N} · entraîné le {model.TrainedUtc:u}", "Tests réservés par patient : jamais utilisés pour apprendre les critères ou l’arbre." };
        for (int i = 0; i < 4; i++)
        {
            var e = model.Exams[i];
            lines.Add($"{ExamLearningData.Exams[i]} : entraînement {e.Root?.Yes + e.Root?.No ?? 0} · tests {e.TestCount} · Oui corrects {e.TruePositive} · Non corrects {e.TrueNegative} · faux Oui {e.FalsePositive} · faux Non {e.FalseNegative}. " +
                $"Questions sur les tests : {e.Questions}. " + (e.Tested ? "Seuil de test atteint ; les cas peu étayés restent à confirmer." : "Collecter davantage de dossiers ou corriger les erreurs avant classement direct."));
        }
        lines.Add("Seuil technique par examen : au moins 5 Oui et 5 Non en test, sensibilité et spécificité observées ≥ 90 %. Ce seuil ne certifie pas la fiabilité clinique. Vérifier sur des dossiers représentatifs avant utilisation.");
        return string.Join("\n\n", lines);
    }
}
