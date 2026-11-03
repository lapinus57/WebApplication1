using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serveur.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(IConfiguration configuration, ILogger<LoginModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public LoginInput Credentials { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(Url.Page("/Index")!) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var expectedUsername = _configuration["Administration:Username"];
        var expectedPassword = _configuration["Administration:Password"];
        if (string.IsNullOrWhiteSpace(expectedUsername) || string.IsNullOrWhiteSpace(expectedPassword) ||
            // « | » et non « || » : les deux comparaisons sont toujours faites.
            (!SecretEquals(Credentials.Username, expectedUsername) |
             !SecretEquals(Credentials.Password, expectedPassword)))
        {
            _logger.LogWarning("Échec de connexion à l'administration pour {Username}.", Credentials.Username);
            // Ralentit les tentatives répétées sans bloquer le thread.
            await Task.Delay(TimeSpan.FromSeconds(1), HttpContext.RequestAborted);
            ModelState.AddModelError(string.Empty, "Identifiant ou mot de passe incorrect.");
            return Page();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Credentials.Username)], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = Credentials.RememberMe });

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);
        return RedirectToPage("/Index");
    }

    // Comparaison à temps constant : la durée ne révèle pas combien de caractères sont corrects.
    private static bool SecretEquals(string? provided, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Saisissez votre identifiant.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Saisissez votre mot de passe.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
