using System.Globalization;
using System.Text;
using ChatServeur.SmsMail;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

/// <summary>Formulaire de la page : valeurs simples (les listes sont saisies une par ligne), les secrets vides gardent ceux du serveur.</summary>
public sealed class SmsMailForm
{
    public string Cabinet { get; set; } = "";
    public string CabinetAddress { get; set; } = "";
    public string CabinetSpecialty { get; set; } = "";
    public string CabinetPhone { get; set; } = "";
    public string CabinetFax { get; set; } = "";
    public string CabinetContact { get; set; } = "";
    public int AppointmentDurationMinutes { get; set; } = 30;
    public string Subject { get; set; } = "";
    public string SmsTemplate { get; set; } = "";
    public string EmailTemplate { get; set; } = "";
    public bool SmsOnlyWhenNoEmail { get; set; }
    public int SmsMinimumFamilySize { get; set; } = 1;
    public bool UseEyeEmailFamilies { get; set; }
    public bool LogFamilyQuestions { get; set; }
    public string FamilyDatabasePath { get; set; } = "";
    public bool AdjustReminderTimes { get; set; }
    /// <summary>Une règle par ligne : « 11:30 → 11:20 ».</summary>
    public string ReminderTimeRules { get; set; } = "";
    public bool SendCalendarInvites { get; set; }
    public bool SendCalendarAlarms { get; set; }
    public bool CalendarIsBusy { get; set; }
    public string CalendarSubject { get; set; } = "";
    /// <summary>Une alerte par ligne : « 1440 ; Rappel la veille » (minutes avant le rendez-vous ; libellé).</summary>
    public string Alerts { get; set; } = "";
    public string SmsServiceName { get; set; } = "";
    public string SmsSender { get; set; } = "";
    public string SmsApplicationKey { get; set; } = "";
    public string? SmsApplicationSecret { get; set; }
    public string? SmsConsumerKey { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string MailUser { get; set; } = "";
    public string? MailPassword { get; set; }
    public string MailFrom { get; set; } = "";
    public string MailDisplayName { get; set; } = "";
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string BounceFilter { get; set; } = "";

    public static SmsMailForm From(SmsMailSettings s) => new()
    {
        Cabinet = s.Cabinet, CabinetAddress = s.CabinetAddress, CabinetSpecialty = s.CabinetSpecialty, CabinetPhone = s.CabinetPhone,
        CabinetFax = s.CabinetFax, CabinetContact = s.CabinetContact, AppointmentDurationMinutes = s.AppointmentDurationMinutes,
        Subject = s.Subject, SmsTemplate = s.SmsTemplate, EmailTemplate = s.EmailTemplate,
        SmsOnlyWhenNoEmail = s.SmsOnlyWhenNoEmail, SmsMinimumFamilySize = s.SmsMinimumFamilySize, UseEyeEmailFamilies = s.UseEyeEmailFamilies,
        LogFamilyQuestions = s.LogFamilyQuestions, FamilyDatabasePath = s.FamilyDatabasePath, AdjustReminderTimes = s.AdjustReminderTimes,
        ReminderTimeRules = string.Join("\n", s.ReminderTimeRules.Select(r => $"{r.From:hh\\:mm} → {r.To:hh\\:mm}")),
        SendCalendarInvites = s.SendCalendarInvites, SendCalendarAlarms = s.SendCalendarAlarms, CalendarIsBusy = s.CalendarIsBusy,
        CalendarSubject = s.CalendarSubject, Alerts = string.Join("\n", s.Alerts.Select(a => $"{a.OffsetMinutes} ; {a.Label}")),
        SmsServiceName = s.Sms.ServiceName, SmsSender = s.Sms.Sender, SmsApplicationKey = s.Sms.ApplicationKey,
        SmtpHost = s.Email.SmtpHost, SmtpPort = s.Email.SmtpPort, SmtpSsl = s.Email.EnableSsl, MailUser = s.Email.UserName,
        MailFrom = s.Email.FromAddress, MailDisplayName = s.Email.FromDisplayName, ImapHost = s.Email.ImapHost, ImapPort = s.Email.ImapPort,
        BounceFilter = s.Email.BounceFilter,
    };

    /// <summary>Applique le formulaire aux réglages actuels (ce que la page ne montre pas est gardé).</summary>
    public SmsMailSettings ApplyTo(SmsMailSettings current)
    {
        var s = current;
        if (Cabinet.Trim().Length == 0) throw new InvalidDataException("Indiquez le nom du cabinet.");
        if (AppointmentDurationMinutes is < 1 or > 480) throw new InvalidDataException("La durée d'un rendez-vous doit être comprise entre 1 et 480 minutes.");
        s.Cabinet = Cabinet.Trim(); s.CabinetAddress = (CabinetAddress ?? "").Trim(); s.CabinetSpecialty = (CabinetSpecialty ?? "").Trim();
        s.CabinetPhone = (CabinetPhone ?? "").Trim(); s.CabinetFax = (CabinetFax ?? "").Trim(); s.CabinetContact = (CabinetContact ?? "").Trim();
        s.AppointmentDurationMinutes = AppointmentDurationMinutes;
        s.Subject = Subject ?? ""; s.SmsTemplate = SmsTemplate ?? ""; s.EmailTemplate = EmailTemplate ?? "";
        s.SmsOnlyWhenNoEmail = SmsOnlyWhenNoEmail; s.SmsMinimumFamilySize = Math.Clamp(SmsMinimumFamilySize, 1, 3);
        s.UseEyeEmailFamilies = UseEyeEmailFamilies; s.LogFamilyQuestions = LogFamilyQuestions; s.FamilyDatabasePath = (FamilyDatabasePath ?? "").Trim();
        s.AdjustReminderTimes = AdjustReminderTimes; s.ReminderTimeRules = ParseRules(ReminderTimeRules);
        s.SendCalendarInvites = SendCalendarInvites; s.SendCalendarAlarms = SendCalendarAlarms; s.CalendarIsBusy = CalendarIsBusy;
        s.CalendarSubject = (CalendarSubject ?? "").Trim(); s.Alerts = ParseAlerts(Alerts);
        s.Sms.ServiceName = (SmsServiceName ?? "").Trim(); s.Sms.Sender = (SmsSender ?? "").Trim(); s.Sms.ApplicationKey = (SmsApplicationKey ?? "").Trim();
        s.Sms.ApplicationSecret = (SmsApplicationSecret ?? "").Trim(); s.Sms.ConsumerKey = (SmsConsumerKey ?? "").Trim();
        s.Email.SmtpHost = (SmtpHost ?? "").Trim(); s.Email.SmtpPort = SmtpPort; s.Email.EnableSsl = SmtpSsl; s.Email.UserName = (MailUser ?? "").Trim();
        s.Email.Password = MailPassword ?? ""; s.Email.FromAddress = (MailFrom ?? "").Trim(); s.Email.SenderAddress = s.Email.FromAddress;
        s.Email.FromDisplayName = MailDisplayName ?? ""; s.Email.ImapHost = (ImapHost ?? "").Trim(); s.Email.ImapPort = ImapPort is > 0 ? ImapPort : 993;
        s.Email.BounceFilter = (BounceFilter ?? "").Trim();
        return s;
    }

    public static List<AppointmentTimeRule> ParseRules(string? text)
    {
        var rules = new List<AppointmentTimeRule>();
        foreach (var line in Lines(text))
        {
            var parts = line.Split(new[] { "→", "->", ">", ";", "=" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TryTime(parts[0], out var from) || !TryTime(parts[1], out var to))
                throw new InvalidDataException($"Heure d'arrivée annoncée illisible : « {line} » (attendu « 11:30 → 11:20 »).");
            rules.Add(new AppointmentTimeRule { From = from, To = to });
        }
        return rules;
    }

    public static List<AlertConfiguration> ParseAlerts(string? text)
    {
        var alerts = new List<AlertConfiguration>();
        foreach (var line in Lines(text))
        {
            var parts = line.Split(';', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 0 || minutes > 60 * 24 * 30)
                throw new InvalidDataException($"Alerte illisible : « {line} » (attendu « 1440 ; Rappel la veille », en minutes avant le rendez-vous).");
            alerts.Add(new AlertConfiguration { OffsetMinutes = minutes, Label = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "Alerte" });
        }
        return alerts;
    }

    private static IEnumerable<string> Lines(string? text)
        => (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static bool TryTime(string text, out TimeSpan time)
        => TimeSpan.TryParseExact(text.Replace('h', ':').Replace('H', ':'), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time);
}

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2_000_000)]
public sealed class SmsMailModel(SmsMailServerSettings settings, ISmsMailAgenda agenda, ILogger<SmsMailModel> logger) : PageModel
{
    [BindProperty] public SmsMailForm Form { get; set; } = new();
    public SmsMailSettings? Current { get; private set; }
    public SmsMailSecretsState Secrets { get; private set; } = new();
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public DateTime PostItFrom => DateTime.Today.AddMonths(-6);
    public DateTime PostItTo => DateTime.Today;
    public string Variables => "[PRENOMS], [CABINET], [ADRESSE_CABINET], [DATE], [DATE_LONGUE], [DatejourL], [DatejourC], [HEURE], [LISTE_PERSONNES]";

