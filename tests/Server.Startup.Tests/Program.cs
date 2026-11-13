using ChatServeur;
using ChatServeur.Startup;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
var folder = Path.Combine(Path.GetTempPath(), "EyeChat-Startup-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
    using var source = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    var password = Guid.NewGuid().ToString("N"); // Synthetic, never a user certificate.
    var path = Path.Combine(folder, "synthetic.pfx");
    File.WriteAllBytes(path, source.Export(X509ContentType.Pfx, password));
    using var certificate = AdministrationCertificate.Load(path, password);
    Check(certificate.HasPrivateKey, "PFX private key is available");
    File.Delete(path);
    using (var loadedKey = certificate.GetRSAPrivateKey())
    {
        var data = new byte[] { 1, 2, 3 };
        var signature = loadedKey!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Check(key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Key remains usable without PFX file");
    }
    File.WriteAllBytes(path, source.Export(X509ContentType.Pfx, password));
    try { using var invalid = AdministrationCertificate.Load(path, "wrong"); throw new Exception("Wrong password accepted"); }
    catch (StartupConfigurationException ex) { Check(!ex.Message.Contains(password), "Failure contains no password"); }
    File.WriteAllBytes(path, source.Export(X509ContentType.Cert));
    try { using var invalid = AdministrationCertificate.Load(path, null); throw new Exception("Public-only certificate accepted"); }
    catch (StartupConfigurationException ex) { Check(ex.Message.Contains("clé privée"), "Missing key is explained"); }
    try { using var invalid = AdministrationCertificate.Load(Path.Combine(folder, "missing.pfx"), null); throw new Exception("Missing file accepted"); }
    catch (StartupConfigurationException) { checks++; }

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
    await using var app = builder.Build();
    app.MapGet("/probe", () => "certificate-ok");
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented?.Thumbprint == certificate.Thumbprint
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        Check(await client.GetStringAsync(address + "/probe") == "certificate-ok", "Kestrel completes HTTPS using the account-scoped key");
        GC.Collect(); GC.WaitForPendingFinalizers();
        Check(await client.GetStringAsync(address + "/probe") == "certificate-ok", "Certificate survives for the host lifetime");
    }
    finally { await app.StopAsync(); }
}
finally { Directory.Delete(folder, recursive: true); }
// Mots de passe des groupes protégés : nouveau format, ancien format toujours accepté.
var hashed = PasswordHelper.HashPassword("groupe-secret");
Check(hashed.StartsWith("v2:"), "New group hashes use the versioned format");
Check(PasswordHelper.VerifyPassword("groupe-secret", hashed), "New hash accepts the right password");
Check(!PasswordHelper.VerifyPassword("groupe-secreT", hashed), "New hash rejects a wrong password");
var legacySalt = RandomNumberGenerator.GetBytes(16);
var legacyHash = KeyDerivation.Pbkdf2("ancien", legacySalt, KeyDerivationPrf.HMACSHA256, 10000, 32);
var legacy = $"{Convert.ToBase64String(legacySalt)}:{Convert.ToBase64String(legacyHash)}";
Check(PasswordHelper.VerifyPassword("ancien", legacy), "Legacy hash still accepted");
Check(!PasswordHelper.VerifyPassword("autre", legacy), "Legacy hash rejects a wrong password");
Check(!PasswordHelper.VerifyPassword("x", "pas-du-base64:!!"), "Corrupted hash is rejected without exception");
Check(!PasswordHelper.VerifyPassword("x", ""), "Empty hash is rejected");

// Avatars : seules de vraies images, extension déduite du contenu.
var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
Check(AvatarUpload.TryDecode(Convert.ToBase64String(png), out var pngBytes, out var pngExt, out _) && pngExt == ".png" && pngBytes.Length == png.Length, "PNG avatar accepted");
Check(AvatarUpload.TryDecode("data:image/png;base64," + Convert.ToBase64String(png), out _, out _, out _), "Data URL prefix accepted");
Check(AvatarUpload.TryDecode(Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 }), out _, out var jpgExt, out _) && jpgExt == ".jpg", "JPEG avatar accepted");
Check(!AvatarUpload.TryDecode(Convert.ToBase64String("<html><script>alert(1)</script>"u8.ToArray()), out _, out _, out _), "HTML disguised as avatar rejected");
Check(!AvatarUpload.TryDecode("@@@ pas du base64 @@@", out _, out _, out var badError) && badError.Length > 0, "Invalid base64 rejected with a message");
Check(!AvatarUpload.TryDecode(Convert.ToBase64String(new byte[AvatarUpload.MaxBytes + 1]), out _, out _, out _), "Oversized avatar rejected");
Check(!AvatarUpload.TryDecode("", out _, out _, out _), "Empty avatar rejected");

// Sauvegarde quotidienne de la base : copie cohérente, une seule par jour, anciennes supprimées.
var backupRoot = Path.Combine(Path.GetTempPath(), "EyeChat-Backup-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(backupRoot);
try
{
    var dbFile = Path.Combine(backupRoot, "chat.db");
    using (var live = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbFile};Pooling=False"))
    {
        live.Open();
        using var cmd = live.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Messages (Content TEXT); INSERT INTO Messages VALUES ('synthétique');";
        cmd.ExecuteNonQuery();
        // Connexion laissée ouverte : la sauvegarde doit fonctionner pendant que le serveur tourne.
        var backupFolder = Path.Combine(backupRoot, "sauvegardes");
        var first = DatabaseBackup.RunIfDue(dbFile, backupFolder, new DateTime(2026, 11, 13));
        Check(first != null && File.Exists(first), "Daily backup created while the database is open");
        using (var copy = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={first};Mode=ReadOnly;Pooling=False"))
        {
            copy.Open();
            using var read = copy.CreateCommand();
            read.CommandText = "SELECT Content FROM Messages";
            Check((string?)read.ExecuteScalar() == "synthétique", "Backup contains the data written in WAL mode");
        }
        Check(DatabaseBackup.RunIfDue(dbFile, backupFolder, new DateTime(2026, 11, 13)) == null, "Only one backup per day");
        for (var day = 14; day <= 30; day++)
            DatabaseBackup.RunIfDue(dbFile, backupFolder, new DateTime(2026, 11, day), keep: 5);
        var kept = Directory.GetFiles(backupFolder, "chat-*.db").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Check(kept.Count == 5 && kept[0] == "chat-2026-11-26.db" && kept[^1] == "chat-2026-11-30.db", "Only the most recent backups are kept");
        Check(!Directory.GetFiles(backupFolder, "*.tmp").Any(), "No temporary file left behind");
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(backupRoot, recursive: true);
}

Console.WriteLine($"{checks} server startup checks passed; synthetic certificate, loopback HTTPS only.");
