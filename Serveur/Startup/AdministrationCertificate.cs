using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ChatServeur.Startup;

public sealed class StartupConfigurationException(string message) : Exception(message);

public static class AdministrationCertificate
{
    // The caller owns this certificate until Kestrel has stopped. Schannel needs
    // a key container on Windows: explicitly use the launching account profile,
    // never the machine store. No PersistKeySet: disposal releases the imported key.
    public static X509Certificate2 Load(string path, string? password)
    {
        try
        {
            var flags = OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.UserKeySet
                : X509KeyStorageFlags.EphemeralKeySet;
            var certificate = new X509Certificate2(path, password, flags);
            if (certificate.HasPrivateKey) return certificate;
            certificate.Dispose();
            throw new StartupConfigurationException("Le certificat HTTPS ne contient pas de clé privée. Fournissez un fichier PFX avec sa clé privée.");
        }
        catch (FileNotFoundException)
        {
            throw new StartupConfigurationException("Le fichier PFX est introuvable. Vérifiez Administration:HttpsCertificatePath.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new StartupConfigurationException("Le dossier du certificat PFX est introuvable. Vérifiez Administration:HttpsCertificatePath.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new StartupConfigurationException("Lecture du fichier PFX refusée. Accordez la lecture du fichier au compte qui exécute le serveur, sans ouvrir les droits à tous les utilisateurs.");
        }
        catch (CryptographicException)
        {
            throw new StartupConfigurationException("Impossible de charger le certificat HTTPS et sa clé privée dans le profil du compte serveur. Vérifiez le fichier PFX, son mot de passe les droits de lecture du compte serveur et l’accès en écriture à son profil. Une variable Administration__HttpsCertificatePassword peut remplacer appsettings.json.");
        }
    }
}
