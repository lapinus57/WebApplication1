namespace ChatServeur.Startup;

/// <summary>
/// Trouve le dossier wwwroot (feuilles de style, scripts, images) des pages d'administration.
/// Une simple compilation (Visual Studio, dotnet build) ne copie pas wwwroot à côté de Serveur.exe :
/// seul « dotnet publish » le fait. Lancé depuis bin\…, le serveur affichait alors les pages
/// sans aucune mise en forme. On cherche donc aussi le wwwroot du projet dans les dossiers parents.
/// </summary>
public static class WebRootLocator
{
    public const string FolderName = "wwwroot";
    private const string MarkerFile = "css/site.css";

    /// <summary>Chemin du wwwroot à utiliser, ou null s'il est introuvable.</summary>
    public static string? Find(string baseDirectory, int maxParentLevels = 8)
    {
        var beside = Path.Combine(baseDirectory, FolderName);
        if (IsWebRoot(beside)) return beside;

        // bin\x86\Release\net8.0-windows…\win-x86\ → dossier du projet Serveur.
        var directory = new DirectoryInfo(baseDirectory).Parent;
        for (int level = 0; directory != null && level < maxParentLevels; level++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FolderName);
            if (File.Exists(Path.Combine(directory.FullName, "Serveur.csproj")) && IsWebRoot(candidate))
                return candidate;
        }
        return null;
    }

    public static bool IsWebRoot(string path) => File.Exists(Path.Combine(path, MarkerFile));

    public static string MissingMessage(string baseDirectory) =>
        $"SER27: dossier {FolderName} introuvable à côté de {Path.Combine(baseDirectory, "Serveur.exe")} : "
        + "les pages d'administration s'afficheront sans mise en forme. Installez le serveur à partir du dossier "
        + "« publish » (dotnet publish ou ChatServeurService.ps1), ou copiez le dossier wwwroot du projet à côté de Serveur.exe.";
}
