namespace ChatServeur.EyeMail;

public sealed record EyeMailImportStatus(bool Running, int Processed, string Message, bool Failed);

/// <summary>Un seul import à la fois, en arrière-plan : fermer la page ne l'interrompt pas.</summary>
public sealed class EyeMailImport(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<EyeMailImport> logger) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private EyeMailImportStatus _status = new(false, 0, "Aucun import en cours.", false);

    public EyeMailImportStatus Status { get { lock (_gate) return _status; } }

    public bool Start()
    {
        lock (_gate)
        {
            if (_status.Running) return false;
            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            _status = new(true, 0, "Lecture des patients dans Access…", false);
            var token = _cancellation.Token;
            _ = Task.Run(() => RunAsync(token));
            return true;
        }
    }

    public void Cancel() { lock (_gate) _cancellation?.Cancel(); }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<IEyeMailSource>();
            var data = source.ReadPatients(count => { lock (_gate) _status = new(true, count, $"{count} patient(s) lu(s) dans Access…", false); }, ct);
            lock (_gate) _status = new(true, data.Patients.Count, $"Enregistrement de {data.Patients.Count} patient(s)…", false);
            var run = await scope.ServiceProvider.GetRequiredService<EyeMailStore>().SyncAsync(data, ct);
            logger.LogInformation("Import EyeMail terminé : {Read} lus, {Added} ajoutés, {Updated} modifiés, {Deactivated} inactifs.",
                run.Read, run.Added, run.Updated, run.Deactivated);
            lock (_gate) _status = new(false, run.Read,
                $"Import terminé : {run.Read} patient(s), {run.Added} nouveau(x), {run.Updated} modifié(s), {run.Deactivated} absent(s) d'Access.", false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate) _status = _status with { Running = false, Message = "Import interrompu. La base précédente est conservée.", Failed = false };
        }
        catch (Exception ex) when (ex is InvalidDataException or EyeChat.Fundus.FoDataException)
        {
            lock (_gate) _status = _status with { Running = false, Failed = true, Message = ex.Message };
        }
        catch (Exception ex)
        {
            // Pas de détail brut à l'écran : un message de pilote peut contenir des données patient.
            logger.LogError("SER24: Import EyeMail échoué ({Type}).", ex.GetType().Name);
            lock (_gate) _status = _status with { Running = false, Failed = true,
                Message = "Import échoué. Vérifiez l'accès du serveur à la base Access. La base précédente est conservée." };
        }
    }

    public void Dispose() { lock (_gate) { _cancellation?.Cancel(); _cancellation?.Dispose(); } }
}
