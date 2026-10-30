using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Client.Helpers;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
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
        private readonly MessageHistory _history;
        private List<RdvInfo> _appointments = new();
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

        public SmsMailPage()
        {
            InitializeComponent();
            Directory.CreateDirectory(SmsMailSettingsStore.DirectoryPath);
            _history = new MessageHistory(Path.Combine(SmsMailSettingsStore.DirectoryPath, "history.db"));
            Loaded += Page_Loaded;
            Unloaded += (_, _) => _unloaded = true;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _unloaded = false;
            if (_loaded) return;
            _loaded = true;
            await RunAsync(async () =>
            {
                _settings = SmsMailSettingsStore.Load();
                DayPicker.Date = DateTimeOffset.Now.AddDays(1);
                CabinetText.Text = _settings.Cabinet;
                CabinetAddressText.Text = _settings.CabinetAddress;
                DurationNumber.Value = _settings.AppointmentDurationMinutes;
                SmsOnlyNoEmail.IsChecked = _settings.SmsOnlyWhenNoEmail;
                SmsFamilySizeFilter.SelectedIndex = Math.Clamp(_settings.SmsMinimumFamilySize, 1, 3) - 1;
                UseFamiliesCheck.IsChecked = _settings.UseEyeEmailFamilies;
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
                await ReloadHistoryAsync();
                StatusText.Text = "Sélectionnez un jour et chargez les rendez-vous. Vérifiez vos paramètres avant le premier envoi.";
            });
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
            var config = await App.ChatService.GetAppointmentSearchConfigAsync()
                ?? throw new InvalidOperationException("Configurez la base Access dans les paramètres RDV.");
            _appointmentHistory = await AppointmentLoader.LoadHistoryAsync(config, MachineConfig.Load(), DateTime.Today);
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
            _appointmentHistory.Clear(); _familyDecisions.Clear();
            AppointmentsList.ItemsSource = null;
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Sélectionnez une date.");
            StatusText.Text = "Chargement des rendez-vous…";
            var config = await App.ChatService.GetAppointmentSearchConfigAsync()
                ?? throw new InvalidOperationException("Configurez la base Access dans les paramètres RDV.");
            _appointments = await AppointmentLoader.LoadAsync(config, MachineConfig.Load(), day);
            AppointmentsList.ItemsSource = _appointments;
            await PrepareAsync();
        });

        private async Task PrepareAsync()
        {
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Sélectionnez une date.");
            _settings = ReadSettings();
            var working = JsonSerializer.Deserialize<List<RdvInfo>>(JsonSerializer.Serialize(_appointments)) ?? new();
            if (_settings.UseEyeEmailFamilies && !string.IsNullOrWhiteSpace(_settings.FamilyDatabasePath))
                await EyeEmailFamilyDatabase.ApplyAsync(_settings.FamilyDatabasePath, MachineConfig.Load(), working, day);
            _families = _settings.UseEyeEmailFamilies
                ? await Task.Run(() => EyeEmailFamilyGrouping.Build(working.Where(r => r.DateRDV.Date == day), _appointmentHistory,
                    _appointmentHistory.Count > 0 ? ConfirmHistoricalFamily : null)) : new();
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
            var sent = await _history.LoadReservedKeysAsync();
            foreach (var message in _messages.Where(m => sent.Contains(m.Key) || m.ReservationKeys.Any(sent.Contains)))
            {
                message.Selected = false;
                message.CanSend = false;
                message.Status = "Déjà traité — consulter l'historique";
            }
            FilterMessages();
            SummaryText.Text = $"{_messages.Count(m => m.Channel == "Mail")} mail(s), {_messages.Count(m => m.Channel == "SMS")} SMS";
            StatusText.Text = $"{_families.Count} famille(s) EyeEmail ; {_appointments.Count} RDV chargés, {_appointments.Count(r => r.IsCancelled)} annulé(s) exclus. Vérifiez les destinataires et les messages avant l'envoi.";
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
            var text = new TextBox
            {
                Text = channel == "Mail" ? MailTextPreview.Convert(body) : body,
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontSize = 16, Padding = new Thickness(16),
                Height = Math.Clamp(XamlRoot.Size.Height - 250, 160, 650),
                Width = Math.Clamp(XamlRoot.Size.Width - 120, 240, 820)
            };
            ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(text);
            var dialog = new ContentDialog { Title = channel == "Mail" ? "Mail · aperçu texte" : "SMS", Content = panel, CloseButtonText = "Fermer", XamlRoot = XamlRoot };
            dialog.Resources["ContentDialogMaxWidth"] = 920.0;
            await dialog.ShowAsync();
        }

        private async void Prepare_Click(object sender, RoutedEventArgs e) => await RunAsync(PrepareAsync);

        private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            _settings = ReadSettings();
            SmsMailSettingsStore.Save(_settings);
            ClearMessages();
            await Task.CompletedTask;
            StatusText.Text = "Enregistré. Préparez à nouveau les messages pour appliquer les modifications.";
        });

        private void ApplyEmailModel_Click(object sender, RoutedEventArgs e)
        {
            EmailTemplateText.Text = SmsMailSettings.DefaultEmailTemplate;
            ClearMessages();
            StatusText.Text = "Modèle du mail joint appliqué. Enregistrez les modèles puis préparez les messages.";
        }

        private async void Balance_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var balance = await new SmsService(ReadSettings().Sms).GetSmsBalanceAsync();
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
                    if (prepared.Any(m => m.Channel == "SMS")) new SmsService(settings.Sms).Validate();
                    if (prepared.Any(m => m.Channel == "Mail"))
                    {
                        var mail = new MailService(settings.Email);
                        mail.Validate();
                        foreach (var item in prepared.Where(m => m.Channel == "Mail"))
                            using (mail.CreateMessage(item)) { }
                    }
                }
                catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || prepared == null || settings == null) return;
            var outcomes = new List<string>();
            foreach (var message in prepared)
            {
                if (_unloaded) break;
                if (!await _history.ReserveAsync(message)) continue;
                string status;
                long? providerId = null;
                try
                {
                    if (message.Channel == "Mail")
                    {
                        await new MailService(settings.Email).SendAsync(message);
                        status = "[TEST] Accepté par SMTP — réception non confirmée";
                    }
                    else
                    {
                        var response = await new SmsService(settings.Sms).SendSmsAsync(message.Contact, message.Body);
                        if (response.Ids.Length > 0 && response.ValidReceivers.Length > 0)
                        {
                            providerId = response.Ids[0];
                            status = "[TEST] Accepté par OVH — réception en attente";
                        }
                        else status = "[TEST] Non accepté par OVH — vérifier le compte";
                    }
                }
                catch (Exception ex)
                {
                    status = "[TEST] Résultat non confirmé — vérifier le compte avant un nouvel essai. " + ex.Message;
                }
                await _history.UpdateAsync(message.Key, status, providerId);
                outcomes.Add($"{message.Channel} vers {message.Contact} : {status}");
            }
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
            var sms = new SmsService(settings.Sms);
            var mail = new MailService(settings.Email);
            if (selected.Any(m => m.Channel == "SMS")) sms.Validate();
            if (selected.Any(m => m.Channel == "Mail"))
            {
                mail.Validate();
                foreach (var item in selected.Where(m => m.Channel == "Mail"))
                    using (mail.CreateMessage(item)) { }
            }
            if (selected.Any(m => string.IsNullOrWhiteSpace(m.Body))) throw new InvalidOperationException("Un message est vide. Vérifiez les modèles.");
            var dialog = new ContentDialog
            {
                Title = "Confirmer l'envoi",
                Content = $"Envoyer {selected.Count(m => m.Channel == "Mail")} mail(s) et {selected.Count(m => m.Channel == "SMS")} SMS pour le {DayPicker.Date:dd/MM/yyyy} ? Les SMS peuvent consommer plusieurs crédits.\n\nLes contacts et contenus sélectionnés seront envoyés immédiatement.",
                PrimaryButtonText = "Envoyer", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            int accepted = 0, skipped = 0, uncertain = 0;
            foreach (var message in selected)
            {
                if (_unloaded) break;
                if (!await _history.ReserveAsync(message)) { skipped++; message.Selected = false; message.CanSend = false; message.Status = "Déjà traité pour ce contact ou cette famille"; continue; }
                string status;
                long? providerId = null;
                try
                {
                    if (message.Channel == "SMS")
                    {
                        var response = await sms.SendSmsAsync(message.Contact, message.Body);
                        if (response.Ids.Length > 0 && response.ValidReceivers.Length > 0)
                        {
                            providerId = response.Ids[0];
                            status = "Accepté par OVH — réception en attente";
                            accepted++;
                        }
                        else { status = "Non accepté par OVH — vérifier le compte avant tout nouvel essai"; uncertain++; }
                    }
                    else
                    {
                        await mail.SendAsync(message);
                        status = "Accepté par SMTP — réception non confirmée";
                        accepted++;
                    }
                }
                catch (Exception)
                {
                    status = "Résultat non confirmé — vérifier le compte OVH/SMTP avant tout nouvel essai";
                    uncertain++;
                }
                // Separate persistence from submission: a log failure must never
                // trigger a second network send after the provider accepted it.
                await _history.UpdateAsync(message.Key, status, providerId);
                message.CanSend = false;
                message.Status = status;
                message.Selected = false;
            }
            FilterMessages();
            await ReloadHistoryAsync();
            StatusText.Text = $"{accepted} accepté(s), {skipped} déjà traité(s), {uncertain} à vérifier.";
        });

        private async void MailReports_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var settings = ReadSettings();
            var since = await _history.EarliestMailAsync(settings.Email.SmtpHost);
            if (!since.HasValue) { StatusText.Text = "Aucun mail envoyé depuis ce compte dans l'historique local."; return; }
            var reports = await MailDeliveryReports.ReadAsync(settings.Email, since.Value);
            int matched = 0;
            foreach (var report in reports) matched += await _history.ApplyReportAsync(report, settings.Email.SmtpHost);
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
            _entries = await _history.LoadAsync();
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
        private List<MessageHistoryEntry> VisibleHistory() => MessagePrintReport.FilterHistory(_entries,
            HistoryDayPicker?.Date?.Date, FilterChannel(HistoryChannelFilter), HistoryFilter?.Text ?? string.Empty);
        private void HistoryDay_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => FilterHistory();
        private void ClearHistoryDay_Click(object sender, RoutedEventArgs e) { HistoryDayPicker.Date = null; FilterHistory(); }

        private async void PrintMessages_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var day = DayPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez un jour de rendez-vous.");
            if (_preparedSignature != MessagePreparation.PreparationSignature(ReadSettings()))
                throw new InvalidOperationException("Les options ont changé. Préparez à nouveau les messages avant d’imprimer.");
            var rows = VisibleMessages();
            if (rows.Count == 0) throw new InvalidOperationException("Aucun message affiché à imprimer.");
            await ShowPrintPreviewAsync(MessagePrintReport.Prepared(rows, day));
        });

        private async void PrintHistory_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var day = HistoryDayPicker.Date?.Date ?? throw new InvalidOperationException("Choisissez le jour d’envoi à imprimer dans l’historique.");
            await ReloadHistoryAsync();
            var rows = VisibleHistory();
            if (rows.Count == 0) throw new InvalidOperationException("Aucun envoi pour ce jour et ces filtres.");
            await ShowPrintPreviewAsync(MessagePrintReport.History(rows, day));
        });

        private async Task ShowPrintPreviewAsync(string html)
        {
            if (Encoding.UTF8.GetByteCount(html) > 1_900_000)
                throw new InvalidOperationException("La liste est trop volumineuse. Réduisez les résultats avec les filtres avant d’imprimer.");
            var browser = new WebView2
            {
                Width = Math.Max(280, Math.Min(950, XamlRoot.Size.Width - 120)),
                Height = Math.Max(200, Math.Min(600, XamlRoot.Size.Height - 220))
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Liste imprimable — A4 paysage",
                Content = browser, PrimaryButtonText = "Imprimer / PDF", CloseButtonText = "Fermer",
                IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close
            };
            dialog.Resources["ContentDialogMaxWidth"] = 1100d;
            bool initialized = false;
            bool closed = false;
            dialog.Closed += (_, _) => closed = true;
            browser.Loaded += async (_, _) =>
            {
                if (initialized) return;
                initialized = true;
                try
                {
                    await browser.EnsureCoreWebView2Async();
                    if (closed || _unloaded) return;
                    browser.CoreWebView2.Settings.IsScriptEnabled = false;
                    browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    browser.CoreWebView2.NavigationStarting += (_, args) =>
                    {
                        if (args.Uri != "about:blank") args.Cancel = true;
                    };
                    browser.CoreWebView2.NavigationCompleted += (_, args) =>
                    {
                        if (closed || _unloaded) return;
                        dialog.IsPrimaryButtonEnabled = args.IsSuccess;
                        if (!args.IsSuccess) dialog.Title = "Impossible de préparer l’impression";
                    };
                    browser.NavigateToString(html);
                }
                catch (Exception) { if (!closed && !_unloaded) dialog.Title = "Aperçu indisponible — vérifiez WebView2"; }
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try { browser.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
                catch (Exception) { dialog.Title = "Impression indisponible — réessayez ou vérifiez WebView2"; }
            };
            try { await dialog.ShowAsync(); }
            finally { closed = true; browser.Close(); }
        }

        private void HistoryFilter_TextChanged(object sender, TextChangedEventArgs e) => FilterHistory();
        private void HistoryChannel_Changed(object sender, SelectionChangedEventArgs e) => FilterHistory();
        private async void HistoryMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not MessageHistoryEntry entry) return;
            await RunAsync(() => ShowTextPreviewAsync(entry.Channel, entry.Contact + " · " + entry.CreatedAt + " · " + entry.Status, entry.Body));
        }

        private async void History_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            await ReloadHistoryAsync();
            var settings = ReadSettings();
            var pending = _entries.Where(h => h.ProviderId.HasValue && h.ProviderService == settings.Sms.ServiceName
                && !h.Status.StartsWith("Reçu", StringComparison.Ordinal)).ToList();
            if (pending.Count == 0) { StatusText.Text = "Historique à jour. Aucun SMS en attente de statut."; return; }
            var sms = new SmsService(settings.Sms);
            int errors = 0;
            foreach (var entry in pending)
            {
                try
                {
                    var detail = await sms.GetSmsOutgoingAsync(entry.ProviderId!.Value);
                    var status = detail.DeliveredAt.HasValue || detail.DeliveryReceipt == 1
                        ? "Reçu — " + (detail.DeliveredAt?.ToLocalTime().ToString("g") ?? "confirmation OVH")
                        : "OVH : " + (detail.Status ?? "réception en attente");
                    await _history.UpdateAsync(entry.Key, status);
                }
                catch { errors++; }
            }
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
            var csv = new StringBuilder("Canal;Contact;Date;Statut;Message\r\n");
            foreach (var entry in _entries) csv.AppendLine(string.Join(";", new[] { entry.Channel, entry.Contact, entry.CreatedAt, entry.Status, entry.Body }.Select(Cell)));
            await FileIO.WriteTextAsync(file, "\uFEFF" + csv);
            StatusText.Text = "Historique exporté.";
        });
    }
}


