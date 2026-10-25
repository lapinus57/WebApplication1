using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Client.Helpers;

// Read registration metadata only. Never instantiate an OLE DB/Office COM object
// or open a database just to diagnose a broken native Access installation.
internal static class AccessRuntimeDiagnostics
{
    public static string ClientDescription => $"Client {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} ({IntPtr.Size * 8} bits)";

    public static string Describe(string? configuredProvider)
    {
        var configured = (configuredProvider ?? "").Trim();
        bool valid = configured.Length is > 0 and <= 128
            && Regex.IsMatch(configured, @"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var output = new StringBuilder();
        output.AppendLine(ClientDescription);
        output.AppendLine("Windows " + (Environment.Is64BitOperatingSystem ? "64" : "32") + " bits");
        output.AppendLine("Fournisseur configuré : " + (valid ? configured : "non renseigné ou format invalide"));
        output.AppendLine();
        output.AppendLine("Déclarations des moteurs Access dans Windows :");
        IEnumerable<string> providers = new[] { "Microsoft.ACE.OLEDB.12.0", "Microsoft.ACE.OLEDB.16.0", "Microsoft.Jet.OLEDB.4.0" };
        if (valid) providers = new[] { configured }.Concat(providers);
        foreach (var provider in providers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            output.AppendLine(provider);
            output.AppendLine("  32 bits : " + Registration(provider, RegistryView.Registry32));
            if (Environment.Is64BitOperatingSystem)
                output.AppendLine("  64 bits : " + Registration(provider, RegistryView.Registry64));
        }
        output.AppendLine();
        output.AppendLine("Les fichiers des pilotes ne sont vérifiés que pour l’architecture du processus en cours.");
        output.AppendLine("La présence d’une déclaration et du fichier ne prouve pas que le moteur fonctionne. Ce diagnostic ne lance ni Office ni connexion Access.");
        output.AppendLine("Le moteur doit être compatible avec l’architecture du client en cours, même sur Windows 64 bits. Pour Office/Access 32 bits, utiliser le client x86 avec ACE 32 bits. Jet concerne les bases .mdb et les processus x86 uniquement.");
        output.AppendLine("Si le client est déjà x86 avec Office/Access 32 bits et que le message « système d’exploitation non configuré » persiste : fermer EyeChat et Office, puis utiliser Paramètres Windows → Applications → Microsoft Office/Access → Modifier → Réparation rapide (si proposée). Réessayer après la réparation ; si nécessaire, faire réparer l’installation Access Runtime/ACE existante par l’administrateur du poste.");
        output.AppendLine("Aucune donnée patient, aucun chemin de base et aucun mot de passe ne figurent dans ce diagnostic.");
        return output.ToString();
    }

    private static string Registration(string provider, RegistryView view)
    {
        try
        {
            using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var program = classes.OpenSubKey(provider + @"\CLSID", writable: false);
            if (program?.GetValue(null) is not string raw || !Guid.TryParse(raw, out var id)) return "non déclaré";
            using var server = classes.OpenSubKey(@"CLSID\" + id.ToString("B") + @"\InprocServer32", writable: false);
            if (server?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path || string.IsNullOrWhiteSpace(path)) return "déclaration incomplète";
            // Environment expansion and filesystem redirection follow this process.
            // Do not report a missing DLL when inspecting the other registry view.
            if ((view == RegistryView.Registry64) != Environment.Is64BitProcess)
                return "déclaré (autre architecture ; fichier non vérifié)";
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return File.Exists(path) ? "déclaré ; fichier du pilote présent" : "déclaré ; fichier du pilote absent ou inaccessible";
        }
        catch
        {
            // Registry and filesystem exceptions may contain local account paths.
            return "vérification indisponible (droits ou installation)";
        }
    }
}
