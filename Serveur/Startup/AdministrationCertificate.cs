using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ChatServeur.Startup;

public sealed class StartupConfigurationException(string message) : Exception(message);

public static class AdministrationCertificate
{
    // The caller owns this certificate until Kestrel has stopped. Never persist
    // the private key in a Windows user/machine key container.
    public static X509Certificate2 Load(string path, string? password)
    {
        try
        {
            var certificate = new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet);
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
            throw new StartupConfigurationException("Impossible de charger le certificat HTTPS et sa clé privée en mémoire. Vérifiez le fichier PFX, son mot de passe et les droits de lecture du compte serveur. Une variable Administration__HttpsCertificatePassword peut remplacer appsettings.json.");
        }
    }
}
