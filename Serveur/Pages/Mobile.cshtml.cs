using ChatServeur;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Serveur.Pages;

public class MobileModel : PageModel
{
    private readonly ChatDbContext _db;

    public MobileModel(ChatDbContext db) => _db = db;

    public MobileSnapshot Snapshot { get; private set; } = new([], [], DateTime.UtcNow);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Snapshot = await CreateSnapshotAsync();
    }

    public async Task<IActionResult> OnGetSnapshotAsync()
    {
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(await CreateSnapshotAsync());
    }

    private async Task<MobileSnapshot> CreateSnapshotAsync()
    {
        var startOfDay = DateTime.Today;
        var patients = await _db.Patients.AsNoTracking()
            .Where(patient => !patient.IsArchived)
            .OrderBy(patient => patient.IsTaken)
            .ThenBy(patient => patient.HoldTime)
            .Select(patient => new MobilePatient(
                patient.Id, patient.Title, patient.FirstName, patient.LastName,
                patient.Exams, patient.Eye, patient.Position, patient.Annotation,
                patient.HoldTime, patient.IsTaken, patient.Colors))
            .ToListAsync();

        var messages = await _db.Messages.AsNoTracking()
            .Where(message => !message.IsDeleted && message.Timestamp >= startOfDay)
            .OrderByDescending(message => message.Timestamp)
            .Take(100)
            .Select(message => new MobileMessage(
                message.Id, message.Sender, message.Destinataire, message.Room,
                message.Content, message.Timestamp))
            .ToListAsync();

        return new MobileSnapshot(patients, messages, DateTime.UtcNow);
    }

    public sealed record MobileSnapshot(
        IReadOnlyList<MobilePatient> Patients,
        IReadOnlyList<MobileMessage> Messages,
        DateTime UpdatedAtUtc);

    public sealed record MobilePatient(
        string Id, string Title, string FirstName, string LastName, string Exams,
        string Eye, string Position, string Annotation, DateTime HoldTime,
        bool IsTaken, string Colors);

    public sealed record MobileMessage(
        int Id, string Sender, string Destinataire, string Room,
        string Content, DateTime Timestamp);
}
