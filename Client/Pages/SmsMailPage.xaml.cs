using System;
using System.Collections.Generic;
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
                SmsOnlyNoEmail.IsChecked = _settings.SmsOnlyWhenNoEmail;
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
            if (double.IsNaN(SmtpPortNumber.Value)) throw new InvalidOperationException("Renseignez le port SMTP.");
            return new SmsMailSettings
            {
                Cabinet = CabinetText.Text.Trim(), SmsOnlyWhenNoEmail = SmsOnlyNoEmail.IsChecked == true,
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

        private async void Load_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            ClearMessages();
            _appointments.Clear();
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
            _messages = MessagePreparation.Prepare(_appointments, day, _settings);
            await ReloadHistoryAsync();
            var sent = _entries.Select(h => h.Key).ToHashSet();
            foreach (var message in _messages.Where(m => sent.Contains(m.Key)))
            {
                message.Selected = false;
                message.Status = "Déjà traité — consulter l'historique";
            }
            MessagesList.ItemsSource = _messages;
            MessagesList.SelectedItem = _messages.FirstOrDefault();
            SummaryText.Text = $"{_messages.Count(m => m.Channel == "Mail")} mail(s), {_messages.Count(m => m.Channel == "SMS")} SMS";
            StatusText.Text = $"{_appointments.Count} RDV chargés, {_appointments.Count(r => r.IsCancelled)} annulé(s) exclus. Vérifiez les destinataires et les messages avant l'envoi.";
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

        private async void Balance_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var balance = await new SmsService(ReadSettings().Sms).GetSmsBalanceAsync();
            StatusText.Text = $"Solde OVH : {balance} crédit(s) SMS.";
        });

        private async void Send_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var selected = _messages.Where(m => m.Selected).ToList();
            if (selected.Count == 0) { StatusText.Text = "Sélectionnez au moins un message à envoyer."; return; }
            var settings = ReadSettings();
            if (string.IsNullOrWhiteSpace(settings.Cabinet)) throw new InvalidOperationException("Renseignez le nom du cabinet puis préparez les messages.");
            // Require a fresh preparation if a model or the fallback rule changed.
            if (settings.SmsTemplate != _settings.SmsTemplate || settings.EmailTemplate != _settings.EmailTemplate
                || settings.Cabinet != _settings.Cabinet || settings.Subject != _settings.Subject)
                throw new InvalidOperationException("Les modèles ont changé. Préparez à nouveau les messages.");
            var sms = new SmsService(settings.Sms);
            var mail = new MailService(settings.Email);
            if (selected.Any(m => m.Channel == "SMS")) sms.Validate();
            if (selected.Any(m => m.Channel == "Mail")) mail.Validate();
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
                if (!await _history.ReserveAsync(message)) { skipped++; continue; }
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
                message.Status = status;
                message.Selected = false;
            }
            MessagesList.ItemsSource = null;
            MessagesList.ItemsSource = _messages;
            await ReloadHistoryAsync();
            StatusText.Text = $"{accepted} accepté(s), {skipped} déjà traité(s), {uncertain} à vérifier.";
        });

        private async Task ReloadHistoryAsync()
        {
            _entries = await _history.LoadAsync();
            FilterHistory();
        }

        private void FilterHistory()
        {
            if (HistoryList == null || HistoryFilter == null) return;
            var filter = HistoryFilter.Text;
            HistoryList.ItemsSource = _entries.Where(h => h.Contact.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || h.Status.Contains(filter, StringComparison.OrdinalIgnoreCase) || h.Channel.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void HistoryFilter_TextChanged(object sender, TextChangedEventArgs e) => FilterHistory();

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
            StatusText.Text = $"Statuts SMS actualisés ; {errors} statut(s) indisponible(s). Les notifications mail sont reçues dans la boîte de l'expéditeur.";
        });

        private void MessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MessagePreview != null) MessagePreview.Text = (MessagesList.SelectedItem as PreparedMessage)?.Body ?? string.Empty;
        }

        private void ClearMessages()
        {
            _messages.Clear();
            if (MessagesList != null) MessagesList.ItemsSource = null;
            if (MessagePreview != null) MessagePreview.Text = string.Empty;
            if (SummaryText != null) SummaryText.Text = "Préparez les messages avant l'envoi.";
        }

        private void DayPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ClearMessages();
            _appointments.Clear();
            if (AppointmentsList != null) AppointmentsList.ItemsSource = null;
        }

        private void Options_Changed(object sender, RoutedEventArgs e) => ClearMessages();

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
            _appointments = appointments;
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
