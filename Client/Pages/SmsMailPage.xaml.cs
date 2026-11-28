using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Client.Helpers;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using Client.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Client.Pages
{
    public sealed partial class SmsMailPage : Page
    {
        private SmsMailSettings _settings = new();
        private readonly IMessageHistory _history;
        // Ancien historique gardé sur ce poste, envoyé au serveur au premier passage.
        private static readonly string LocalHistoryPath = Path.Combine(SmsMailSettingsStore.DirectoryPath, "history.db");
        private List<RdvInfo> _appointments = new();
        private List<RdvInfo> _working = new();
        private List<PreparedMessage> _messages = new();
        private List<MessageHistoryEntry> _entries = new();
        private readonly ObservableCollection<AlertConfiguration> _alerts = new();
        private readonly ObservableCollection<AppointmentTimeRule> _timeRules = new();
        private List<RdvFamille> _families = new();
        private Dictionary<int, List<(DateTime Date, DateTime? RdvTime, DateTime? ArrivalTime)>> _appointmentHistory = new();
        private readonly Dictionary<string, bool> _familyDecisions = new();
        private bool _editorReady;
        private bool _syncingEditor;
        private string _preparedSignature = string.Empty;
        private bool _busy;
        private bool _unloaded;
        private bool _loaded;
        private CancellationTokenSource? _sendStop;

        public SmsMailPage()
        {
            InitializeComponent();
            Directory.CreateDirectory(SmsMailSettingsStore.DirectoryPath);
            _history = new ServerMessageHistory(() => App.ChatService.GetSmsMailHistoryConnection());
            Loaded += Page_Loaded;
            Unloaded += (_, _) => { _unloaded = true; _sendStop?.Cancel(); };
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _unloaded = false;
            if (_loaded) return;
            _loaded = true;
            await RunAsync(async () =>
            {
                _settings = await LoadServerSettingsAsync();
                DayPicker.Date = DateTimeOffset.Now.AddDays(1);
                CabinetText.Text = _settings.Cabinet;
                CabinetAddressText.Text = _settings.CabinetAddress;
                DurationNumber.Value = _settings.AppointmentDurationMinutes;
                SmsOnlyNoEmail.IsChecked = _settings.SmsOnlyWhenNoEmail;
                SmsFamilySizeFilter.SelectedIndex = Math.Clamp(_settings.SmsMinimumFamilySize, 1, 3) - 1;
                UseFamiliesCheck.IsChecked = _settings.UseEyeEmailFamilies;
                LogFamilyQuestionsCheck.IsChecked = _settings.LogFamilyQuestions;
                FamilyLogPathText.Text = SmsMailSettingsStore.FamilyQuestionsLogPath;
                ShowPostItTabCheck.IsChecked = _settings.ShowPostItTab;
                SetPostItTabVisible(_settings.ShowPostItTab);
                PostItFromPicker.Date = DateTimeOffset.Now.Date.AddMonths(-6);
                PostItToPicker.Date = DateTimeOffset.Now.Date;
                FamilyDatabaseText.Text = _settings.FamilyDatabasePath;
                CalendarInviteCheck.IsChecked = _settings.SendCalendarInvites;
                CalendarAlarmsCheck.IsChecked = _settings.SendCalendarAlarms;
                CalendarBusyCheck.IsChecked = _settings.CalendarIsBusy;
                CalendarSubjectText.Text = _settings.CalendarSubject;
                AdjustTimesCheck.IsChecked = _settings.AdjustReminderTimes;
                foreach (var rule in _settings.ReminderTimeRules) _timeRules.Add(rule);
                TimeRulesList.ItemsSource = _timeRules;
                foreach (var alert in _settings.Alerts) _alerts.Add(alert);
                AlertsList.ItemsSource = _alerts;
                SubjectText.Text = _settings.Subject;
                SmsTemplateText.Text = _settings.SmsTemplate;
                EmailTemplateText.Text = _settings.EmailTemplate;
                SmsAccountText.Text = _settings.Sms.ServiceName;
                SmsSenderText.Text = _settings.Sms.Sender;
                SmsAppKeyText.Text = _settings.Sms.ApplicationKey;
                SmsSecretText.Password = _settings.Sms.ApplicationSecret;
                SmsConsumerText.Password = _settings.Sms.ConsumerKey;
                SmtpHostText.Text = _settings.Email.SmtpHost;
                SmtpPortNumber.Value = _settings.Email.SmtpPort;
                SmtpSslCheck.IsChecked = _settings.Email.EnableSsl;
                ImapHostText.Text = _settings.Email.ImapHost;
                ImapPortNumber.Value = _settings.Email.ImapPort;
                BounceFilterText.Text = _settings.Email.BounceFilter;
                MailUserText.Text = _settings.Email.UserName;
                MailPasswordText.Password = _settings.Email.Password;
                MailFromText.Text = _settings.Email.FromAddress;
                MailDisplayText.Text = _settings.Email.FromDisplayName;
                ShowSecretState();
                StatusText.Text = _settingsNote + "Sélectionnez un jour et chargez les rendez-vous. Vérifiez vos paramètres avant le premier envoi.";
                try
                {
                    var migrated = await LocalHistoryMigration.MigrateAsync(LocalHistoryPath, _history);
                    await ReloadHistoryAsync();
                    if (migrated.HasValue)
                        StatusText.Text = $"Historique de ce poste envoyé au serveur ({migrated} message(s) ajouté(s)) : il est maintenant commun à tous les postes. " + StatusText.Text;
                }
                catch (Exception ex)
                {
                    // Le reste de la page reste utilisable ; l'historique et l'envoi demandent le serveur.
                    StatusText.Text = "Historique des SMS et mails indisponible : " + ex.Message;
                }
            });
        }

        private SmsMailSecretsState _secrets = new();
        private string _settingsNote = "";

        private Microsoft.AspNetCore.SignalR.Client.HubConnection Server() => App.ChatService.GetSmsMailHistoryConnection();

        /// <summary>
        /// Réglages communs gardés par le serveur (sans les secrets). Le premier poste mis à jour y envoie ses réglages locaux.
        /// Sans serveur, la page s'ouvre avec des réglages vides : rien ne peut être lu ni envoyé.
        /// </summary>
        private async Task<SmsMailSettings> LoadServerSettingsAsync()
        {
            _settingsNote = "";
            try
            {
                var connection = Server();
                var station = await connection.InvokeAsync<SmsMailStationSettings>("SmsMailSettingsGet");
                if (!station.Exists)
                {
                    SmsMailSettings? local = null;
                    try { local = SmsMailSettingsStore.Load(); } catch (Exception) { }
                    if (local != null && await connection.InvokeAsync<bool>("SmsMailSettingsImport", local))
                        _settingsNote = "Réglages SMS et mail de ce poste envoyés au serveur : ils sont maintenant communs à tous les postes. ";
                    station = await connection.InvokeAsync<SmsMailStationSettings>("SmsMailSettingsGet");
                }
                _secrets = station.Secrets;
                return station.Exists ? station.Settings : new SmsMailSettings();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.AspNetCore.SignalR.HubException or System.Net.Http.HttpRequestException or TimeoutException)
            {
                _settingsNote = "Réglages SMS et mail indisponibles (serveur) : " + ex.Message + " ";
                return new SmsMailSettings();
            }
        }

        /// <summary>Les secrets restent sur le serveur : un champ vide veut dire « garder celui du serveur ».</summary>
        private void ShowSecretState()
        {
            const string kept = "Enregistré sur le serveur — laisser vide pour garder";
            SmsSecretText.Password = SmsConsumerText.Password = MailPasswordText.Password = string.Empty;
            SmsSecretText.PlaceholderText = _secrets.SmsApplicationSecret ? kept : "";
            SmsConsumerText.PlaceholderText = _secrets.SmsConsumerKey ? kept : "";
            MailPasswordText.PlaceholderText = _secrets.MailPassword ? kept : "";
        }

        private SmsMailSettings ReadSettings()
        {
            if (double.IsNaN(DurationNumber.Value) || DurationNumber.Value < 1 || DurationNumber.Value > 480) throw new InvalidOperationException("La durée doit être comprise entre 1 et 480 minutes.");
            if (double.IsNaN(SmtpPortNumber.Value)) throw new InvalidOperationException("Renseignez le port SMTP.");
            return new SmsMailSettings
            {
                SmsMinimumFamilySize = SmsFamilySizeFilter.SelectedIndex + 1,
                AdjustReminderTimes = AdjustTimesCheck.IsChecked == true,
                ReminderTimeRules = _timeRules.Select(r => new AppointmentTimeRule { From = r.From, To = r.To }).ToList(),
                UseEyeEmailFamilies = UseFamiliesCheck.IsChecked == true, FamilyDatabasePath = FamilyDatabaseText.Text.Trim(),
                LogFamilyQuestions = LogFamilyQuestionsCheck.IsChecked == true,
                ShowPostItTab = ShowPostItTabCheck.IsChecked == true,
                SendCalendarInvites = CalendarInviteCheck.IsChecked == true,
                SendCalendarAlarms = CalendarAlarmsCheck.IsChecked == true, CalendarIsBusy = CalendarBusyCheck.IsChecked == true,
                CalendarSubject = CalendarSubjectText.Text,
                Alerts = _alerts.Select(a => new AlertConfiguration { OffsetMinutes = a.OffsetMinutes, Label = a.Label }).ToList(),
                Cabinet = CabinetText.Text.Trim(), CabinetAddress = CabinetAddressText.Text.Trim(),
                AppointmentDurationMinutes = (int)DurationNumber.Value, CalendarTimeZone = _settings.CalendarTimeZone, SmsOnlyWhenNoEmail = SmsOnlyNoEmail.IsChecked == true,
                Subject = SubjectText.Text, SmsTemplate = SmsTemplateText.Text, EmailTemplate = EmailTemplateText.Text,
                Sms = new SmsSettings
                {
                    ServiceName = SmsAccountText.Text.Trim(), Sender = SmsSenderText.Text.Trim(),
                    ApplicationKey = SmsAppKeyText.Text.Trim(), ApplicationSecret = SmsSecretText.Password.Trim(),
                    ConsumerKey = SmsConsumerText.Password.Trim()
                },
                Email = new EmailSettings
                {
                    SmtpHost = SmtpHostText.Text.Trim(), SmtpPort = (int)SmtpPortNumber.Value, EnableSsl = SmtpSslCheck.IsChecked == true,
                    UserName = MailUserText.Text.Trim(), Password = MailPasswordText.Password,
                    ImapHost = ImapHostText.Text.Trim(), ImapPort = double.IsNaN(ImapPortNumber.Value) ? 993 : (int)ImapPortNumber.Value,
                    BounceFilter = BounceFilterText.Text.Trim(),
                    FromAddress = MailFromText.Text.Trim(), SenderAddress = MailFromText.Text.Trim(), FromDisplayName = MailDisplayText.Text
                }
            };
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_busy) return;
            _busy = true;
            Workspace.IsEnabled = false;
            BusyProgress.Visibility = Visibility.Visible;
            try { await action(); }
            catch (Exception ex) { StatusText.Text = "Erreur : " + ex.Message; }
            finally
            {
                _busy = false;
                Workspace.IsEnabled = true;
                BusyProgress.Visibility = Visibility.Collapsed;
                BusyProgress.IsIndeterminate = true;
                BusyProgress.Value = 0;
                StopSendButton.Visibility = Visibility.Collapsed;
            }
        }

        private string _serverFamiliesNote = "";

        /// <summary>Familles calculées par le serveur pour les patients du jour ; liste vide si le serveur ne répond pas.</summary>
        private async Task<List<EyeChat.SmsMail.FamilyMembershipDto>> LoadServerFamiliesAsync(IReadOnlyList<RdvInfo> rows, DateTime day)
        {
            _serverFamiliesNote = "";
            var ids = rows.Where(r => r.DateRDV.Date == day && !r.IsCancelled && r.Id > 0).Select(r => r.Id).Distinct().ToList();
            if (ids.Count == 0) return new();
            try
            {
                var connection = App.ChatService.GetSmsMailHistoryConnection();
                return await connection.InvokeAsync<List<EyeChat.SmsMail.FamilyMembershipDto>>("SmsMailFamilies", ids) ?? new();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.AspNetCore.SignalR.HubException or System.Net.Http.HttpRequestException or TimeoutException)
            {
                _serverFamiliesNote = " (familles du serveur indisponibles)";
                return new();
            }
        }

        private static readonly object FamilyLogGate = new();

        /// <summary>Journal anonymisé (numéros patients, pas de noms) : à transmettre pour améliorer les questions.</summary>
        private static void WriteFamilyLog(string entry)
        {
            try
            {
                lock (FamilyLogGate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SmsMailSettingsStore.FamilyQuestionsLogPath)!);
                    File.AppendAllText(SmsMailSettingsStore.FamilyQuestionsLogPath, entry + Environment.NewLine, new UTF8Encoding(true));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void OpenFamilyLog_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.GetDirectoryName(SmsMailSettingsStore.FamilyQuestionsLogPath)!;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }

        /// <summary>Réponses « Regrouper / Garder séparés » : gardées par le serveur, la question n'est plus posée sur aucun poste.</summary>
        private async Task SendFamilyAnswersAsync(IReadOnlyList<ServerFamilies.Answer> answers)
        {
            if (answers.Count == 0) return;
            try
            {
                var connection = App.ChatService.GetSmsMailHistoryConnection();
                foreach (var answer in answers)
                    await connection.InvokeAsync<int>("SmsMailFamilyAnswer", answer.PatientIds.ToList(), answer.Together, Environment.MachineName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.AspNetCore.SignalR.HubException or System.Net.Http.HttpRequestException or TimeoutException)
            {
                _serverFamiliesNote += " (réponse non transmise au serveur : la question sera reposée)";
            }
        }

        private bool ConfirmHistoricalFamily(string message, string title)
        {
            if (_familyDecisions.TryGetValue(message, out bool answer)) return answer;
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (_unloaded) { pending.TrySetResult(false); return; }
                    var dialog = new ContentDialog
                    {
                        Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, MaxHeight = 400 },
                        PrimaryButtonText = "Regrouper", CloseButtonText = "Garder séparés", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
                    };
                    var result = await dialog.ShowAsync() == ContentDialogResult.Primary;
                    _familyDecisions[message] = result; pending.TrySetResult(result);
                }
                catch (Exception ex) { pending.TrySetException(ex); }
            })) return false;
            return pending.Task.GetAwaiter().GetResult();
        }

        private async void FamilyDatabase_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            if (App.MainWindow == null) return;
            var picker = new FileOpenPicker();
            foreach (var extension in new[] { ".mdb", ".mde", ".accdb" }) picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            FamilyDatabaseText.Text = file.Path; UseFamiliesCheck.IsChecked = true;
            await PrepareAsync();
        });

        private async void HistoricalFamilies_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            if (_appointments.Count == 0) { StatusText.Text = "Chargez les rendez-vous avant de comparer leur historique."; return; }
            _appointmentHistory = AppointmentLoader.Group(await Server().InvokeAsync<List<AgendaVisit>>("SmsMailAgendaHistory", DateTime.Today));
            _familyDecisions.Clear(); UseFamiliesCheck.IsChecked = true;
            await PrepareAsync();
        });

        private void AddAlert_Click(object sender, RoutedEventArgs e) => _alerts.Add(new AlertConfiguration { Label = "Alerte " + (_alerts.Count + 1), OffsetMinutes = 60 });
        private void DeleteAlert_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AlertConfiguration alert) _alerts.Remove(alert);
        }

        private async void EmailEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (_editorReady) return;
            try
            {
                await EmailEditor.EnsureCoreWebView2Async();
                EmailEditor.CoreWebView2.WebMessageReceived -= Editor_MessageReceived;
                EmailEditor.CoreWebView2.WebMessageReceived += Editor_MessageReceived;
                EmailEditor.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
                var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "EmailEditor", "index.html"));
                EmailEditor.NavigateToString(html);
            }
            catch (Exception ex) { StatusText.Text = "Éditeur visuel indisponible ; le champ HTML reste utilisable. " + ex.Message; }
        }

        private void Editor_MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var document = JsonDocument.Parse(args.WebMessageAsJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type)) return;
                if (type.GetString() == "editorReady") { _editorReady = true; PushEditorHtml(); }
                else if (type.GetString() == "contentChanged" && root.TryGetProperty("payload", out var html))
                {
                    _syncingEditor = true;
                    try { EmailTemplateText.Text = html.GetString() ?? string.Empty; }
                    finally { _syncingEditor = false; }
                }
            }
            catch (JsonException) { }
        }
        private void PushEditorHtml()
        {
            if (_editorReady && !_syncingEditor)
                EmailEditor.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "loadHtml", payload = EmailTemplateText.Text }));
        }
        private void EmailHtml_Changed(object sender, TextChangedEventArgs e) => PushEditorHtml();

        private async void Load_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            ClearMessages();
            _appointments.Clear();
            _working.Clear();
            _appointmentHistory.Clear(); _familyDecisions.Clear();
            AppointmentsList.ItemsSource = null;
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Sélectionnez une date.");
            StatusText.Text = "Chargement des rendez-vous…";
            // C'est le serveur qui lit l'agenda (sa base Access, ses paramètres RDV).
            _appointments = await Server().InvokeAsync<List<RdvInfo>>("SmsMailAgendaDay", day);
            AppointmentsList.ItemsSource = _appointments;
            await PrepareAsync();
        });

        private async Task PrepareAsync()
        {
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Sélectionnez une date.");
            _settings = ReadSettings();
            var working = JsonSerializer.Deserialize<List<RdvInfo>>(JsonSerializer.Serialize(_appointments)) ?? new();
            _working = working;
            if (_settings.UseEyeEmailFamilies && !string.IsNullOrWhiteSpace(_settings.FamilyDatabasePath))
                await EyeEmailFamilyDatabase.ApplyAsync(_settings.FamilyDatabasePath, MachineConfig.Load(), working, day);
            var serverFamilies = _settings.UseEyeEmailFamilies ? await LoadServerFamiliesAsync(working, day) : new();
            int serverApplied = 0;
            var familyAnswers = new List<ServerFamilies.Answer>();
            _families = _settings.UseEyeEmailFamilies
                ? await Task.Run(() =>
                {
                    // Familles du serveur (page « Familles ») : une famille à vérifier n'est regroupée qu'après accord, en voyant pourquoi.
                    serverApplied = ServerFamilies.Apply(working, day, serverFamilies, ConfirmHistoricalFamily, familyAnswers,
                        _settings.LogFamilyQuestions ? WriteFamilyLog : null);
                    return EyeEmailFamilyGrouping.Build(working.Where(r => r.DateRDV.Date == day), _appointmentHistory,
                        _appointmentHistory.Count > 0 ? ConfirmHistoricalFamily : null);
                }) : new();
            await SendFamilyAnswersAsync(familyAnswers);
            FilterFamilies();
            AppointmentsList.ItemsSource = working;
            _messages = MessagePreparation.Prepare(working, day, _settings, _families);
            var rules = _settings.AdjustReminderTimes ? ReminderTimes.Validate(_settings.ReminderTimeRules) : new();
            var changes = working.Where(r => r.DateRDV.Date == day && !r.IsCancelled)
                .Select(r => (Row: r, Decision: RemarkArrivalTime.Resolve(r, rules)))
                .Where(x => x.Decision.NeedsReview || x.Decision.Time != x.Row.HeureRDV)
                .Select(x => $"{x.Row.Prenom} {x.Row.Nom} : RDV {x.Row.HeureRDV:HH:mm} → "
                    + (x.Decision.NeedsReview ? "à vérifier" : $"venue {x.Decision.Time:HH:mm}") + $" ({x.Decision.Reason})").ToList();
            TimeChangesText.Text = changes.Count == 0 ? "Aucun horaire ajusté." : string.Join(Environment.NewLine, changes);
            _preparedSignature = MessagePreparation.PreparationSignature(_settings);
            await ReloadHistoryAsync();
            var sent = await Task.Run(() => _history.LoadReservedKeysAsync());
            foreach (var message in _messages.Where(m => sent.Contains(m.Key) || m.ReservationKeys.Any(sent.Contains)))
            {
                message.Selected = false;
                message.CanSend = false;
                message.Status = "Déjà traité — consulter l'historique";
            }
            FilterMessages();
            SummaryText.Text = $"{_messages.Count(m => m.Channel == "Mail")} mail(s), {_messages.Count(m => m.Channel == "SMS")} SMS";
            StatusText.Text = $"{_families.Count} famille(s) EyeEmail{(serverApplied > 0 ? $", dont {serverApplied} connue(s) du serveur" : "")}{_serverFamiliesNote} ; {_appointments.Count} RDV chargés, {_appointments.Count(r => r.IsCancelled)} annulé(s) exclus. Vérifiez les destinataires et les messages avant l'envoi.";
        }

        private void ReminderTimes_Click(object sender, RoutedEventArgs e)
        {
            Workspace.SelectedItem = SettingsTab;
            ReminderTimesExpander.IsExpanded = true;
            DispatcherQueue.TryEnqueue(() => ReminderTimesExpander.StartBringIntoView());
        }
        private void AddTimeRule_Click(object sender, RoutedEventArgs e) => _timeRules.Add(new AppointmentTimeRule());
        private void RemoveTimeRule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppointmentTimeRule rule) _timeRules.Remove(rule);
            ClearMessages();
        }
        private void PreparationOptions_Click(object sender, RoutedEventArgs e)
        {
            Workspace.SelectedItem = SettingsTab;
            PreparationExpander.IsExpanded = true;
            DispatcherQueue.TryEnqueue(() => PreparationExpander.StartBringIntoView());
        }
        private async void ReviewTimes_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ShowTextPreviewAsync("SMS", "Horaires réels → heures d’arrivée annoncées", TimeChangesText.Text));
        private void MailDetails_Opening(object sender, object e)
        {
            if (sender is Flyout flyout && flyout.Content is FrameworkElement content
                && flyout.Target is FrameworkElement target) content.DataContext = target.DataContext;
        }

        private async Task ShowTextPreviewAsync(string channel, string caption, string body)
        {
            var content = channel == "Mail" ? MailTextPreview.Convert(body) : body;
            // Imported archives can hold images or attachments as long encoded blocks.
            content = System.Text.RegularExpressions.Regex.Replace(content, @"[A-Za-z0-9+/=\r\n]{2000,}", "[contenu encodé non affiché]");
            if (content.Length > 60000) content = content.Substring(0, 60000) + "\n\n[… message tronqué pour l’affichage]";
            // Bloc de texte dans une zone qui défile (et non une TextBox) : la TextBox en lecture seule
            // n'affichait que la première ligne des mails convertis (retours à la ligne « \n »).
            var text = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = content.Replace("\r\n", "\n"),
                    TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                    FontSize = 16, Margin = new Thickness(16)
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = Math.Clamp(XamlRoot.Size.Height - 250, 160, 650),
                Width = Math.Clamp(XamlRoot.Size.Width - 120, 240, 820)
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            panel.Children.Add(text);
            var dialog = new ContentDialog { Title = channel == "Mail" ? "Mail · aperçu texte" : "SMS", Content = panel, CloseButtonText = "Fermer", XamlRoot = XamlRoot };
            dialog.Resources["ContentDialogMaxWidth"] = 920.0;
            await dialog.ShowAsync();
        }

        private async void Prepare_Click(object sender, RoutedEventArgs e) => await RunAsync(PrepareAsync);

        private void ShowPostItTab_Changed(object sender, RoutedEventArgs e)
        {
            if (PostItTab == null || Workspace == null) return;
            SetPostItTabVisible(ShowPostItTabCheck.IsChecked == true);
            if (_loaded) StatusText.Text = "Enregistrez les paramètres pour conserver l’affichage de l’onglet Post-it.";
        }

        // A collapsed PivotItem can keep its header: remove and re-insert the tab instead.
        private void SetPostItTabVisible(bool visible)
        {
            var present = Workspace.Items.Contains(PostItTab);
            if (visible && !present) Workspace.Items.Insert(Math.Max(0, Workspace.Items.IndexOf(SettingsTab)), PostItTab);
            else if (!visible && present)
            {
                if (ReferenceEquals(Workspace.SelectedItem, PostItTab)) Workspace.SelectedIndex = 0;
                Workspace.Items.Remove(PostItTab);
            }
        }

        private async void ExportPostItLinks_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var from = PostItFromPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez la date de début.");
            var to = PostItToPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez la date de fin.");
            if (to < from) throw new InvalidOperationException("La date de fin doit être après la date de début.");
            if (PostItPasswordBox.Password.Length < DiagnosticPackage.MinimumPasswordLength)
                throw new InvalidOperationException($"Le mot de passe doit contenir au moins {DiagnosticPackage.MinimumPasswordLength} caractères.");
            if (PostItPasswordBox.Password != PostItConfirmBox.Password) throw new InvalidOperationException("Les deux mots de passe sont différents.");
            List<PostItAnalysis.PostIt> postIts; List<(string Nom, string Prenom)> names; List<RdvInfo> appointments;
            try
            {
                PostItStatusText.Text = "Lecture des post-it…";
                (postIts, names) = await LoadPostItsAsync(from, to);
                PostItStatusText.Text = $"{postIts.Count} post-it lus. Lecture des rendez-vous de la période…";
                appointments = await Server().InvokeAsync<List<RdvInfo>>("SmsMailAgendaRange", from, to);
            }
            catch (Exception ex) { PostItStatusText.Text = "Échec de la lecture : " + ex.Message; throw; }
            var pseudonymize = PostItPseudonymizeCheck.IsChecked == true;
            var secret = PostItPasswordBox.Password;
            PostItStatusText.Text = $"{postIts.Count} post-it, {appointments.Count} rendez-vous. Recherche des liens…";
            var json = await Task.Run(() => PostItAnalysis.BuildLinksReportJson(postIts, appointments, names, from, to, pseudonymize));
            var (links, unresolved) = await Task.Run(() => PostItFamilyLinks.Build(postIts.Where(p => PostItAnalysis.IsConsultationPostIt(p.Text)).ToList(), appointments));
            var bytes = await Task.Run(() => DiagnosticPackage.Encrypt(json, secret, PostItAnalysis.LinksFormat));
            var summary = $"{links.Count} lien(s) trouvé(s) : {links.Count(l => l.SameSurname)} même nom, {links.Count(l => !l.SameSurname)} nom différent (moins d’1 h). "
                + $"{unresolved.Count} cas non résolu(s). Familles : {PostItFamilyLinks.Families(links).Count}.";
            if (await SaveFileAsync($"LiensPostIt_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}", "Rapport chiffré", DiagnosticPackage.Extension, bytes))
                PostItStatusText.Text = "Rapport enregistré. " + summary + " Envoyez le mot de passe séparément.";
            else PostItStatusText.Text = "Enregistrement annulé. " + summary;
            PostItPasswordBox.Password = PostItConfirmBox.Password = string.Empty;
        });

        private async void ExportPostIts_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var from = PostItFromPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez la date de début.");
            var to = PostItToPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez la date de fin.");
            if (to < from) throw new InvalidOperationException("La date de fin doit être après la date de début.");
            if (PostItPasswordBox.Password.Length < DiagnosticPackage.MinimumPasswordLength)
                throw new InvalidOperationException($"Le mot de passe doit contenir au moins {DiagnosticPackage.MinimumPasswordLength} caractères.");
            if (PostItPasswordBox.Password != PostItConfirmBox.Password) throw new InvalidOperationException("Les deux mots de passe sont différents.");
            PostItStatusText.Text = "Lecture des post-it…";
            List<PostItAnalysis.PostIt> postIts; List<(string Nom, string Prenom)> names;
            try { (postIts, names) = await LoadPostItsAsync(from, to); }
            catch (Exception ex) { PostItStatusText.Text = "Échec de la lecture : " + ex.Message; throw; }
            var kept = postIts.Count(p => PostItAnalysis.IsConsultationPostIt(p.Text));
            if (kept == 0) { PostItStatusText.Text = $"{postIts.Count} post-it lus, aucun ne ressemble à un post-it de consultation. Rien n’a été enregistré."; return; }
            var pseudonymize = PostItPseudonymizeCheck.IsChecked == true;
            var secret = PostItPasswordBox.Password;
            PostItStatusText.Text = $"{kept} post-it de consultation sur {postIts.Count}. Chiffrement…";
            var bytes = await Task.Run(() => DiagnosticPackage.Encrypt(PostItAnalysis.BuildJson(postIts, names, from, to, pseudonymize), secret, PostItAnalysis.Format));
            if (await SaveFileAsync($"PostIt_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}", "Export chiffré", DiagnosticPackage.Extension, bytes))
                PostItStatusText.Text = $"Enregistré : {kept} post-it de consultation sur {postIts.Count} lus du {from:dd/MM/yyyy} au {to:dd/MM/yyyy}"
                    + (pseudonymize ? ", pseudonymisés." : ", noms en clair.") + " Envoyez le mot de passe séparément.";
            else PostItStatusText.Text = "Enregistrement annulé.";
            PostItPasswordBox.Password = PostItConfirmBox.Password = string.Empty;
        });

        private async Task<(List<PostItAnalysis.PostIt> PostIts, List<(string Nom, string Prenom)> Names)> LoadPostItsAsync(DateTime from, DateTime to)
        {
            var result = await Server().InvokeAsync<SmsMailPostIts>("SmsMailAgendaPostIts", from, to);
            return (result.PostIts, result.Names.Select(n => (n.Nom, n.Prenom)).ToList());
        }

        /// <summary>
        /// Fichier .eyeconfig chiffré à importer sur le serveur (administration → SMS et mail). Il reprend les réglages locaux
        /// de ce poste s'il en a encore (clés et mot de passe compris), sinon ceux affichés (sans secret : ils sont sur le serveur).
        /// </summary>
        private async void ExportConfig_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            SmsMailSettings? local = null;
            try { local = SmsMailSettingsStore.Load(); } catch (Exception) { }
            var hasLocal = local != null && (local.Sms.ApplicationSecret.Length > 0 || local.Email.Password.Length > 0 || local.Email.SmtpHost.Length > 0);
            var settings = hasLocal ? local! : ReadSettings();
            var password = new PasswordBox { Header = $"Mot de passe du fichier ({DiagnosticPackage.MinimumPasswordLength} caractères au moins)" };
            var confirm = new PasswordBox { Header = "Confirmer le mot de passe" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = (hasLocal
                    ? "Le fichier reprend les réglages enregistrés sur ce poste, clés OVH et mot de passe SMTP compris : "
                    : "Ce poste n'a plus de réglages locaux : le fichier reprend les réglages affichés, sans clé ni mot de passe (ils sont gardés par le serveur) : ")
                    + SmsMailConfigFile.Describe(settings) + ".\n\nImportez-le dans l'administration du serveur, page « SMS et mail »."
            });
            panel.Children.Add(password); panel.Children.Add(confirm); panel.Children.Add(error);
            byte[]? bytes = null;
            var dialog = new ContentDialog
            {
                Title = "Exporter la configuration SMS et mail", Content = panel, PrimaryButtonText = "Exporter", CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (password.Password != confirm.Password) throw new InvalidOperationException("Les deux mots de passe sont différents.");
                    bytes = SmsMailConfigFile.Export(settings, password.Password);
                }
                catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || bytes == null) return;
            StatusText.Text = await SaveFileAsync($"config-sms-mail-{Environment.MachineName}", "Configuration EyeChat chiffrée", SmsMailConfigFile.Extension, bytes)
                ? "Configuration exportée. Importez-la sur le serveur (administration → SMS et mail) et transmettez le mot de passe séparément."
                : "Export annulé.";
        });

        private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            _settings = ReadSettings();
            await Server().InvokeAsync("SmsMailSettingsSave", _settings);
            _settings = await LoadServerSettingsAsync();
            ShowSecretState();
            ClearMessages();
            StatusText.Text = "Enregistré sur le serveur, pour tous les postes. Préparez à nouveau les messages pour appliquer les modifications.";
        });

        private void ApplyEmailModel_Click(object sender, RoutedEventArgs e)
        {
            EmailTemplateText.Text = SmsMailSettings.DefaultEmailTemplate;
            ClearMessages();
            StatusText.Text = "Modèle du mail joint appliqué. Enregistrez les modèles puis préparez les messages.";
        }

        private async void Balance_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var balance = await Server().InvokeAsync<decimal>("SmsMailBalance");
            StatusText.Text = $"Solde OVH : {balance} crédit(s) SMS.";
        });

        private async void TestSend_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var email = new TextBox { Header = "Adresse mail de test (facultative)", PlaceholderText = "vous@exemple.fr", MinWidth = 320 };
            var phone = new TextBox { Header = "Numéro mobile de test (facultatif)", PlaceholderText = "06… ou +33…" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = "Saisissez une adresse mail, un numéro, ou les deux. Les modèles et paramètres actuellement affichés seront utilisés pour un rendez-vous fictif demain à 10 h (heure de Paris). Le mail respecte les options d'invitation .ics et d'alarmes. L'envoi SMS consomme les crédits habituels.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(email); panel.Children.Add(phone); panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                Title = "Tester le mail et les SMS", Content = panel, PrimaryButtonText = "Envoyer le test",
                CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            List<PreparedMessage>? prepared = null;
            SmsMailSettings? settings = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    settings = ReadSettings();
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.CalendarTimeZone);
                    var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
                    prepared = TestMessagePreparation.Create(settings, email.Text, phone.Text, today);
                }
                catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || prepared == null || settings == null) return;
            // Réglages OVH et SMTP du serveur : c'est lui qui vérifie et envoie.
            await Server().InvokeAsync("SmsMailValidate", prepared);
            await SendInBackgroundAsync(prepared, message => SendOneAsync(message, test: true));
            var outcomes = prepared.Where(m => !string.IsNullOrEmpty(m.Status)).Select(m => $"{m.Channel} vers {m.Contact} : {m.Status}").ToList();
            await ReloadHistoryAsync();
            StatusText.Text = string.Join(Environment.NewLine, outcomes);
        });

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendSelectedAsync(null);
        private async void SendMail_Click(object sender, RoutedEventArgs e) => await SendSelectedAsync("Mail");
        private async void SendSms_Click(object sender, RoutedEventArgs e) => await SendSelectedAsync("SMS");
        private Task SendSelectedAsync(string? channel) => RunAsync(async () =>
        {
            var selected = MessageSelection.ForSending(VisibleMessages(), channel);
            if (selected.Count == 0) { StatusText.Text = "Sélectionnez au moins un message à envoyer."; return; }
            var settings = ReadSettings();
            if (string.IsNullOrWhiteSpace(settings.Cabinet)) throw new InvalidOperationException("Renseignez le nom du cabinet puis préparez les messages.");
            // Require a fresh preparation if a model or the fallback rule changed.
            if (MessagePreparation.PreparationSignature(settings) != _preparedSignature)
                throw new InvalidOperationException("Les modèles ont changé. Préparez à nouveau les messages.");
            if (selected.Any(m => string.IsNullOrWhiteSpace(m.Body))) throw new InvalidOperationException("Un message est vide. Vérifiez les modèles.");
            await Server().InvokeAsync("SmsMailValidate", selected.ToList());
            var dialog = new ContentDialog
            {
                Title = "Confirmer l'envoi",
                Content = $"Envoyer {selected.Count(m => m.Channel == "Mail")} mail(s) et {selected.Count(m => m.Channel == "SMS")} SMS pour le {DayPicker.Date:dd/MM/yyyy} ? Les SMS peuvent consommer plusieurs crédits.\n\nLes contacts et contenus sélectionnés seront envoyés immédiatement.",
                PrimaryButtonText = "Envoyer", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var outcome = await SendInBackgroundAsync(selected, message => SendOneAsync(message, test: false));
            FilterMessages();
            await ReloadHistoryAsync();
            StatusText.Text = $"{outcome.Accepted} accepté(s), {outcome.Skipped} déjà traité(s), {outcome.Uncertain} à vérifier."
                + (outcome.Stopped ? $" Envoi arrêté : {outcome.NotStarted} message(s) non envoyé(s), toujours sélectionnables." : "");
        });

        // Envoi hors du fil de l'interface : la fenêtre reste fluide, la progression s'affiche
        // et l'utilisateur peut arrêter l'envoi après le message en cours.
        private async Task<SendOutcome> SendInBackgroundAsync(IReadOnlyList<PreparedMessage> messages, Func<PreparedMessage, Task<SendResult>> send)
        {
            using var stop = new CancellationTokenSource();
            _sendStop = stop;
            if (_unloaded) stop.Cancel();
            BusyProgress.IsIndeterminate = false;
            BusyProgress.Maximum = Math.Max(1, messages.Count);
            BusyProgress.Value = 0;
            StopSendButton.IsEnabled = true;
            StopSendButton.Visibility = Visibility.Visible;
            var progress = new Progress<SendProgress>(p =>
            {
                if (_sendStop != stop) return;
                BusyProgress.Value = p.Done;
                StatusText.Text = stop.IsCancellationRequested
                    ? $"Arrêt demandé : fin du message en cours ({p.Done + 1} / {p.Total})…"
                    : $"Envoi {p.Done + 1} / {p.Total} : {(p.Current.Channel == "SMS" ? "SMS" : "mail")} vers {p.Current.Contact}…";
            });
            var sender = new MessageSender(_history, send);
            try { return await Task.Run(() => sender.SendAsync(messages, progress, stop.Token)); }
            finally
            {
                _sendStop = null;
                StopSendButton.Visibility = Visibility.Collapsed;
            }
        }

        private void StopSend_Click(object sender, RoutedEventArgs e)
        {
            if (_sendStop == null) return;
            _sendStop.Cancel();
            StopSendButton.IsEnabled = false;
            StatusText.Text = "Arrêt demandé : l'envoi s'arrête après le message en cours.";
        }

        /// <summary>
        /// Le serveur envoie (ses réglages OVH et SMTP) un message déjà réservé dans l'historique commun, et écrit le résultat.
        /// Sans réponse du serveur, le résultat reste « à vérifier » : le message ne sera pas renvoyé automatiquement.
        /// </summary>
        private async Task<SendResult> SendOneAsync(PreparedMessage message, bool test)
        {
            try
            {
                var result = await Server().InvokeAsync<SmsMailSendResult>("SmsMailSend", message, test).ConfigureAwait(false);
                return new SendResult(result.Status, result.ProviderId, result.Accepted);
            }
            catch (Exception ex)
            {
                return new SendResult(test ? "[TEST] Résultat non confirmé — vérifier le compte avant un nouvel essai. " + ex.Message : MessageSender.UncertainStatus, null, false);
            }
        }

        // ---------- Onglet « Message individuel » ----------

        private const string ReplyEmailSetting = "SmsMailReplyEmail";

        private string IndividualChannelValue => IndividualChannel?.SelectedItem as string ?? "SMS";

        private void IndividualPatient_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var query = sender.Text.Trim();
            sender.ItemsSource = query.Length < 2 ? null : _appointments
                .Where(r => !r.IsCancelled && ($"{r.Nom} {r.Prenom} {r.Prenom} {r.Nom}").Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Nom).ThenBy(r => r.HeureRDV).Take(20)
                .Select(r => new IndividualPatientChoice(r)).ToList();
        }

        private void IndividualPatient_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not IndividualPatientChoice choice) return;
            var r = choice.Rdv;
            IndividualNameText.Text = $"{r.Prenom} {r.Nom}".Trim();
            IndividualPhoneText.Text = new[] { r.Tel1, r.Tel2 }.Select(MessagePreparation.NormalizePhone).FirstOrDefault(p => p.Length > 0) ?? string.Empty;
            IndividualEmailText.Text = r.Email?.Trim() ?? string.Empty;
            IndividualDatePicker.Date = new DateTimeOffset(r.DateRDV.Date);
            IndividualTimePicker.SelectedTime = r.HeureRDV.TimeOfDay;
            RefreshIndividualAutoText();
        }

        private sealed class IndividualPatientChoice(RdvInfo rdv)
        {
            public RdvInfo Rdv { get; } = rdv;
            public override string ToString() => $"{Rdv.Nom} {Rdv.Prenom} — {Rdv.DateRDV:dd/MM} {Rdv.HeureRDV:HH:mm}";
        }

        private void IndividualChannel_Changed(object sender, SelectionChangedEventArgs e) => UpdateIndividualFields();
        private void IndividualLink_Changed(object sender, RoutedEventArgs e)
        {
            var withLink = IndividualLinkCheck.IsChecked == true;
            if (withLink && string.IsNullOrWhiteSpace(IndividualReplyEmailText.Text))
                IndividualReplyEmailText.Text = Client.Helpers.AppSettings.Get(ReplyEmailSetting, string.Empty);
            // Texte rédigé automatiquement s'il n'a pas été modifié ; sinon le lien est ajouté ou retiré du texte saisi.
            if (!RefreshIndividualAutoText())
            {
                var before = IndividualText.Text.Trim();
                IndividualText.Text = IndividualMessage.ToggleLink(IndividualText.Text, withLink, ReadSettings().Cabinet,
                    IndividualAppointmentStart() ?? DateTime.Today.AddDays(1).AddHours(9));
                // Texte proposé sans date choisie : il reste « automatique » et suivra la date et l'heure choisies ensuite.
                if (before.Length == 0 || before == _individualGeneratedText.Trim()) _individualGeneratedText = IndividualText.Text;
            }
            UpdateIndividualFields();
        }
        private void IndividualText_Changed(object sender, TextChangedEventArgs e) => UpdateIndividualFields();

        private void UpdateIndividualFields()
        {
            if (IndividualPhoneText == null || IndividualEmailText == null || IndividualMetrics == null) return;
            var sms = IndividualChannelValue == "SMS";
            var withLink = IndividualLinkCheck.IsChecked == true;
            IndividualPhoneText.Visibility = sms ? Visibility.Visible : Visibility.Collapsed;
            IndividualEmailText.Visibility = sms ? Visibility.Collapsed : Visibility.Visible;
            IndividualSubjectText.Visibility = sms ? Visibility.Collapsed : Visibility.Visible;
            IndividualReplyEmailText.Visibility = withLink ? Visibility.Visible : Visibility.Collapsed;
            IndividualMetrics.Text = sms
                ? MessagePreparation.SmsMetrics(IndividualMessage.ForMetrics(IndividualText.Text, withLink)) + (withLink ? " (lien compris)" : "")
                : string.Empty;
        }

        private DateTime? IndividualAppointmentStart()
            => IndividualDatePicker.Date is DateTimeOffset date && IndividualTimePicker.SelectedTime is TimeSpan time ? date.Date + time : null;

        // Dernier texte rédigé automatiquement : tant que la zone le contient encore, il suit la date, l'heure et la case.
        private string _individualGeneratedText = string.Empty;

        private bool RefreshIndividualAutoText()
        {
            var text = IndividualMessage.AutoText(IndividualText.Text, _individualGeneratedText, ReadSettings().Cabinet,
                IndividualAppointmentStart(), IndividualLinkCheck.IsChecked == true);
            if (text == null) return false;
            _individualGeneratedText = text;
            IndividualText.Text = text;
            return true;
        }

        private void IndividualDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => RefreshIndividualAutoText();
        private void IndividualTime_Changed(TimePicker sender, TimePickerSelectedValueChangedEventArgs args) => RefreshIndividualAutoText();

        private void IndividualDefaultText_Click(object sender, RoutedEventArgs e)
        {
            var start = IndividualAppointmentStart() ?? DateTime.Today.AddDays(1).AddHours(9);
            _individualGeneratedText = IndividualMessage.DefaultText(ReadSettings().Cabinet, start, IndividualLinkCheck.IsChecked == true);
            IndividualText.Text = _individualGeneratedText;
        }

        private async void SendIndividual_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            IndividualStatus.Text = string.Empty;
            var settings = ReadSettings();
            var channel = IndividualChannelValue;
            var withLink = IndividualLinkCheck.IsChecked == true;
            var start = IndividualAppointmentStart();
            if (withLink && start == null) throw new InvalidOperationException("Indiquez la date et l’heure du rendez-vous : elles s’affichent sur la page de réponse.");
            if (!IndividualMessage.HasTextBesidesLink(IndividualText.Text))
                throw new InvalidOperationException("Écrivez un message (nom du cabinet, date et heure du rendez-vous) : un lien seul ne dit pas au patient qui lui écrit. « Texte par défaut » propose un exemple.");
            var replyEmail = IndividualReplyEmailText.Text.Trim();
            if (withLink && !System.Net.Mail.MailAddress.TryCreate(replyEmail, out _))
                throw new InvalidOperationException("Indiquez l’adresse mail qui recevra la réponse du patient.");
            var message = IndividualMessage.Create(settings, channel, IndividualNameText.Text, IndividualPhoneText.Text, IndividualEmailText.Text,
                start ?? DateTime.Today, IndividualText.Text, IndividualSubjectText.Text);
            var connection = App.ChatService.GetSmsMailHistoryConnection();
            await connection.InvokeAsync("SmsMailValidate", new List<PreparedMessage> { message });
            if (withLink && string.IsNullOrEmpty(await connection.InvokeAsync<string>("SmsMailReplyLinkBase")))
                throw new InvalidOperationException("Le service de réponse n’est pas configuré sur le serveur (administration du serveur → Réponses des patients).");

            var preview = channel == "SMS" ? IndividualMessage.ForMetrics(IndividualText.Text.Trim(), false) : IndividualText.Text.Trim();
            var dialog = new ContentDialog
            {
                Title = "Envoyer ce message ?",
                Content = new ScrollViewer
                {
                    MaxHeight = 400,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                        Text = $"{(channel == "SMS" ? "SMS" : "Mail")} à {message.Contact}{(message.PeopleDisplay.Length > 0 ? " (" + message.PeopleDisplay + ")" : "")}\n\n{preview}"
                            + (withLink ? $"\n\n[LIEN] sera remplacé par le lien « Confirmer / Annuler ». La réponse sera envoyée à {replyEmail}." : "")
                    }
                },
                PrimaryButtonText = "Envoyer", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (withLink)
            {
                Client.Helpers.AppSettings.Set(ReplyEmailSetting, replyEmail);
                var url = await connection.InvokeAsync<string>("SmsMailCreateReplyLink", new EyeChat.SmsMail.ReplyLinkRequest
                {
                    MessageKey = message.Key, Channel = channel, Contact = message.Contact, People = message.PeopleDisplay,
                    AppointmentStart = start!.Value, Cabinet = settings.Cabinet, CabinetAddress = settings.CabinetAddress, ReplyEmail = replyEmail
                });
                message.Body = IndividualMessage.WithLink(message.Body, IndividualMessage.LinkFor(channel, url));
            }

            var outcome = await SendInBackgroundAsync(new[] { message }, m => SendOneAsync(m, test: false));
            await ReloadHistoryAsync();
            IndividualStatus.Text = outcome.Accepted == 1
                ? $"Message envoyé à {message.Contact}. {message.Status}" + (withLink ? $" La réponse du patient arrivera à {replyEmail} et dans l’historique." : "")
                : $"Message non confirmé : {message.Status}";
            StatusText.Text = IndividualStatus.Text;
            if (outcome.Accepted == 1) { IndividualText.Text = string.Empty; _individualGeneratedText = string.Empty; }
        });

        private async void MailReports_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            // Le serveur lit la boîte IMAP avec ses réglages.
            var matched = await Server().InvokeAsync<int>("SmsMailApplyMailReports");
            if (matched < 0) { StatusText.Text = "Aucun mail envoyé depuis ce compte dans l'historique."; return; }
            await ReloadHistoryAsync();
            StatusText.Text = $"{matched} retour(s) mail rapproché(s). La boîte IMAP est lue sans suppression ni modification des messages.";
        });

        private async void ImportHistory_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            if (App.MainWindow == null) return;
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".db");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync(); if (file == null) return;
            var count = await _history.ImportEyeEmailAsync(file.Path);
            await ReloadHistoryAsync();
            StatusText.Text = $"{count} entrée(s) EyeEmail importée(s). La base d'origine est conservée.";
        });

        private async Task ReloadHistoryAsync()
        {
            // Microsoft.Data.Sqlite exécute ses méthodes « Async » de façon synchrone : lecture hors du fil de l'interface.
            _entries = await Task.Run(() => _history.LoadAsync());
            FilterHistory();
        }

        private static string FilterChannel(ComboBox filter) => (filter?.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        private List<PreparedMessage> VisibleMessages() => MessageSelection.Visible(_messages, FilterChannel(SendChannelFilter), SendSearch?.Text ?? string.Empty);
        private void FilterMessages()
        {
            if (MessagesList == null || SendChannelFilter == null || SendSearch == null) return;

            var visible = VisibleMessages();
            MessagesList.ItemsSource = null;
            MessagesList.ItemsSource = visible;

            UpdateSelectionSummary();
        }
        private void UpdateSelectionSummary()
        {
            if (SelectionSummary == null || EmptyMessagesPanel == null) return;
            var visible = VisibleMessages();
            var selected = MessageSelection.ForSending(visible);
            EmptyMessagesPanel.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyMessagesText.Text = _messages.Count == 0 ? "Vos messages apparaîtront ici" : "Aucun résultat pour ces filtres";
            SelectionSummary.Text = _messages.Count == 0 ? "Aucun message préparé. Chargez les rendez-vous pour commencer."
                : visible.Count == 0 ? "Aucun message ne correspond aux filtres."
                : $"{visible.Count} affiché(s) sur {_messages.Count} · À envoyer : {selected.Count(m => m.Channel == "Mail")} mail(s) et {selected.Count(m => m.Channel == "SMS")} SMS";
        }
        private void SendFilter_Changed(object sender, SelectionChangedEventArgs e) => FilterMessages();
        private void SendSearch_Changed(object sender, TextChangedEventArgs e) => FilterMessages();
        private void MessageSelection_Changed(object sender, RoutedEventArgs e)
        {
            // Read after the two-way binding has updated the model.
            DispatcherQueue.TryEnqueue(UpdateSelectionSummary);
        }

        private void FilterFamilies()
        {
            if (FamiliesList == null || FamilySearch == null || FamilySizeFilter == null || FamiliesSummary == null || FamiliesEmpty == null) return;
            var query = FamilySearch.Text.Trim();
            var size = FamilySizeFilter.SelectedIndex;
            var visible = _families.Where(f => (size <= 0 || (size == 1 ? f.PersonCount == 1 : f.PersonCount >= size))
                && (f.TitleDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.MembersDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.PhonesDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.EmailsDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f.FirstAppointment).ThenBy(f => f.Numero).ToList();
            FamiliesList.ItemsSource = visible;
            FamiliesEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FamiliesEmpty.Text = _families.Count == 0 ? "Aucune famille préparée." : "Aucune famille ne correspond aux filtres.";
            FamiliesSummary.Text = _families.Count == 0
                ? "Chargez les rendez-vous et activez le regroupement des familles dans les options de préparation."
                : $"{visible.Count} famille(s) affichée(s) sur {_families.Count} · {visible.Sum(f => f.PersonCount)} personne(s) · Tri par premier rendez-vous. Ces filtres ne modifient pas les envois.";
        }
        private void FamilySearch_Changed(object sender, TextChangedEventArgs e) => FilterFamilies();
        private void FamilySize_Changed(object sender, SelectionChangedEventArgs e) => FilterFamilies();

        private void FilterHistory()
        {
            if (HistoryList == null || HistoryFilter == null) return;
            var visible = VisibleHistory();
            HistoryList.ItemsSource = visible;
            if (HistorySummary != null) HistorySummary.Text = _entries.Count == 0 ? "Aucun envoi enregistré sur ce poste."
                : visible.Count == 0 ? "Aucun envoi ne correspond aux filtres."
                : $"{visible.Count} envoi(s) affiché(s) sur {_entries.Count} · {visible.Count(h => h.Channel == "Mail")} mail(s) · {visible.Count(h => h.Channel == "SMS")} SMS";
        }
        private List<MessageHistoryEntry> VisibleHistory() => MessageExportReport.FilterHistory(_entries,
            HistoryDayPicker?.Date?.Date, FilterChannel(HistoryChannelFilter), HistoryFilter?.Text ?? string.Empty);
        private void HistoryDay_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => FilterHistory();
        private void ClearHistoryDay_Click(object sender, RoutedEventArgs e) { HistoryDayPicker.Date = null; FilterHistory(); }

        private async void ExportMessages_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez un jour de rendez-vous.");
            if (_preparedSignature != MessagePreparation.PreparationSignature(ReadSettings()))
                throw new InvalidOperationException("Les options ont changé. Préparez à nouveau les messages avant d’exporter.");
            var rows = VisibleMessages();
            if (rows.Count == 0) throw new InvalidOperationException("Aucun message affiché à exporter.");
            var sheets = new List<XlsxSheet> { MessageExportReport.PreparedSheet(rows) };
            if (_families.Count > 0) sheets.Add(MessageExportReport.FamiliesSheet(_families));
            if (await SaveFileAsync($"Rappels_{day:yyyy-MM-dd}", "Classeur Excel", ".xlsx", XlsxWriter.Create(sheets)))
                StatusText.Text = $"{rows.Count} message(s) exportés dans le classeur (lignes visibles, filtres appliqués).";
        });

        private async void ExportHistoryDay_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var day = HistoryDayPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez le jour d’envoi à exporter dans l’historique.");
            await ReloadHistoryAsync();
            var rows = VisibleHistory();
            if (rows.Count == 0) throw new InvalidOperationException("Aucun envoi pour ce jour et ces filtres.");
            var sheet = MessageExportReport.HistorySheet(rows, day);
            if (await SaveFileAsync($"Envois_{day:yyyy-MM-dd}", "Classeur Excel", ".xlsx", XlsxWriter.Create(new[] { sheet })))
                StatusText.Text = $"{sheet.Rows.Count} envoi(s) du {day:dd/MM/yyyy} exportés.";
        });

        private async void ExportDiagnostic_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez un jour de rendez-vous.");
            if (_working.Count == 0) throw new InvalidOperationException("Chargez et préparez les rendez-vous avant d’exporter un diagnostic.");
            if (_preparedSignature != MessagePreparation.PreparationSignature(ReadSettings()))
                throw new InvalidOperationException("Les options ont changé. Préparez à nouveau les messages avant d’exporter.");

            var password = new PasswordBox { Header = $"Mot de passe (au moins {DiagnosticPackage.MinimumPasswordLength} caractères)" };
            var confirm = new PasswordBox { Header = "Confirmer le mot de passe" };
            var pseudonymize = new CheckBox { Content = "Remplacer noms, téléphones, e-mails et adresses par des pseudonymes (recommandé)", IsChecked = true };
            var note = new TextBox { Header = "Problème constaté (facultatif)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
            var error = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Firebrick), TextWrapping = TextWrapping.Wrap };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Diagnostic chiffré",
                PrimaryButtonText = "Choisir l’emplacement…", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    MaxHeight = 520,
                    Content = new StackPanel
                    {
                        Spacing = 10, MaxWidth = 520,
                        Children =
                        {
                            new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Le fichier contient les rendez-vous du jour, les familles formées et la liste des mails et SMS préparés. "
                                + "Les clés OVH et le mot de passe de messagerie ne sont jamais inclus. Transmettez le mot de passe séparément du fichier." },
                            password, confirm, pseudonymize,
                            new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
                                Text = "Avec les pseudonymes, une même valeur donne toujours le même pseudonyme : les doublons restent visibles, mais une faute de frappe dans une adresse ou un nom n’est plus reconnaissable. "
                                    + "Décochez seulement si le problème vient de l’orthographe exacte." },
                            note, error
                        }
                    }
                }
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (password.Password.Length < DiagnosticPackage.MinimumPasswordLength) { error.Text = $"Le mot de passe doit contenir au moins {DiagnosticPackage.MinimumPasswordLength} caractères."; args.Cancel = true; }
                else if (password.Password != confirm.Password) { error.Text = "Les deux mots de passe sont différents."; args.Cancel = true; }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var input = new DiagnosticPackage.Input
            {
                Day = day, Appointments = _working.ToList(), Families = _families.ToList(), Messages = _messages.ToList(),
                History = _appointmentHistory, Settings = ReadSettings(), Pseudonymize = pseudonymize.IsChecked == true, Note = note.Text
            };
            var secret = password.Password;
            StatusText.Text = "Chiffrement du diagnostic…";
            var bytes = await Task.Run(() => DiagnosticPackage.Encrypt(DiagnosticPackage.BuildJson(input), secret));
            if (await SaveFileAsync($"Diagnostic_SMS_Mail_{day:yyyy-MM-dd}", "Diagnostic chiffré", DiagnosticPackage.Extension, bytes))
                StatusText.Text = $"Diagnostic chiffré enregistré ({_working.Count} RDV, {_families.Count} famille(s), {_messages.Count} message(s)"
                    + (input.Pseudonymize ? ", pseudonymisé)." : ", données réelles).") + " Envoyez le mot de passe séparément.";
        });

        private async Task<bool> SaveFileAsync(string suggestedName, string description, string extension, byte[] content)
        {
            if (App.MainWindow == null) return false;
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add(description, new List<string> { extension });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await FileIO.WriteBytesAsync(file, content);
            return true;
        }

        private void HistoryFilter_TextChanged(object sender, TextChangedEventArgs e) => FilterHistory();
        private void HistoryChannel_Changed(object sender, SelectionChangedEventArgs e) => FilterHistory();
        // Reading a message is independent of the other actions: it opens even while
        // something else is running, and any problem is shown in the history tab itself.
        private bool _previewOpen;
        private async void HistoryMessage_Click(object sender, RoutedEventArgs e)
        {
            if (_previewOpen || (sender as FrameworkElement)?.DataContext is not MessageHistoryEntry entry) return;
            _previewOpen = true;
            try
            {
                var body = string.IsNullOrWhiteSpace(entry.Body) ? "Le texte de ce message n’a pas été enregistré dans l’historique." : entry.Body;
                await ShowTextPreviewAsync(entry.Channel, entry.Contact + " · " + entry.CreatedAt + " · " + entry.Status, body);
            }
            catch (Exception ex)
            {
                var message = "Impossible d’afficher ce message : " + ex.Message;
                HistorySummary.Text = message;
                StatusText.Text = message;
            }
            finally { _previewOpen = false; }
        }

        private async void History_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            await ReloadHistoryAsync();
            // Le serveur interroge OVH avec ses réglages et met à jour l'historique commun.
            var counts = await Server().InvokeAsync<int[]>("SmsMailRefreshSmsStatuses");
            if (counts[0] == 0 && counts[1] == 0) { StatusText.Text = "Historique à jour. Aucun SMS en attente de statut."; return; }
            var errors = counts[1];
            await ReloadHistoryAsync();
            StatusText.Text = $"Statuts SMS actualisés ; {errors} statut(s) indisponible(s). Utilisez Retours mail (IMAP) pour lire les notifications mail.";
        });

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetMessageSelection(true);
        private void SelectNone_Click(object sender, RoutedEventArgs e) => SetMessageSelection(false);
        private void SetMessageSelection(bool selected)
        {
            var processed = _entries.Select(entry => entry.Key).ToHashSet();
            foreach (var message in VisibleMessages()) message.Selected = selected && message.CanSend && !processed.Contains(message.Key);
            FilterMessages();
            StatusText.Text = $"{MessageSelection.ForSending(VisibleMessages()).Count} message(s) visible(s) sélectionné(s).";
        }

        private void ClearMessages()
        {
            _messages.Clear();
            UpdateSelectionSummary();
            _preparedSignature = string.Empty;
            if (MessagesList != null) MessagesList.ItemsSource = null;

            if (TimeChangesText != null) TimeChangesText.Text = "Préparez les messages pour vérifier les horaires ajustés.";
            if (SummaryText != null) SummaryText.Text = "Préparez les messages avant l'envoi.";
        }

        private void DayPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ClearMessages();
            _appointments.Clear();
            _working.Clear();
            _families.Clear(); _appointmentHistory.Clear(); _familyDecisions.Clear();
            FilterFamilies();
            if (AppointmentsList != null) AppointmentsList.ItemsSource = null;
        }

        private void Options_Changed(object sender, RoutedEventArgs e) => ClearMessages();
        private void SmsFamilySize_Changed(object sender, SelectionChangedEventArgs e) => ClearMessages();

        private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            if (App.MainWindow == null) return;
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var appointments = JsonSerializer.Deserialize<List<RdvInfo>>(await FileIO.ReadTextAsync(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Le fichier doit contenir un tableau de rendez-vous.");
            foreach (var rdv in appointments)
            {
                if (rdv == null || rdv.DateRDV == DateTime.MinValue || rdv.HeureRDV == DateTime.MinValue)
                    throw new InvalidDataException("Chaque rendez-vous doit préciser DateRDV et HeureRDV.");
                rdv.AdditionalEmails ??= new();
                rdv.Tel1 ??= string.Empty;
                rdv.Tel2 ??= string.Empty;
                rdv.IsCancelled |= (rdv.Remarque ?? string.Empty).Contains("annul", StringComparison.OrdinalIgnoreCase);
            }
            ClearMessages();
            foreach (var row in appointments)
            {
                EyeEmailRecordMapper.NormalizeContactDetails(row);
                EyeEmailRecordMapper.ApplyRemarkBasedAdjustments(row);
            }
            _appointmentHistory.Clear(); _familyDecisions.Clear();
            _appointments = EyeEmailRecordMapper.ConsolidateDuplicateAppointments(appointments);
            AppointmentsList.ItemsSource = _appointments;
            await PrepareAsync();
        });

        private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            if (App.MainWindow == null) return;
            await ReloadHistoryAsync();
            var picker = new FileSavePicker { SuggestedFileName = $"SMS_Mail_{DateTime.Now:yyyyMMdd}" };
            picker.FileTypeChoices.Add("Rapport CSV", new List<string> { ".csv" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            string Cell(string value)
            {
                // Neutralise spreadsheet formulas in imported names/content.
                if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            var csv = new StringBuilder("Canal;Contact;Date;Statut;Réponse du patient;Message\r\n");
            foreach (var entry in _entries) csv.AppendLine(string.Join(";", new[] { entry.Channel, entry.Contact, entry.CreatedAt, entry.Status, entry.Reply, entry.Body }.Select(Cell)));
            await FileIO.WriteTextAsync(file, "\uFEFF" + csv);
            StatusText.Text = "Historique exporté.";
        });
    }
}


