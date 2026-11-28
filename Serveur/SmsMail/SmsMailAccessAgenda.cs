using System.Text.Json;
using ChatServeur.Fundus;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using Microsoft.EntityFrameworkCore;

namespace ChatServeur.SmsMail;

/// <summary>Agenda lu par le serveur : base Access des réglages du serveur, table et colonnes des paramètres RDV.</summary>
public sealed class SmsMailAccessAgenda(FoAccessReader access, ChatDbContext db) : ISmsMailAgenda
{
    private async Task<AgendaSource> SourceAsync()
    {
        var json = await db.ServerConfigs.AsNoTracking().Select(c => c.AppointmentSearchJson).FirstOrDefaultAsync();
        AppointmentSearchConfig config;
        try { config = string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<AppointmentSearchConfig>(json) ?? new(); }
        catch (JsonException) { config = new(); }
        return new AgendaSource(access.AccessOptions(), config.TableName, config.DateColumn, config.TimeColumn);
    }

    public async Task<List<RdvInfo>> DayAsync(DateTime day) => await AppointmentLoader.LoadAsync(await SourceAsync(), day);
    public async Task<List<RdvInfo>> RangeAsync(DateTime from, DateTime to) => await AppointmentLoader.LoadRangeAsync(await SourceAsync(), from, to);
    public async Task<List<AgendaVisit>> HistoryAsync(DateTime today) => AppointmentLoader.Flatten(await AppointmentLoader.LoadHistoryAsync(await SourceAsync(), today));

    public async Task<SmsMailPostIts> PostItsAsync(DateTime from, DateTime to)
    {
        var (postIts, names) = await AppointmentLoader.LoadPostItsAsync(await SourceAsync(), from, to);
        return new SmsMailPostIts { PostIts = postIts, Names = names.Select(n => new SmsMailName { Nom = n.Nom, Prenom = n.Prenom }).ToList() };
    }
}
