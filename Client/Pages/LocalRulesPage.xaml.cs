using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Fundus;
using EyeChat.Fundus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Client.Pages;

public sealed partial class LocalRulesPage : Page
{
    private readonly FoPageLifetime _lifetime = new();
    private FoPageLifetime.Operation? _operation;
    private LocalRulePackage? _package;
    private Guid? _editing;
    private bool _hasProposal;
    private bool _loaded;
    private bool _interpreting;
    public LocalRulesPage()
    {
        InitializeComponent();
        ConditionBox.ItemsSource = new[] { "Diabète documenté", "HTO documentée", "Myopie : seuil de sphère" };
        ExamBox.ItemsSource = new[] { "FO / OCT", "V3F", "Skiacol" };
        Loaded += async (_, _) =>
        {
            if (!_lifetime.Open()) return;
            _loaded = true;
            await RunAsync(async op => { var package = await Task.Run(LocalRuleStore.Load, op.Token); if (op.IsCurrent) { _package = package; Refresh(); } });
        };
        Unloaded += (_, _) => { _loaded = false; _lifetime.Close(); InstructionText.Text = QuestionText.Text = ""; _package = null; RulesList.ItemsSource = null; };
    }
    private void Message(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    { StatusBar.Message = text; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
    private async Task RunAsync(Func<FoPageLifetime.Operation, Task> work)
    {
        using var op = _lifetime.TryStart(); if (op == null) return;
        _operation = op; Form.IsEnabled = false; Progress.Visibility = Visibility.Visible;
        try { await work(op); }
        catch (OperationCanceledException) { if (op.IsCurrent) Message("Analyse annulée. Aucune proposition n’a été activée."); }
        catch (FoDataException ex) { if (op.IsCurrent) Message(ex.Message, InfoBarSeverity.Warning); }
        catch { if (op.IsCurrent) Message("L’opération a échoué. Aucun détail de la consigne n’est journalisé.", InfoBarSeverity.Error); }
        finally
        {
            // Cancellation keeps the page open; do not inspect native window state after unloading.
            if (ReferenceEquals(_operation, op)) { _operation = null; if (_loaded) { Form.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; } }
        }
    }
    private LocalRulePackage Package => _package ?? throw new FoDataException("Les règles locales n’ont pas pu être chargées. Rouvrez cette page.");
    private void Refresh() => RulesList.ItemsSource = Package.Rules.ToList();
    private void Back_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ExamLearningPage));
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (_interpreting) { _operation?.Cancel(); Message("Annulation demandée."); } }
    private void Instruction_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _editing = null; _hasProposal = false; ProposalForm.IsEnabled = false;
        QuestionText.Text = "Relancez l’interprétation après modification de la consigne.";
    }
    private void ShowProposal(string condition, string exam, decimal? threshold, int months)
    {
        ConditionBox.SelectedIndex = Array.IndexOf(LocalExamRules.Conditions, condition);
        ExamBox.SelectedIndex = Array.IndexOf(LocalExamRules.Exams, exam);
        SphereBox.Value = threshold.HasValue ? (double)threshold.Value : double.NaN;
        MonthsBox.Value = months; _hasProposal = true; ProposalForm.IsEnabled = true;
    }
    private async void Interpret_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        _ = Package;
        _editing = null; _hasProposal = false; ProposalForm.IsEnabled = false; QuestionText.Text = "";
        Message("Ollama interprète la consigne sur ce poste…");
        using var interpreter = new OllamaRuleInterpreter();
        LocalRuleProposal proposal;
        _interpreting = true;
        try { proposal = await interpreter.InterpretAsync(InstructionText.Text, op.Token); }
        finally { _interpreting = false; }
        if (!op.IsCurrent) return;
        if (proposal.NeedsClarification)
        {
            QuestionText.Text = proposal.Question;
            Message("Précisez ou corrigez votre consigne, puis relancez l’interprétation. Aucune règle n’a été activée.");
        }
        else
        {
            ShowProposal(proposal.Condition!, proposal.Exam!, proposal.SphereLessThan, proposal.IntervalMonths!.Value);
            Message("Proposition reçue : vérifiez le critère, le signe du seuil, l’examen et le délai avant validation.");
        }
    });
    private void Rule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RulesList.SelectedItem is not LocalExamRule rule) return;
        InstructionText.Text = "";
        _editing = rule.Id; ShowProposal(rule.Condition, rule.Exam, rule.SphereLessThan, rule.IntervalMonths);
        QuestionText.Text = "Règle sélectionnée : vous pouvez corriger ses valeurs, puis valider et activer.";
    }
    private async Task SaveAsync(LocalRulePackage package, FoPageLifetime.Operation op)
    {
        await Task.Run(() => LocalRuleStore.Save(package), op.Token);
        if (op.IsCurrent) { _package = package; Refresh(); }
    }
    private async void Validate_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        if (!_hasProposal || ConditionBox.SelectedIndex < 0 || ExamBox.SelectedIndex < 0) throw new FoDataException("Relisez d’abord une proposition.");
        if (!double.IsFinite(MonthsBox.Value) || MonthsBox.Value != Math.Truncate(MonthsBox.Value)) throw new FoDataException("Le délai doit être un nombre entier de mois.");
        var condition = LocalExamRules.Conditions[ConditionBox.SelectedIndex];
        decimal? threshold = condition == "MYOPIA" && double.IsFinite(SphereBox.Value) ? (decimal)SphereBox.Value : null;
        var rule = new LocalExamRule(_editing ?? Guid.NewGuid(), condition, LocalExamRules.Exams[ExamBox.SelectedIndex], threshold, checked((int)MonthsBox.Value), DateTime.UtcNow, true);
        LocalExamRules.Validate(rule);
        var next = new LocalRulePackage { Rules = Package.Rules.Where(r => r.Id != rule.Id).Append(rule).ToList() };
        await SaveAsync(next, op);
        if (op.IsCurrent) { _editing = rule.Id; Message("Règle validée et active sur ce poste. Rechargez les patients dans l’apprentissage.", InfoBarSeverity.Success); }
    });
    private async void Disable_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        if (RulesList.SelectedItem is not LocalExamRule selected) throw new FoDataException("Sélectionnez une règle.");
        await SaveAsync(new LocalRulePackage { Rules = Package.Rules.Select(r => r.Id == selected.Id ? r with { Enabled = false } : r).ToList() }, op);
        if (op.IsCurrent) Message("Règle désactivée. Rechargez les patients pour actualiser le classement.");
    });
    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        if (!Package.Rules.Any(r => r.Enabled)) throw new FoDataException("Aucune règle active à exporter.");
        var json = LocalExamRules.Export(Package);
        var picker = new FileSavePicker { SuggestedFileName = "EyeChat-regles-validees" };
        picker.FileTypeChoices.Add("Règles JSON", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync(); if (file == null || !op.IsCurrent) return;
        await File.WriteAllTextAsync(file.Path, json, new UTF8Encoding(false), op.Token);
        if (op.IsCurrent) Message("Règles exportées, sans consignes libres ni dossiers. Elles devront être relues et activées sur l’autre poste.", InfoBarSeverity.Success);
    });
    private async void Delete_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        if (RulesList.SelectedItem is not LocalExamRule selected) throw new FoDataException("Sélectionnez une règle.");
        await SaveAsync(new LocalRulePackage { Rules = Package.Rules.Where(r => r.Id != selected.Id).ToList() }, op);
        if (op.IsCurrent) { _editing = null; _hasProposal = false; ProposalForm.IsEnabled = false; Message("Règle supprimée de ce poste. Rechargez les patients pour actualiser le classement."); }
    });
    private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync(); if (file == null || !op.IsCurrent) return;
        if (new FileInfo(file.Path).Length > 64000) throw new FoDataException("Le fichier dépasse 64 Ko.");
        var imported = LocalExamRules.Import(await File.ReadAllTextAsync(file.Path, op.Token));
        await SaveAsync(new LocalRulePackage { Rules = Package.Rules.Concat(imported.Rules).ToList() }, op);
        if (op.IsCurrent) { _editing = null; _hasProposal = false; ProposalForm.IsEnabled = false; Message("Règles importées inactives. Sélectionnez chaque règle, relisez ses valeurs puis validez son activation.", InfoBarSeverity.Success); }
    });
}
