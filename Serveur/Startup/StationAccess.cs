using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ChatServeur.Startup;

/// <summary>
/// Secrets partagés avec les postes, lus dans appsettings.json (section Administration) :
/// - StationKey : clé que chaque poste doit présenter pour se connecter au hub et lire la
///   configuration de déploiement. Vide = pas de contrôle (comportement historique).
/// - ApplicationPassword : mot de passe des actions d'administration faites depuis un poste
///   (import de configuration, création de groupes). Vide = ancienne valeur par défaut.
/// </summary>
public sealed class StationAccess
{
    public const string LegacyApplicationPassword = "901027";

    private readonly string _stationKey;
    private readonly string _applicationPassword;

    public StationAccess(string? stationKey, string? applicationPassword)
    {
        _stationKey = stationKey?.Trim() ?? string.Empty;
        _applicationPassword = string.IsNullOrWhiteSpace(applicationPassword)
            ? LegacyApplicationPassword
            : applicationPassword.Trim();
    }

    public static StationAccess FromConfiguration(IConfiguration configuration)
        => new(configuration["Administration:StationKey"], configuration["Administration:ApplicationPassword"]);

    public bool RequiresStationKey => _stationKey.Length > 0;

    public bool UsesLegacyApplicationPassword => _applicationPassword == LegacyApplicationPassword;

    public bool IsApplicationPasswordValid(string? candidate) => SecretEquals(candidate, _applicationPassword);

    /// <summary>
    /// Clé envoyée par le poste : en-tête « Authorization: Bearer … » (négociation SignalR)
    /// ou paramètre « access_token » (WebSocket, que le navigateur ne peut pas munir d'en-tête).
    /// </summary>
    public bool IsStationAuthorized(HttpRequest request)
    {
        if (!RequiresStationKey)
            return true;

        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            SecretEquals(header["Bearer ".Length..].Trim(), _stationKey))
            return true;

        return request.Query.TryGetValue("access_token", out var token) && SecretEquals(token.ToString(), _stationKey);
    }

    public static bool IsProtectedPath(PathString path)
        => path.StartsWithSegments("/chatHub") || path.StartsWithSegments("/api/configuration");

    // Comparaison à temps constant : la durée ne révèle pas combien de caractères sont corrects.
    private static bool SecretEquals(string? provided, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