    public void OnGet() => Load(fillForm: true);

    public async Task<IActionResult> OnPostSaveAsync()
    {
        try
        {
            var next = Form.ApplyTo(settings.Load() ?? new SmsMailSettings());
            await settings.SaveAsync(next);
            logger.LogInformation("Paramètres SMS et mail enregistrés sur le serveur par {User}.", User.Identity?.Name);
            Notice = "Paramètres enregistrés pour tous les postes. Les postes les prennent à la prochaine préparation des messages.";
            Load(fillForm: true);
        }
        catch (InvalidDataException ex) { Error = ex.Message; Load(fillForm: false); }
        return Page();
    }

    /// <summary>Remet le modèle de mail de rappel du cabinet dans le formulaire (à enregistrer ensuite).</summary>
    public IActionResult OnPostDefaultTemplate()
    {
        Load(fillForm: false);
        Form.EmailTemplate = SmsMailSettings.DefaultEmailTemplate;
        ModelState.Remove("Form.EmailTemplate");
        Notice = "Modèle de mail du cabinet appliqué dans le formulaire : vérifiez l'aperçu, puis enregistrez.";
        return Page();
    }

    /// <summary>Importe un fichier .eyeconfig (exporté d'un poste ou du serveur). Un secret absent du fichier garde celui du serveur.</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, string? password)
    {
        try
        {
            if (file is not { Length: > 0 }) throw new InvalidDataException("Choisissez le fichier de configuration (.eyeconfig).");
            if (file.Length > 1_000_000) throw new InvalidDataException("Fichier trop gros pour une configuration SMS et mail.");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            var imported = SmsMailConfigFile.Import(buffer.ToArray(), password ?? "");
            await settings.SaveAsync(imported);
            logger.LogInformation("Configuration SMS et mail importée sur le serveur par {User}.", User.Identity?.Name);
            Notice = "Configuration importée : " + SmsMailConfigFile.Describe(imported) + ". Elle vaut maintenant pour tous les postes.";
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {
            Error = ex.Message;
        }
        Load(fillForm: true);
        return Page();
    }

    /// <summary>Sauvegarde chiffrée des réglages du serveur, secrets compris.</summary>
    public IActionResult OnPostExport(string? password, string? confirm)
    {
        try
        {
            if (password != confirm) throw new InvalidOperationException("Les deux mots de passe sont différents.");
            var current = settings.Load() ?? throw new InvalidOperationException("Aucune configuration SMS et mail sur le serveur.");
            var bytes = SmsMailConfigFile.Export(current, password ?? "");
            logger.LogInformation("Configuration SMS et mail exportée du serveur par {User}.", User.Identity?.Name);
            return File(bytes, "application/octet-stream", $"config-sms-mail-serveur-{DateTime.Now:yyyy-MM-dd}{SmsMailConfigFile.Extension}");
        }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        Load(fillForm: true);
        return Page();
    }

    /// <summary>
    /// Post-it de consultation (export chiffré pour l'analyse) ou rapport des liens familiaux trouvés dans les post-it :
    /// lus par le serveur dans Access, chiffrés avec le mot de passe choisi, pseudonymisés si demandé.
    /// </summary>
    public async Task<IActionResult> OnPostPostItsAsync(DateTime? from, DateTime? to, bool pseudonymize, string? password, string? confirm, string? kind)
    {
        try
        {
            if (from is null || to is null || to < from) throw new InvalidOperationException("Choisissez une période valide.");
            if ((to.Value - from.Value).TotalDays > 400) throw new InvalidOperationException("Période trop longue (13 mois au plus).");
            if ((password ?? "").Length < DiagnosticPackage.MinimumPasswordLength)
                throw new InvalidOperationException($"Le mot de passe doit contenir au moins {DiagnosticPackage.MinimumPasswordLength} caractères.");
            if (password != confirm) throw new InvalidOperationException("Les deux mots de passe sont différents.");
            var read = await agenda.PostItsAsync(from.Value.Date, to.Value.Date);
            var names = read.Names.Select(n => (n.Nom, n.Prenom)).ToList();
            byte[] bytes; string name;
            if (kind == "links")
            {
                var appointments = await agenda.RangeAsync(from.Value.Date, to.Value.Date);
                var json = PostItAnalysis.BuildLinksReportJson(read.PostIts, appointments, names, from.Value, to.Value, pseudonymize);
                bytes = DiagnosticPackage.Encrypt(json, password!, PostItAnalysis.LinksFormat);
                name = $"LiensPostIt_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}{DiagnosticPackage.Extension}";
            }
            else
            {
                if (!read.PostIts.Any(p => PostItAnalysis.IsConsultationPostIt(p.Text)))
                    throw new InvalidOperationException($"{read.PostIts.Count} post-it lus, aucun ne ressemble à un post-it de consultation. Rien n'a été exporté.");
                bytes = DiagnosticPackage.Encrypt(PostItAnalysis.BuildJson(read.PostIts, names, from.Value, to.Value, pseudonymize), password!, PostItAnalysis.Format);
                name = $"PostIt_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}{DiagnosticPackage.Extension}";
            }
            logger.LogInformation("Export des post-it ({Kind}) par {User}.", kind == "links" ? "liens" : "post-it", User.Identity?.Name);
            return File(bytes, "application/octet-stream", name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Data.Common.DbException)
        {
            Error = "Post-it : " + ex.Message;
        }
        Load(fillForm: true);
        return Page();
    }

    private void Load(bool fillForm)
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        try { Current = settings.Load(); }
        catch (System.Security.Cryptography.CryptographicException) { Error = "Configuration du serveur illisible (clés du serveur changées) : importez un fichier .eyeconfig."; }
        var shown = Current ?? new SmsMailSettings();
        Secrets = new SmsMailSecretsState
        {
            SmsApplicationSecret = shown.Sms.ApplicationSecret.Length > 0, SmsConsumerKey = shown.Sms.ConsumerKey.Length > 0, MailPassword = shown.Email.Password.Length > 0
        };
        if (fillForm) Form = SmsMailForm.From(shown);
    }
}
