using ChatServeur.Fundus;
using ChatServeur.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccessSettingsModel(ServerAccessSettingsStore store, FoAccessReader access, IConfiguration configuration, ILogger<AccessSettingsModel> logger) : PageModel
{
    [BindProperty] public string DatabasePath { get; set; } = "";
    [BindProperty] public string Provider { get; set; } = "";
    [BindProperty] public string WorkgroupPath { get; set; } = "";
    [BindProperty] public string UserName { get; set; } = "";
    [BindProperty] public string? NewPassword { get; set; }
    [BindProperty] public bool ClearPassword { get; set; }

    public bool HasPassword { get; private set; }
    public (string Path, string Origin, bool Exists) Source { get; private set; } = ("", "", false);
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string TestResult { get; private set; } = "";
    public bool TestFailed { get; private set; }

    public void OnGet() => Load();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        NoCache();
        if (new[] { DatabasePath, Provider, WorkgroupPath, UserName }.Any(v => (v ?? "").Length > 500) || (NewPassword ?? "").Length > 200)
        {
            Error = "Valeur trop longue.";
            Load(keepForm: true);
            return Page();
        }
        await store.SaveAsync(DatabasePath ?? "", Provider ?? "", WorkgroupPath ?? "", UserName ?? "", NewPassword, ClearPassword, HttpContext.RequestAborted);
        logger.LogInformation("Réglages Access du serveur modifiés par {User}.", User.Identity?.Name);
        Notice = "Réglages enregistrés. Ils servent dès maintenant à l'import EyeMail, à la préparation FO et au registre patients.";
        Load();
        return Page();
    }

    public IActionResult OnPostTest()
    {
        Load();
        try
        {
            TestResult = access.TestConnection(configuration["EyeMail:PatientsTable"] ?? "Patients");
            TestFailed = !TestResult.StartsWith("Connexion réussie (", StringComparison.Ordinal) || TestResult.Contains("mais", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // Erreurs du pilote ou d'accès au fichier : elles ne contiennent pas de données patient.
            TestFailed = true;
            TestResult = "Échec de la connexion : " + ex.Message;
        }
        return Page();
    }

    private void Load(bool keepForm = false)
    {
        NoCache();
        var saved = store.Load();
        HasPassword = !string.IsNullOrEmpty(saved.ProtectedPassword);
        if (!keepForm)
        {
            DatabasePath = saved.DatabasePath;
            Provider = saved.Provider;
            WorkgroupPath = saved.WorkgroupPath;
            UserName = saved.UserName;
            NewPassword = null;
            ClearPassword = false;
        }
        try { Source = access.DescribeSource(); } catch { Source = ("", "illisible", false); }
    }

    private void NoCache()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
