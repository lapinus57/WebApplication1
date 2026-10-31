using ChatServeur.Startup;
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
        Check(await client.GetStringAsync(address + "/probe") == "certificate-ok", "Kestrel completes HTTPS using the in-memory key");
        GC.Collect(); GC.WaitForPendingFinalizers();
        Check(await client.GetStringAsync(address + "/probe") == "certificate-ok", "Certificate survives for the host lifetime");
    }
    finally { await app.StopAsync(); }
}
finally { Directory.Delete(folder, recursive: true); }
Console.WriteLine($"{checks} server startup checks passed; synthetic certificate, loopback HTTPS only.");
