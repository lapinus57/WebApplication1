using ChatServeur.ClientUpdate;
using EyeChat.ClientUpdate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClientsModel(ClientVersionRegistry registry, IConfiguration configuration) : PageModel
{
    [BindProperty] public string UpdateFolder { get; set; } = "";
    public IReadOnlyList<ClientVersionEntry> Entries { get; private set; } = Array.Empty<ClientVersionEntry>();
    public ClientUpdateInfo Info { get; private set; } = new();
    public ClientPackage? Package { get; private set; }
    public bool FolderReadable { get; private set; }
    public string Notice { get; private set; } = "";

    public string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        TimeZoneInfo.FindSystemTimeZoneById(configuration["FoAccess:TimeZoneId"] ?? "Europe/Paris"))
        .ToString("g", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    public void OnGet() { NoCache(); Load(); }

    public IActionResult OnPost()
    {
        NoCache();
        registry.UpdateFolder = UpdateFolder;
        Notice = UpdateFolder.Trim().Length == 0 ? "Dossier des mises à jour effacé." : "Dossier des mises à jour enregistré. Les postes le reçoivent à leur prochaine connexion.";
        Load();
        return Page();
    }

    private void Load()
    {
        Entries = registry.Entries();
        Info = registry.Info(DateTime.UtcNow);
        UpdateFolder = registry.UpdateFolder;
        try { FolderReadable = UpdateFolder.Length > 0 && Directory.Exists(UpdateFolder); } catch { FolderReadable = false; }
        Package = ClientVersions.FindLatest(UpdateFolder);
    }

    private void NoCache()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
