using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Client.Fundus;
using Client.Helpers;
using EyeChat.Data;
using EyeChat.Fundus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages;

public sealed partial class FundusPage : Page
{
    private readonly FoPageLifetime _lifetime = new();
    private IReadOnlyList<FoPatientRow> _rows = Array.Empty<FoPatientRow>();
    private FoContext? _context;
    private string _sourceKey = "";
    private DateTime _searchedFrom, _searchedUntil;
    private bool _ready, _hasResults;
    private ContentDialog? _dialog;

    private sealed record FoContext(FoAccessData Reader, string Path, FoLocalSettings Settings, FoMapping Mapping, IReadOnlyList<FoField> Schema);

    public FundusPage()
    {
        InitializeComponent();
        AccessRuntimeText.Text = AccessRuntimeDiagnostics.ClientDescription + " · moteur Access de même architecture requis";
        _ready = true;
        UpdatePeriod();
        // Attach after XAML construction; repeated Loaded notifications must not reinitialize the page.
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_lifetime.Open()) return;
        // Loading/reopening the page must never activate ACE. A native Office dialog
        // can block connection.Open(), preventing any in-page diagnostic or cancellation.
        Busy(false);
        ApplyFilter();
        Message("Choisissez une période, puis cliquez sur Rechercher les patients.");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _lifetime.Close();
        _dialog?.Hide();
        _dialog = null;
        _context = null;
        _rows = Array.Empty<FoPatientRow>();
        PatientsList.ItemsSource = null;
        _hasResults = false;
    }

    private static DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).Date;

    private void UpdatePeriod()
    {
        if (!_ready) return;
        var custom = PeriodBox.SelectedIndex == 3;
        FromDate.IsEnabled = ToDate.IsEnabled = custom;
        if (custom) return;
        var from = Today.AddDays(PeriodBox.SelectedIndex == 1 ? 1 : 0);
        FromDate.Date = new DateTimeOffset(from);
        ToDate.Date = new DateTimeOffset(from.AddDays(PeriodBox.SelectedIndex == 2 ? 6 : 0));
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e) => UpdatePeriod();
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void State_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void Busy(bool busy)
    {
        foreach (var control in SearchControls.Children.OfType<Control>()) control.IsEnabled = !busy;
        if (!busy) FromDate.IsEnabled = ToDate.IsEnabled = PeriodBox.SelectedIndex == 3;
        SettingsButton.IsEnabled = AccessDiagnosticButton.IsEnabled = PatientsList.IsEnabled = !busy;
        LoadingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Message(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Message = text;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void ShowFailure(Exception ex)
    {
        // Never log provider errors or medical text; expected schema errors are safe to display.
        var message = ex is FoDataException ? ex.Message :
            "Impossible de lire les données FO. Vérifiez la connexion au serveur, le chemin Access des paramètres RDV et les droits du poste (fournisseur ACE et groupe de travail).";
        Message(message + " " + AccessRuntimeDiagnostics.ClientDescription + ". Le bouton Diagnostic Access permet de vérifier l’installation du moteur sans ouvrir la base." + (_hasResults ? " La liste précédente reste affichée ; elle n’a pas été actualisée." : ""), InfoBarSeverity.Error);
        if (!_hasResults) EmptyText.Text = "Recherche non aboutie. Consultez Diagnostic Access avant de relancer la recherche.";
    }

    private async Task<FoContext> ReadContextAsync(FoPageLifetime.Operation operation)
    {
        var config = await App.ChatService.GetAppointmentSearchConfigAsync().WaitAsync(operation.Token);
        operation.Token.ThrowIfCancellationRequested();
        if (config == null || string.IsNullOrWhiteSpace(config.DatabasePath))
            throw new FoDataException("Configurez le chemin de la base Access dans Paramètres → Recherche RDV.");
        var machine = MachineConfig.Load();
        return await Task.Run(() =>
        {
            operation.Token.ThrowIfCancellationRequested();
            if (!File.Exists(config.DatabasePath))
                throw new FoDataException("La base Access des paramètres RDV n’est pas accessible depuis ce poste. Vérifiez le chemin et les droits de lecture Windows.");
            var reader = new FoAccessData(new AccessReadOptions(config.DatabasePath, machine.AccessOleDbProvider,
                machine.AccessWorkgroupPath, machine.AccessUserName, machine.AccessPassword));
            var schema = reader.Inspect();
            var settings = FoLocalSettings.Load(config.DatabasePath);
            var mapping = settings.Mapping ?? FoMapping.Suggest(schema);
            if (settings.Mapping == null)
            {
                var table = schema.FirstOrDefault(f => f.Table.Equals(config.TableName, StringComparison.OrdinalIgnoreCase))?.Table;
                if (table != null)
                {
                    mapping.AppointmentsTable = table;
                    string Column(params string[] names) => names.Select(n => schema.FirstOrDefault(f => f.Table == table && f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))?.Name).FirstOrDefault(n => n != null) ?? "";
                    mapping.AppointmentDate = Column(config.DateColumn);
                    mapping.AppointmentTime = Column(config.TimeColumn);
                    mapping.AppointmentPatientId = Column("Code Patient");
                    mapping.Cancelled = Column("Annulé", "Annule");
                }
            }
            operation.Token.ThrowIfCancellationRequested();
            return new FoContext(reader, config.DatabasePath, settings, mapping, schema);
        }, operation.Token);
    }

    private async Task SearchAsync()
    {
        using var operation = _lifetime.TryStart();
        if (operation == null) return;
        Busy(true);
        Message("Recherche des prochains rendez-vous et des consignes FO dans Access…");
        try
        {
            if (FromDate.Date == null || ToDate.Date == null)
                throw new FoDataException("Choisissez les deux dates de la période.");
            var from = FromDate.Date.Value.Date;
            var until = ToDate.Date.Value.Date.AddDays(1);
            if (from < Today || until <= from || (until - from).TotalDays > 93)
                throw new FoDataException("Choisissez une période à partir d’aujourd’hui, de 1 à 93 jours.");
            var context = await ReadContextAsync(operation);
            if (!operation.IsCurrent) return;
            context.Mapping.SelectClinicalFields(context.Schema);
            try { context.Mapping.Validate(context.Schema); }
            catch (FoDataException ex) { throw new FoDataException(ex.Message + " Vérifiez le bouton Champs Access."); }
            var result = await Task.Run(() =>
            {
                var data = context.Reader.Load(context.Mapping, from, until, operation.Token);
                var rows = FoPatientRow.Analyze(data, context.Reader.Now, operation.Token)
                    .Select(r => r.WithReview(context.Settings.Reviews.GetValueOrDefault(r.ReviewKey(data.SourceKey)))).ToList();
                operation.Token.ThrowIfCancellationRequested();
                return (data.SourceKey, Count: data.Patients.Count, Rows: rows);
            }, operation.Token);
            if (!operation.IsCurrent) return;
            context.Settings.Mapping = context.Mapping;
            context.Settings.Save(context.Path);
            _context = context;
            _sourceKey = result.SourceKey;
            _searchedFrom = from; _searchedUntil = until;
            _rows = result.Rows;
            _hasResults = true;
            ApplyFilter();
            Message($"{result.Rows.Count} patient(s) avec une consigne FO parmi {result.Count} patient(s) ayant rendez-vous du {from:dd/MM/yyyy} au {until.AddDays(-1):dd/MM/yyyy}. Actualisé à {context.Reader.Now:HH:mm}.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (operation.IsCurrent) ShowFailure(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private void ApplyFilter()
    {
        if (!_ready) return;
        var rows = FoPatientRow.Filter(_rows, StateFilter.SelectedIndex, PatientFilter.Text);
        PatientsList.ItemsSource = rows;
        EmptyText.Text = !_hasResults ? "Cliquez sur Rechercher les patients pour charger la liste." : rows.Count == 0 ? "Aucun patient ne correspond à ces filtres dans la période analysée." : "";
        SummaryText.Text = !_hasResults ? "" : $"{rows.Count} affiché(s) · {_rows.Count(r => r.Status == FoStatus.Planned)} FO à prévoir · {_rows.Count(r => r.Status == FoStatus.Verify)} à vérifier · {_rows.Count(r => r.Status == FoStatus.ProbablyPerformed)} probablement réalisé(s) — du {_searchedFrom:dd/MM} au {_searchedUntil.AddDays(-1):dd/MM}";
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        _dialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private static TextBlock Text(string value, bool heading = false) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        FontSize = heading ? 16 : 14,
        FontWeight = heading ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
    };

    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FoPatientRow row || _context == null) return;
        using var operation = _lifetime.TryStart();
        if (operation == null) return;
        var context = _context;
        Busy(true);
        try
        {
            var source = await Task.Run(() => context.Reader.ReadConsultation(context.Mapping, row.PatientId, row.Analysis.SourceId), operation.Token);
            if (!operation.IsCurrent) return;
            var panel = new StackPanel { Spacing = 12, MaxWidth = 620 };
            panel.Children.Add(Text($"Rendez-vous : {row.Appointment:dd/MM/yyyy à HH:mm}\nConsultation du {source.Date:dd/MM/yyyy} — n° {source.Id}"));
            panel.Children.Add(Text(row.Analysis.Reason));
            panel.Children.Add(Text("Élément retrouvé", true)); panel.Children.Add(Text(row.Excerpt));
            if (!string.IsNullOrWhiteSpace(row.Analysis.LaterExcerpt))
            {
                panel.Children.Add(Text($"Mention ultérieure ({row.Analysis.LaterDate:dd/MM/yyyy})", true));
                panel.Children.Add(Text(row.Analysis.LaterExcerpt));
            }
            panel.Children.Add(Text("Observation", true)); panel.Children.Add(Text(string.IsNullOrWhiteSpace(source.Text) ? "Champ vide." : source.Text));
            panel.Children.Add(Text("REFRACTION", true)); panel.Children.Add(Text(string.IsNullOrWhiteSpace(source.RefractionText) ? "Champ vide." : source.RefractionText));
            var review = new ComboBox { Header = "État après relecture", HorizontalAlignment = HorizontalAlignment.Stretch };
            review.ItemsSource = new[] { "Conserver la détection automatique", "FO à prévoir", "À vérifier", "FO probablement réalisé" };
            review.SelectedIndex = row.Review == null ? 0 : (int)row.Review.Status + 1;
            panel.Children.Add(review);
            panel.Children.Add(Text("La relecture est enregistrée sur ce poste, dans votre profil Windows. Elle ne modifie pas le dossier Access."));
            if (row.Review != null) panel.Children.Add(Text($"Dernière relecture : {row.Review.Author}, {row.Review.Utc.ToLocalTime():dd/MM/yyyy HH:mm}"));
            var dialog = new ContentDialog
            {
                Title = row.PatientName, XamlRoot = XamlRoot, PrimaryButtonText = "Enregistrer la relecture", CloseButtonText = "Fermer",
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 580 }
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary || !operation.IsCurrent) return;
            // Re-read the full timeline so a newly added consultation cannot receive an obsolete approval.
            var fresh = await Task.Run(() =>
            {
                var data = context.Reader.Load(context.Mapping, _searchedFrom, _searchedUntil, operation.Token);
                return FoPatientRow.Analyze(data, context.Reader.Now, operation.Token).FirstOrDefault(r => r.PatientId == row.PatientId);
            }, operation.Token);
            if (!operation.IsCurrent) return;
            if (fresh == null || fresh.Analysis.EvidenceHash != row.Analysis.EvidenceHash)
                throw new FoDataException("Le dossier ou le rendez-vous a changé. Relancez la recherche avant d’enregistrer cette relecture.");
            var key = row.ReviewKey(_sourceKey);
            var reviews = new Dictionary<string, FoReview>(context.Settings.Reviews);
            FoReview? saved = null;
            if (review.SelectedIndex == 0) reviews.Remove(key);
            else
            {
                saved = new(row.Analysis.EvidenceHash, (FoStatus)(review.SelectedIndex - 1), Environment.UserName, DateTime.UtcNow);
                reviews[key] = saved;
            }
            var updated = new FoLocalSettings { Mapping = context.Mapping, Reviews = reviews };
            updated.Save(context.Path);
            _context = context with { Settings = updated };
            _rows = _rows.Select(r => r.PatientId == row.PatientId ? r.WithReview(saved) : r).ToList();
            ApplyFilter();
            Message("Relecture enregistrée sur ce poste.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (operation.IsCurrent) ShowFailure(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async void AccessDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        using var operation = _lifetime.TryStart();
        if (operation == null) return;
        Busy(true);
        try
        {
            var provider = MachineConfig.Load().AccessOleDbProvider;
            var report = await Task.Run(() => AccessRuntimeDiagnostics.Describe(provider), operation.Token);
            if (!operation.IsCurrent) return;
            await ShowDialogAsync(new ContentDialog
            {
                Title = "Diagnostic Access du poste", XamlRoot = XamlRoot, CloseButtonText = "Fermer",
                Content = new ScrollViewer
                {
                    Content = Text(report), MaxHeight = 580, MaxWidth = 640,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            });
        }
        catch (OperationCanceledException) { }
        catch { if (operation.IsCurrent) Message("Le diagnostic local n’a pas pu être lu. Aucun accès à la base n’a été effectué.", InfoBarSeverity.Error); }
        finally { if (operation.IsCurrent) Busy(false); }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        using var operation = _lifetime.TryStart();
        if (operation == null) return;
        Busy(true);
        bool saved = false;
        try
        {
            var context = await ReadContextAsync(operation);
            if (!operation.IsCurrent) return;
            var map = context.Mapping;
            var panel = new StackPanel { Spacing = 12, MinWidth = 400 };
            panel.Children.Add(Text("Choisissez les champs réellement présents dans la base. Les textes analysés restent Observation et REFRACTION. Les changements sont propres à ce poste."));
            var tables = context.Schema.Select(f => f.Table).Distinct().ToList();
            void Group(string title, string initialTable, Action<string> setTable, params (string Label, string Value, Action<string> Set)[] fields)
            {
                panel.Children.Add(Text(title, true));
                var table = new ComboBox { Header = "Table", ItemsSource = tables, SelectedItem = initialTable, HorizontalAlignment = HorizontalAlignment.Stretch };
                panel.Children.Add(table);
                var controls = fields.Select(f => (Field: f, Box: new ComboBox { Header = f.Label, HorizontalAlignment = HorizontalAlignment.Stretch })).ToList();
                void Fill()
                {
                    var name = table.SelectedItem as string ?? "";
                    setTable(name);
                    var names = new[] { "" }.Concat(context.Schema.Where(f => f.Table == name).Select(f => f.Name)).ToList();
                    foreach (var item in controls)
                    {
                        var old = item.Box.SelectedItem as string ?? item.Field.Value;
                        item.Box.ItemsSource = names;
                        item.Box.SelectedItem = names.Contains(old) ? old : "";
                        item.Field.Set(item.Box.SelectedItem as string ?? "");
                    }
                }
                Fill();
                table.SelectionChanged += (_, _) => Fill();
                foreach (var item in controls)
                {
                    item.Box.SelectionChanged += (_, _) => item.Field.Set(item.Box.SelectedItem as string ?? "");
                    panel.Children.Add(item.Box);
                }
            }
            Group("Patients", map.PatientsTable, value => map.PatientsTable = value,
                ("Identifiant patient", map.PatientId, value => map.PatientId = value),
                ("Nom", map.LastName, value => map.LastName = value), ("Prénom", map.FirstName, value => map.FirstName = value));
            Group("Consultations", map.ConsultationsTable, value => map.ConsultationsTable = value,
                ("Identifiant consultation", map.ConsultationId, value => map.ConsultationId = value),
                ("Identifiant patient", map.ConsultationPatientId, value => map.ConsultationPatientId = value),
                ("Date", map.ConsultationDate, value => map.ConsultationDate = value));
            Group("Agenda", map.AppointmentsTable, value => map.AppointmentsTable = value,
                ("Identifiant patient", map.AppointmentPatientId, value => map.AppointmentPatientId = value),
                ("Date du rendez-vous", map.AppointmentDate, value => map.AppointmentDate = value),
                ("Heure", map.AppointmentTime, value => map.AppointmentTime = value),
                ("Annulation (laisser vide uniquement si absent)", map.Cancelled, value => map.Cancelled = value));
            var noCancellation = new CheckBox { Content = "Je confirme que l’agenda n’a pas de champ d’annulation", IsChecked = map.NoCancellationColumn };
            panel.Children.Add(noCancellation);
            var error = Text(""); panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                Title = "Champs Access — Préparation FO", XamlRoot = XamlRoot,
                PrimaryButtonText = "Enregistrer et rechercher", CloseButtonText = "Annuler",
                Content = new ScrollViewer { Content = panel, MaxHeight = 580, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    map.NoCancellationColumn = noCancellation.IsChecked == true;
                    map.SelectClinicalFields(context.Schema); map.Validate(context.Schema);
                    new FoLocalSettings { Mapping = map, Reviews = context.Settings.Reviews }.Save(context.Path);
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    error.Text = ex is FoDataException ? ex.Message : "Impossible d’enregistrer les réglages dans le profil Windows.";
                }
            };
            saved = await ShowDialogAsync(dialog) == ContentDialogResult.Primary && operation.IsCurrent;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (operation.IsCurrent) ShowFailure(ex); }
        finally { if (operation.IsCurrent) Busy(false); }
        // Release the guard before the single explicit search, never call a Loaded handler recursively.
        operation.Dispose();
        if (saved) await SearchAsync();
    }
}
