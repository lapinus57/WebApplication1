using ChatServeur.SmsMail;
using Client.SmsMail.Configuration;
using Client.SmsMail.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2_000_000)]
public sealed class SmsMailModel(SmsMailServerSettings settings, ILogger<SmsMailModel> logger) : PageModel
{
    public SmsMailSettings? Current { get; private set; }
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";

    public void OnGet() => Load();

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
        Load();
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
        Load();
        return Page();
    }

    private void Load()
    {
        Response.Headers["Cache-Control"] = "no-store, private";
        try { Current = settings.Load(); }
        catch (System.Security.Cryptography.CryptographicException) { Error = "Configuration du serveur illisible (clés du serveur changées) : importez un fichier .eyeconfig."; }
    }
}
