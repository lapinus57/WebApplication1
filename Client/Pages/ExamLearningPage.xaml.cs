using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Fundus;
using Client.Helpers;
using EyeChat.Data;
using EyeChat.Fundus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Client.Pages;

public sealed partial class ExamLearningPage : Page
{
    private readonly FoPageLifetime _lifetime = new();
    private ExamLearningStore? _store;
    private FoAccessData? _reader;
    private FoMapping? _mapping;
    private string _source = "";
    private List<PatientRow> _rows = new();
    private PatientRow? _selected;
    private ExamExample? _draft;
    private bool _ready;
    private ComboBox[] Answers => new[] { FoAnswer, OctAnswer, V3Answer, SkiAnswer };
    public sealed record PatientRow(string Id, string Name, DateTime Appointment, string Group, string Digest, string Classification)
    {
        public string AppointmentText => Appointment.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
    }
    private static DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).Date;

    public ExamLearningPage()
    {
        InitializeComponent();
        foreach (var box in Answers) { box.ItemsSource = new[] { "Non renseigné", "Oui", "Non" }; box.SelectedIndex = 0; }
        EvidenceExam.ItemsSource = ExamLearningData.Exams; EvidenceExam.SelectedIndex = 0;
        FromDate.Date = new DateTimeOffset(Today); ToDate.Date = new DateTimeOffset(Today.AddDays(6));
        _ready = true;
        Loaded += async (_, _) =>
        {
            if (!_lifetime.Open()) return;
            await RunAsync(async op => { var store = await Task.Run(ExamLearningStore.Load, op.Token); if (op.IsCurrent) { _store = store; RefreshReport(); } });
        };
        Unloaded += (_, _) =>
        {
            _lifetime.Close(); _rows.Clear(); _selected = null; _draft = null;
            PatientsList.ItemsSource = null; ConsultationText.Text = EvidenceText.Text = ExplanationText.Text = RationaleText.Text = HistoryRationaleText.Text = HistoryText.Text = ClinicalSummary.Text = "";
        };
    }

    private void Message(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }

    private async Task RunAsync(Func<FoPageLifetime.Operation, Task> work)
    {
        using var op = _lifetime.TryStart(); if (op == null) return;
        Actions.IsEnabled = PatientsList.IsEnabled = FilterText.IsEnabled = ReviewPanel.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await work(op); }
        catch (OperationCanceledException) { }
        catch (FoDataException ex) { if (op.IsCurrent) Message(ex.Message, InfoBarSeverity.Warning); }
        catch { if (op.IsCurrent) Message("L’opération n’a pas abouti. Vérifiez l’accès au fichier ou à Access. Aucun détail médical n’est journalisé.", InfoBarSeverity.Error); }
        finally
        {
            if (op.IsCurrent)
            {
                Actions.IsEnabled = PatientsList.IsEnabled = FilterText.IsEnabled = true;
                ReviewPanel.IsEnabled = _draft != null; Progress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private ExamLearningStore Store => _store ?? throw new FoDataException("Le jeu local n’est pas disponible. Fermez puis rouvrez cette page.");
    private ExamLearningStore CopyStore() => new()
    { Salt = Store.Salt, SourceKey = Store.SourceKey, Examples = Store.Examples.ToList(), Candidate = Store.Candidate, Active = Store.Active };
    private async Task SaveStoreAsync(ExamLearningStore store, FoPageLifetime.Operation op)
    { await Task.Run(store.Save, op.Token); if (op.IsCurrent) { _store = store; RefreshReport(); } }

    private void RefreshReport()
    {
        int tests = Store.Examples.Count(e => ExamLearningData.IsTest(e.Group));
        SummaryText.Text = $"{Store.Examples.Count - tests} patient(s) d’entraînement · {tests} patient(s) de test · " +
            (Store.Active == null ? "classement désactivé" : $"modèle actif {Store.Active.Id:N}");
        ModelReport.Text = ExamDecisionTree.Report(Store.Candidate);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FundusPage));
    private async void Load_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadPatientsAsync);
    private async Task LoadPatientsAsync(FoPageLifetime.Operation op)
    {
        var from = FromDate.Date?.Date ?? Today; var until = (ToDate.Date?.Date ?? from).AddDays(1);
        if (from < Today || until <= from || (until - from).TotalDays > 93) throw new FoDataException("Choisissez une période de 1 à 93 jours à partir d’aujourd’hui.");
        var config = await App.ChatService.GetAppointmentSearchConfigAsync().WaitAsync(op.Token);
        if (config == null || string.IsNullOrWhiteSpace(config.DatabasePath)) throw new FoDataException("Configurez le chemin Access dans les paramètres Recherche RDV.");
        var machine = MachineConfig.Load(); var store = Store;
        var result = await Task.Run(() =>
        {
            op.Token.ThrowIfCancellationRequested();
            var reader = new FoAccessData(new AccessReadOptions(config.DatabasePath, machine.AccessOleDbProvider, machine.AccessWorkgroupPath, machine.AccessUserName, machine.AccessPassword));
            var schema = reader.Inspect();
            var map = FoLocalSettings.Load(config.DatabasePath).Mapping ?? throw new FoDataException("Effectuez d’abord une recherche réussie dans Préparation FO pour enregistrer les champs de cette base.");
            map.SelectClinicalFields(schema); map.Validate(schema);
            var source = reader.RegistrySourceKey(map);
            var data = reader.Load(map, from, until, op.Token);
            var rows = new List<PatientRow>();
            foreach (var patient in data.Patients)
            {
                op.Token.ThrowIfCancellationRequested();
                var input = ExamLearningData.Extract(patient.Consultations, reader.Now, patient.Appointment);
                var group = store.Group(source, patient.Id);
                var digest = ExamLearningData.Digest(input.Text, input.Signals, patient.Appointment);
                var predictions = ExamReviewContext.ApplyMyopia(patient.Consultations, reader.Now, patient.Appointment, ExamDecisionTree.Predict(store.Active, input.Text, input.Signals));
                rows.Add(new(patient.Id, patient.Name, patient.Appointment, group, digest, Classification(predictions, store.Examples.FirstOrDefault(e => e.Group == group), digest)));
            }
            return (Reader: reader, Map: map, Source: source, Rows: rows.OrderBy(r => r.Appointment).ThenBy(r => r.Name).ToList());
        }, op.Token);
        if (!op.IsCurrent) return;
        _reader = result.Reader; _mapping = result.Map; _source = result.Source; _rows = result.Rows;
        _selected = null; _draft = null; ConsultationText.Text = EvidenceText.Text = ExplanationText.Text = RationaleText.Text = HistoryRationaleText.Text = HistoryText.Text = ClinicalSummary.Text = "";
        ApplyFilter();
        Message($"{_rows.Count} patient(s) chargé(s), y compris ceux sans alerte FO. Sélectionnez un dossier pour répondre et annoter les passages.", InfoBarSeverity.Success);
    }

    private static string Classification(ExamPrediction[] predictions, ExamExample? reviewed, string digest)
    {
        bool current = reviewed != null && ExamLearningData.Digest(reviewed.Text, reviewed.Signals, reviewed.AppointmentDay) == digest;
        return string.Join(" · ", predictions.Select((p, i) =>
        {
            var manual = current ? reviewed!.Answers[i] : null;
            return ExamLearningData.Exams[i] + " : " + (manual.HasValue ? (manual.Value ? "Oui" : "Non") + " (relu)" : p.Decision.HasValue ? (p.Decision.Value ? "Oui" : "Non") : "À confirmer");
        }));
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (_ready) ApplyFilter(); }
    private void ApplyFilter()
    {
        var query = FundusAnalysis.Normalize(FilterText.Text.Trim());
        PatientsList.ItemsSource = _rows.Where(r => query.Length == 0 || FundusAnalysis.Normalize(r.Name + " " + r.Id + " " + r.Classification).Contains(query)).ToList();
    }

    private async void Patient_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PatientsList.SelectedItem is not PatientRow row || _reader == null || _mapping == null) return;
        await RunAsync(async op =>
        {
            var visits = await Task.Run(() => _reader.ReadPatientTimeline(_mapping, row.Id, op.Token), op.Token);
            var input = await Task.Run(() => ExamLearningData.Extract(visits, _reader.Now, row.Appointment), op.Token);
            if (!op.IsCurrent) return;
            _selected = row;
            var old = Store.Examples.FirstOrDefault(s => s.Group == row.Group);
            bool unchanged = old != null && ExamLearningData.Digest(old.Text, old.Signals, old.AppointmentDay) == ExamLearningData.Digest(input.Text, input.Signals, row.Appointment);
            _draft = new ExamExample { SourceVersion = 2, Group = row.Group, Text = input.Text, Signals = input.Signals, AppointmentDay = row.Appointment.Date,
                Answers = unchanged ? old!.Answers.ToArray() : new bool?[4], Rationale = unchanged ? old!.Rationale : "", HistoricalRationale = old?.HistoricalRationale ?? "", Annotations = unchanged ? old!.Annotations.ToList() : new() };
            PatientTitle.Text = row.Name + " · " + row.AppointmentText;
            ConsultationText.Text = input.Text;
            RationaleText.Text = _draft.Rationale; HistoryRationaleText.Text = _draft.HistoricalRationale;
            var birth = await Task.Run(() => _reader.ReadBirthDate(_mapping, row.Id), op.Token);
            if (!op.IsCurrent) return;
            ClinicalSummary.Text = ExamReviewContext.Summary(visits, _reader.Now, row.Appointment, birth);
            HistoryText.Text = ExamReviewContext.History(visits, _reader.Now);
            for (int i = 0; i < 4; i++) Answers[i].SelectedIndex = _draft.Answers[i] == true ? 1 : _draft.Answers[i] == false ? 2 : 0;
            RefreshEvidence();
            var predictions = await Task.Run(() => ExamReviewContext.ApplyMyopia(visits, _reader.Now, row.Appointment, ExamDecisionTree.Predict(Store.Active, input.Text, input.Signals)), op.Token);
            if (!op.IsCurrent) return;
            ExplanationText.Text = string.Join("\n\n", predictions.Select((p, i) => ExamLearningData.Exams[i] + " — " + (p.Decision.HasValue ? p.Decision.Value ? "Oui" : "Non" : "À confirmer") + "\n" + p.Explanation));
            Message(ExamLearningData.IsTest(row.Group) ? "Dossier réservé aux tests : ses réponses et extraits ne servent jamais à construire l’arbre." : "Dossier d’entraînement : répondez d’après votre relecture, puis sélectionnez les passages utiles.");
        });
    }

    private void AddHistory_Click(object sender, RoutedEventArgs e)
    {
        var selected = HistoryText.SelectedText;
        if (string.IsNullOrWhiteSpace(selected)) { Message("Sélectionnez un passage dans l’historique."); return; }
        if (HistoryRationaleText.Text.Length + selected.Length + 8 > 12000) { Message("Limite de 12 000 caractères atteinte."); return; }
        HistoryRationaleText.Text += "\n« " + selected + " »\n";
    }

    private bool? Answer(int exam) => Answers[exam == 1 ? 0 : exam].SelectedIndex switch { 1 => true, 2 => false, _ => null };
    private void AddEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        int exam = EvidenceExam.SelectedIndex; var answer = Answer(exam); var text = ConsultationText.SelectedText;
        if (!answer.HasValue) { Message("Choisissez Oui ou Non pour cet examen avant d’ajouter son explication.", InfoBarSeverity.Warning); return; }
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1200) { Message("Sélectionnez un passage de 1 à 1 200 caractères dans la consultation.", InfoBarSeverity.Warning); return; }
        if (_draft.Annotations.Count >= 24) { Message("Limite de 24 extraits par dossier atteinte.", InfoBarSeverity.Warning); return; }
        _draft.Annotations.Add(new(exam, answer.Value, text)); RefreshEvidence();
    }
    private void ClearEvidence_Click(object sender, RoutedEventArgs e) { _draft?.Annotations.Clear(); RefreshEvidence(); }
    private void RefreshEvidence() => EvidenceText.Text = _draft == null ? "" : string.Join("\n", _draft.Annotations.Select(a => ExamLearningData.Exams[a.Exam] + " — " + (a.Yes ? "Oui" : "Non") + " : « " + a.Text + " »"));

    private async void SaveAnswers_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        if (_draft == null || _selected == null || _reader == null || _mapping == null) return;
        if (Store.Examples.Count > 0 && Store.SourceKey != _source) throw new FoDataException("Ce jeu d’apprentissage appartient à une autre base. Les modèles peuvent être utilisés ici, mais les dossiers de deux bases ne sont pas mélangés.");
        var answers = Enumerable.Range(0, 4).Select(Answer).ToArray();
        if (answers.All(a => !a.HasValue)) throw new FoDataException("Renseignez au moins une réponse Oui ou Non. Les autres examens peuvent rester non renseignés.");
        var example = new ExamExample { SourceVersion = 2, Group = _draft.Group, Text = _draft.Text, Signals = _draft.Signals.ToArray(), AppointmentDay = _draft.AppointmentDay, Answers = answers, Rationale = RationaleText.Text, HistoricalRationale = HistoryRationaleText.Text, Annotations = _draft.Annotations.ToList() };
        ExamLearningData.Validate(example);
        var visits = await Task.Run(() => _reader.ReadPatientTimeline(_mapping, _selected.Id, op.Token), op.Token);
        var fresh = await Task.Run(() => ExamLearningData.Extract(visits, _reader.Now, _selected.Appointment), op.Token);
        if (ExamLearningData.Digest(fresh.Text, fresh.Signals, _selected.Appointment) != ExamLearningData.Digest(example.Text, example.Signals, example.AppointmentDay))
            throw new FoDataException("Le dossier a changé pendant la relecture. Resélectionnez le patient avant d’enregistrer.");
        var store = CopyStore(); store.SourceKey = _source; store.Examples.RemoveAll(s => s.Group == example.Group); store.Examples.Add(example); store.Candidate = null;
        await SaveStoreAsync(store, op); if (!op.IsCurrent) return;
        // The editable draft must not mutate the already saved training collection.
        _draft = new ExamExample { SourceVersion = 2, Group = example.Group, Text = example.Text, AppointmentDay = example.AppointmentDay,
            Signals = example.Signals.ToArray(), Rationale = example.Rationale, HistoricalRationale = example.HistoricalRationale, Answers = example.Answers.ToArray(), Annotations = example.Annotations.ToList() };
        var row = _selected with { Classification = Classification(ExamDecisionTree.Predict(Store.Active, example.Text, example.Signals), example, ExamLearningData.Digest(example.Text, example.Signals, example.AppointmentDay)) };
        _selected = row;
        _rows = _rows.Select(r => r.Group == row.Group ? row : r).ToList(); ApplyFilter();
        Message("Réponses et passages enregistrés dans votre profil Windows chiffré. Entraînez un nouveau modèle pour intégrer ces corrections.", InfoBarSeverity.Success);
    });

    private async void Train_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var examples = Store.Examples.ToArray();
        Message("Entraînement local et évaluation sur les patients réservés aux tests…");
        var model = await Task.Run(() => ExamDecisionTree.Train(examples, op.Token), op.Token);
        if (!op.IsCurrent) return;
        var store = CopyStore(); store.Candidate = model; await SaveStoreAsync(store, op);
        if (op.IsCurrent) Message("Entraînement terminé. Consultez les faux Oui et faux Non dans les résultats avant d’activer ou d’exporter le modèle.", InfoBarSeverity.Success);
    });
    private async void Activate_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var model = Store.Candidate ?? throw new FoDataException("Entraînez ou importez d’abord un modèle.");
        if (model.Exams.All(t => t.Root == null)) throw new FoDataException("Aucun examen n’a assez d’exemples pour construire un arbre.");
        var store = CopyStore(); store.Active = model; await SaveStoreAsync(store, op);
        if (op.IsCurrent) { _rows.Clear(); _draft = null; _selected = null; ApplyFilter(); ConsultationText.Text = EvidenceText.Text = ExplanationText.Text = RationaleText.Text = HistoryRationaleText.Text = HistoryText.Text = ClinicalSummary.Text = ""; Message("Modèle activé sur ce poste. Rechargez les patients pour actualiser le classement ; les cas insuffisamment étayés restent À confirmer.", InfoBarSeverity.Success); }
    });
    private async void Disable_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var store = CopyStore(); store.Active = null; await SaveStoreAsync(store, op);
        if (op.IsCurrent) { _rows.Clear(); _draft = null; _selected = null; ApplyFilter(); ConsultationText.Text = EvidenceText.Text = ExplanationText.Text = RationaleText.Text = HistoryRationaleText.Text = HistoryText.Text = ClinicalSummary.Text = ""; Message("Classement désactivé. Les exemples locaux sont conservés."); }
    });
    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var model = Store.Candidate ?? throw new FoDataException("Entraînez ou importez d’abord un modèle.");
        var json = ExamDecisionTree.Export(model);
        var picker = new FileSavePicker { SuggestedFileName = "EyeChat-examens-" + model.Id.ToString("N") };
        picker.FileTypeChoices.Add("Modèle local JSON", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync(); if (file == null || !op.IsCurrent) return;
        await File.WriteAllTextAsync(file.Path, json, new UTF8Encoding(false), op.Token);
        if (op.IsCurrent) Message("Arbres et résultats de tests exportés. Le fichier ne contient ni comptes rendus, ni extraits, ni identifiants de patients.", InfoBarSeverity.Success);
    });
    private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(async op =>
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync(); if (file == null || !op.IsCurrent) return;
        if (new FileInfo(file.Path).Length > 256_000) throw new FoDataException("Le fichier de modèle est trop volumineux.");
        var model = ExamDecisionTree.Import(await File.ReadAllTextAsync(file.Path, op.Token));
        var store = CopyStore(); store.Candidate = model; await SaveStoreAsync(store, op);
        if (op.IsCurrent) Message("Modèle importé pour examen. Consultez ses résultats, puis activez le classement sur ce poste.", InfoBarSeverity.Success);
    });
}

