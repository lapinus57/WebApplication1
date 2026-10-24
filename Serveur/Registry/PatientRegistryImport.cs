namespace ChatServeur.Registry;

public sealed record RegistryImportStatus(bool Running, int Processed, string Message, bool Failed);

// One background import per server. A failed/cancelled scan never replaces the previous registry.
public sealed class PatientRegistryImport(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime) : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private RegistryImportStatus status = new(false, 0, "Aucun import en cours.", false);
    public RegistryImportStatus Status { get { lock (gate) return status; } }
    public bool Start()
    {
        lock (gate)
        {
            if (status.Running) return false;
            cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            status = new(true, 0, "Lecture de tous les patients dans Access…", false);
            var token = cancellation.Token;
            _ = Task.Run(() => RunAsync(token)); return true;
        }
    }
    public void Cancel() { lock (gate) cancellation?.Cancel(); }
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var count = await scope.ServiceProvider.GetRequiredService<PatientRegistryStore>().RefreshAllAsync(
                count => { lock (gate) status = new(true, count, $"{count} patient(s) analysé(s)…", false); }, ct);
            lock (gate) status = new(false, count, $"Import terminé : {count} patient(s).", false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { lock (gate) status = status with { Running = false, Message = "Import interrompu. Le registre précédent est conservé.", Failed = false }; }
        catch (Exception ex)
        {
            // Provider/parser exceptions can contain clinical text: never log or display their raw details.
            lock (gate) status = status with { Running = false, Failed = true, Message = ex is EyeChat.Fundus.FoDataException
                ? ex.Message : "Import échoué. Vérifiez Access et les droits du serveur. Le registre précédent est conservé." };
        }
    }
    public void Dispose() { lock (gate) { cancellation?.Cancel(); cancellation?.Dispose(); } }
}
