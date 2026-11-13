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
        catch (Exception ex)
        {
            var message = Describe(ex);
            logger.LogError("SER24: Import EyeMail échoué ({Type}) : {Message}", ex.GetType().Name, message);
            lock (_gate) _status = _status with { Running = false, Failed = true, Message = message };
        }
    }

    /// <summary>
    /// Message affiché quand l'import échoue. Les erreurs d'Access et du pilote (fichier, droits,
    /// pilote absent) sont montrées telles quelles : elles ne contiennent pas de données patient.
    /// </summary>
    public static string Describe(Exception ex)
    {
        const string kept = " La base précédente est conservée.";
        var bits = Environment.Is64BitProcess ? "64" : "32";
        return ex switch
        {
            InvalidDataException or EyeChat.Fundus.FoDataException => ex.Message + kept,
            InvalidOperationException when ex.Message.Contains("fournisseur Access", StringComparison.OrdinalIgnoreCase)
                => $"{ex.Message} Le serveur tourne actuellement en {bits} bits : le pilote Access (ACE) doit être installé en {bits} bits, "
                   + "ou le serveur lancé dans l'autre architecture (version x86 pour un Office 32 bits)." + kept,
            InvalidOperationException when ex.Message.Contains("chemin de la base Access", StringComparison.OrdinalIgnoreCase) => ex.Message + kept,
            System.Data.Common.DbException => $"Access a refusé la lecture : {ex.Message.Trim()}" + kept,
            UnauthorizedAccessException => "Le compte du serveur n'a pas le droit de lire la base Access (ou son dossier)." + kept,
            IOException => $"La base Access n'a pas pu être ouverte : {ex.Message.Trim()}" + kept,
            _ => $"Import échoué ({ex.GetType().Name}). Vérifiez l'accès du serveur à la base Access." + kept,
        };
    }

    public void Dispose() { lock (_gate) { _cancellation?.Cancel(); _cancellation?.Dispose(); } }
}
