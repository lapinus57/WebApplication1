using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using EyeChat.Fundus;

namespace Client.Fundus;

public static class LocalRuleStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "Fundus", "rules.dat");
    public static LocalRulePackage Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            if (new FileInfo(FilePath).Length > 128000) throw new InvalidDataException();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            try
            {
                var package = JsonSerializer.Deserialize<LocalRulePackage>(bytes) ?? throw new JsonException();
                LocalExamRules.ValidatePackage(package); return package;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch { throw new FoDataException("Les règles locales sont illisibles. Le fichier n’a pas été remplacé. Vérifiez le profil Windows."); }
    }

    public static void Save(LocalRulePackage package)
    {
        LocalExamRules.ValidatePackage(package);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(package);
        try
        {
            if (bytes.Length > 64000) throw new FoDataException("Le fichier de règles dépasse 64 Ko.");
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
