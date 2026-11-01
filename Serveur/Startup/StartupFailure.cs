using Microsoft.Extensions.Hosting.WindowsServices;
using System.Windows.Forms;

namespace ChatServeur.Startup;

public static class StartupFailure
{
    public static void Report(Exception exception)
    {
        // Never write raw exceptions/configuration: providers may include secrets
        // or database contents in their messages. Only controlled text is shown.
        var reason = exception is StartupConfigurationException
            ? exception.Message
            : $"Le serveur n’a pas pu démarrer ({exception.GetType().Name}, code 0x{exception.HResult:X8}). Vérifiez les ports 5000/5443 et les droits sur le dossier data.";
        var report = $"EyeChat — échec du démarrage\n{DateTimeOffset.Now:O}\n\n{reason}";
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "Serveur", "Logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "dernier-demarrage-erreur.txt"), report);
        }
        catch { /* Reporting must not mask the original startup failure. */ }
        try { Console.Error.WriteLine(report); } catch { }
        if (OperatingSystem.IsWindows() && Environment.UserInteractive && !WindowsServiceHelpers.IsWindowsService())
        {
            try { MessageBox.Show(reason, "EyeChat — serveur arrêté", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch { }
        }
    }
}
