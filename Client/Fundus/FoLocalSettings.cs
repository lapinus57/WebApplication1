using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using EyeChat.Fundus;

namespace Client.Fundus;

// Per Windows account. No names, excerpts, full observations or Access credentials are persisted.
public sealed class FoLocalSettings
{
    public FoMapping? Mapping { get; set; }
    public Dictionary<string, FoReview> Reviews { get; set; } = new();

    private static string FilePath(string databasePath) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "Fundus",
        FundusAnalysis.Hash(Path.GetFullPath(databasePath).ToUpperInvariant()) + ".dat");

    public static FoLocalSettings Load(string databasePath)
    {
        var path = FilePath(databasePath);
        if (!File.Exists(path)) return new();
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<FoLocalSettings>(bytes) ?? throw new JsonException(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch
        {
            throw new FoDataException("Les réglages FO locaux sont illisibles. Vérifiez l’accès au profil Windows ; ils n’ont pas été remplacés.");
        }
    }

    public void Save(string databasePath)
    {
        var path = FilePath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encrypted);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
