using System.Text.Json;
using ChatServeur.Fundus;
using EyeChat.Data;
using EyeChat.Families;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.Families;

public sealed record FamilyRebuildStatus(bool Running, string Message, bool Failed, DateTime? StartedUtc);

/// <summary>
/// Recalcul complet des familles (tout l'historique de l'agenda, en lecture seule), un seul à la fois, en arrière-plan :
/// chaque nuit et à la demande depuis la page d'administration. Le résultat précédent reste en service jusqu'à la fin.
/// </summary>
public sealed class FamilyRebuild(IServiceScopeFactory scopes, FamilyStore store, IConfiguration configuration,
    IHostApplicationLifetime lifetime, ILogger<FamilyRebuild> logger) : BackgroundService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private FamilyRebuildStatus _status = new(false, "Aucun calcul en cours.", false, null);

    public FamilyRebuildStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Heure locale du recalcul nocturne (Families:NightlyHour, 3 h par défaut ; -1 pour le désactiver).</summary>
    private int NightlyHour => int.TryParse(configuration["Families:NightlyHour"], out var h) ? h : 3;

    public bool Start()
    {
        lock (_gate)
        {
            if (_status.Running) return false;
            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            _status = new(true, "Lecture de l'agenda…", false, DateTime.UtcNow);
            var token = _cancellation.Token;
            _ = Task.Run(() => RunAsync(token));
            return true;
        }
    }

    public void Cancel() { lock (_gate) _cancellation?.Cancel(); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var hour = NightlyHour;
            if (hour < 0 || hour > 23) { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); continue; }
            var now = DateTime.Now;
            var next = now.Date.AddHours(hour);
            if (next <= now) next = next.AddDays(1);
            await Task.Delay(next - now, stoppingToken);
            if (Start()) logger.LogInformation("Recalcul nocturne des familles démarré.");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var access = scope.ServiceProvider.GetRequiredService<FoAccessReader>();
            var options = new FamilyPipelineOptions
            {
                Table = configuration["Families:AgendaTable"] is { Length: > 0 } t ? t : await AgendaTableAsync(scope, ct),
                PatientsTable = configuration["Families:PatientsTable"] is { Length: > 0 } p ? p : null,
                Vitale = store.LoadVitale(),
                Payments = store.LoadPayments(),
                Decisions = store.LoadDecisions(),
            };
            var columns = new List<string>();
            var result = await Task.Run(() =>
            {
                using var connection = ReadOnlyAccess.Open(access.AccessOptions());
                return FamilyPipeline.Run(connection, options, line =>
                {
                    if (line.Contains('→')) columns.Add(string.Join(" ", line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                    lock (_gate) _status = _status with { Message = line.Trim() };
                }, ct);
            }, ct);
            store.SetMeta("agenda_columns", string.Join(" ; ", columns));
            lock (_gate) _status = _status with { Message = "Enregistrement des familles…" };
            await store.SaveResultAsync(result, FamilyScanReport.Anonymous(result));
            var message = $"Calcul terminé : {result.Families.Count} familles ({result.Families.Sum(f => f.Members.Count)} patients), "
                + $"{result.Families.Count(f => f.ToReview)} à vérifier.";
            store.SetMeta("last_run_message", message);
            logger.LogInformation("Familles : {Message}", message);
            lock (_gate) _status = new(false, message, false, _status.StartedUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate) _status = _status with { Running = false, Message = "Calcul interrompu. Les familles précédentes sont conservées." };
        }
        catch (Exception ex)
        {
            // Les erreurs d'Access (fichier, droits, pilote, colonne) ne contiennent pas de données patient.
            var message = $"Calcul échoué : {ex.Message.Trim()} Les familles précédentes sont conservées.";
            logger.LogError("SER30: Recalcul des familles échoué ({Type}) : {Message}", ex.GetType().Name, ex.Message);
            store.SetMeta("last_run_message", message);
            lock (_gate) _status = _status with { Running = false, Failed = true, Message = message };
        }
    }

    /// <summary>Table de l'agenda choisie dans les paramètres RDV, sinon AG_rdv.</summary>
    private static async Task<string> AgendaTableAsync(IServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var json = await db.ServerConfigs.Select(c => c.AppointmentSearchJson).FirstOrDefaultAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<AppointmentSearchConfig>(json) is { TableName.Length: > 0 } config)
                return config.TableName;
        }
        catch (JsonException) { }
        return "AG_rdv";
    }

    public override void Dispose()
    {
        lock (_gate) { _cancellation?.Cancel(); _cancellation?.Dispose(); }
        base.Dispose();
    }
}
